using System.Globalization;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Artists.Data;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Artists.Business;

/// <summary>
/// The artist rules: slugs unique and locked once published, status changes only along the lifecycle, a portrait
/// with alt text before publishing, and every write checked against the row version the editor read.
/// </summary>
internal sealed class ArtistsBusiness(IArtistsDataLayer dataLayer, IClock clock) : IArtistsBusiness
{
    private const string What = "this artist";

    public async Task<OperationResult<CursorPage<ArtistSummaryServiceModel>>> ListAsync(
        ArtistListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var scope = string.Create(CultureInfo.InvariantCulture, $"artists|status={request.Status}|search={search}");

        ArtistListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded || !Guid.TryParseExact(decoded.Value!.TieBreaker, "N", out var afterId))
            {
                return OperationResult.Failure<CursorPage<ArtistSummaryServiceModel>>(
                    OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));
            }

            after = new ArtistListPosition(decoded.Value.SortValue, afterId);
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListAsync(after, limit + 1, request.Status, search, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(last.Name, last.Id.ToString("N")).Encode(scope);
        }

        return OperationResult.Success(new CursorPage<ArtistSummaryServiceModel>(rows, next));
    }

    public async Task<OperationResult<ArtistServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var artist = await dataLayer.FindAsync(id, cancellationToken);
        return artist is null ? NotFound() : OperationResult.Success(ToServiceModel(artist));
    }

    public async Task<IReadOnlyList<ArtistSummaryServiceModel>> GetSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Count == 0 ? [] : await dataLayer.GetSummariesAsync([.. ids.Distinct()], cancellationToken);
    }

    public async Task<IReadOnlyList<ArtistSummaryServiceModel>> FindBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slugs);
        return slugs.Count == 0
            ? []
            : await dataLayer.FindBySlugsAsync([.. slugs.Distinct(StringComparer.Ordinal)], cancellationToken);
    }

    public async Task<OperationResult<ArtistServiceModel>> CreateAsync(
        ArtistCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);

        var name = create.Name.Trim();
        var slug = create.Slug ?? Slugs.FromName(name);

        if (await dataLayer.SlugExistsAsync(slug, exceptId: null, cancellationToken))
        {
            return OperationResult.Failure<ArtistServiceModel>(SlugTaken(slug));
        }

        var artist = new Artist
        {
            Id = Guid.CreateVersion7(clock.UtcNow),
            Name = name,
            Slug = slug,
            Medium = Clean(create.Medium),
            Bio = CleanBio(create.Bio),
            Active = create.Active,
            PortraitMediaId = create.PortraitMediaId,
            Status = PublishStatus.Draft,
        };

        return await dataLayer.CreateAsync(artist, actorUserId, cancellationToken) switch
        {
            ArtistWriteOutcome.Saved => OperationResult.Success(ToServiceModel(artist)),
            ArtistWriteOutcome.SlugTaken => OperationResult.Failure<ArtistServiceModel>(SlugTaken(slug)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ArtistServiceModel>> UpdateAsync(
        Guid id,
        ArtistUpdateViewModel update,
        string? ifMatch,
        ArtistPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var artist = await dataLayer.FindAsync(id, cancellationToken);
        if (artist is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(artist.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ArtistServiceModel>(versionError);
        }

        var changes = new List<string>();

        if (update.Name is not null && update.Name.Trim() is var name && !string.Equals(name, artist.Name, StringComparison.Ordinal))
        {
            artist.Name = name;
            changes.Add("name");
        }

        PublishedPathChange? pathChange = null;
        if (update.Slug is not null && !string.Equals(update.Slug, artist.Slug, StringComparison.Ordinal))
        {
            if (await dataLayer.SlugExistsAsync(update.Slug, artist.Id, cancellationToken))
            {
                return OperationResult.Failure<ArtistServiceModel>(SlugTaken(update.Slug));
            }

            // A slug that has ever been public is someone's bookmark: the old address is redirected to the new one,
            // staged with this save (content.md, ADR 0035).
            pathChange = PublishedPathChange.For(ArtistPolicy.SitePathPrefix, artist.Slug, update.Slug, artist.PublishedAt);
            artist.Slug = update.Slug;
            changes.Add(pathChange is null ? "slug" : $"slug (redirecting {pathChange.FromPath})");
        }

        if (update.Medium is not null && Clean(update.Medium) is var medium && !string.Equals(medium, artist.Medium, StringComparison.Ordinal))
        {
            artist.Medium = medium;
            changes.Add(medium is null ? "medium cleared" : "medium");
        }

        if (update.Bio is not null && CleanBio(update.Bio) is var bio && !string.Equals(bio, artist.Bio, StringComparison.Ordinal))
        {
            artist.Bio = bio;
            changes.Add(bio is null ? "bio cleared" : "bio");
        }

        if (update.Active is { } active && active != artist.Active)
        {
            artist.Active = active;
            changes.Add(active ? "active" : "inactive");
        }

        if (update.PortraitMediaId is { } portraitId && portraitId != artist.PortraitMediaId)
        {
            // media.md: an image on published content has alt text. A draft may wait for it until publish; content
            // that is (or is about to be) public may not take an image without it.
            if (artist.Status is PublishStatus.Published or PublishStatus.Scheduled
                && PortraitError(portraitId, portrait, "This artist is published, so a new portrait needs alt text first.") is { } portraitError)
            {
                return OperationResult.Failure<ArtistServiceModel>(portraitError);
            }

            artist.PortraitMediaId = portraitId;
            changes.Add("portrait");
        }
        else if (update.RemovePortrait == true && artist.PortraitMediaId is not null)
        {
            artist.PortraitMediaId = null;
            changes.Add("portrait removed");
        }

        if (changes.Count == 0)
        {
            return OperationResult.Success(ToServiceModel(artist));
        }

        return await dataLayer.UpdateAsync(artist, actorUserId, $"Updated: {string.Join(", ", changes)}.", pathChange, cancellationToken) switch
        {
            ArtistWriteOutcome.Saved => OperationResult.Success(ToServiceModel(artist)),
            ArtistWriteOutcome.SlugTaken => OperationResult.Failure<ArtistServiceModel>(SlugTaken(artist.Slug)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ArtistServiceModel>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ArtistPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var artist = await dataLayer.FindAsync(id, cancellationToken);
        if (artist is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(artist.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ArtistServiceModel>(versionError);
        }

        var from = artist.Status;
        if (PublishLifecycle.Next(from, command) is not { } to)
        {
            return OperationResult.Failure<ArtistServiceModel>(OperationError.Of(
                ContentErrorCodes.StatusTransitionInvalid,
                $"This artist is {from.ToString().ToLowerInvariant()}, so “{command.Verb()}” doesn't apply. Reload it to see its current status."));
        }

        if (command == PublishCommand.Publish)
        {
            if (PublishRequirementError(artist, portrait) is { } requirementError)
            {
                return OperationResult.Failure<ArtistServiceModel>(requirementError);
            }

            artist.PublishedAt ??= clock.UtcNow;
            artist.ScheduledFor = null;
        }

        artist.Status = to;

        return await dataLayer.TransitionAsync(artist, command, from, actorUserId, cancellationToken) switch
        {
            ArtistWriteOutcome.Saved => OperationResult.Success(ToServiceModel(artist)),
            _ => Conflict(),
        };
    }

    /// <summary>
    /// content.md: what the site needs is checked at publish, not at draft. The name and slug are guaranteed by
    /// every write; the portrait is the one thing a draft may leave unfinished.
    /// </summary>
    private static OperationError? PublishRequirementError(Artist artist, ArtistPortraitCheck? portrait)
    {
        if (artist.PortraitMediaId is null)
        {
            return null;
        }

        return PortraitError(
            artist.PortraitMediaId.Value, portrait, "The portrait needs alt text before this artist can be published.");
    }

    /// <summary>
    /// Whether Media's answer clears <paramref name="portraitId"/> for published content. The facade asked about a
    /// particular image; an answer about any other one counts as no answer — a conflict, since the row version moved
    /// too and the If-Match check would normally have refused first.
    /// </summary>
    private static OperationError? PortraitError(Guid portraitId, ArtistPortraitCheck? portrait, string altTextMessage)
    {
        if (portrait is null || portrait.PortraitMediaId != portraitId)
        {
            return EntityTag.Conflict(What);
        }

        if (!portrait.Exists)
        {
            return OperationError.Validation(
                "The portrait is no longer in the media library.",
                [("portraitMediaId", "Choose another portrait, or remove it, before publishing.")]);
        }

        return portrait.HasAltText
            ? null
            : OperationError.Validation(altTextMessage, [("portraitMediaId", "Add alt text to the portrait in Media first.")]);
    }

    public async Task<int> SeedAsync(
        IReadOnlyList<(string Name, string? Medium)> roster, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roster);

        var now = clock.UtcNow;
        var taken = await dataLayer.GetSlugsAsync(cancellationToken);
        var artists = roster
            .Select(entry => (entry.Name, entry.Medium, Slug: Slugs.FromName(entry.Name)))
            .Where(entry => taken.Add(entry.Slug))
            .Select(entry => new Artist
            {
                Id = Guid.CreateVersion7(now),
                Name = entry.Name,
                Slug = entry.Slug,
                Medium = entry.Medium,
                Active = true,
                Status = PublishStatus.Published,
                PublishedAt = now,
            })
            .ToList();

        return artists.Count == 0 ? 0 : await dataLayer.AddSeedAsync(artists, actor, cancellationToken);
    }

    private static ArtistServiceModel ToServiceModel(Artist artist) => new(
        artist.Id,
        artist.Name,
        artist.Slug,
        artist.Medium,
        artist.Bio,
        artist.Active,
        artist.PortraitMediaId,
        artist.Status,
        artist.PublishedAt,
        artist.ScheduledFor,
        SlugLocked: artist.PublishedAt is not null,
        artist.LegacyWordPressId,
        artist.CreatedAt,
        artist.UpdatedAt,
        artist.UpdatedBy,
        EntityTag.Format(artist.RowVersion));

    /// <summary>Trimmed, and <c>null</c> when nothing is left: an empty string clears the field.</summary>
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>As <see cref="Clean"/>, with line endings made <c>\n</c> so paragraphs compare and render alike.</summary>
    private static string? CleanBio(string? text) =>
        Clean(text?.ReplaceLineEndings("\n"));

    private static OperationResult<ArtistServiceModel> NotFound() =>
        OperationResult.Failure<ArtistServiceModel>(OperationError.Of(ErrorCodes.NotFound, "There is no such artist."));

    private static OperationResult<ArtistServiceModel> Conflict() =>
        OperationResult.Failure<ArtistServiceModel>(EntityTag.Conflict(What));

    private static OperationError SlugTaken(string slug) => new(
        ContentErrorCodes.SlugTaken,
        "Another artist already has this web address.",
        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = [$"“{slug}” is taken. Choose another."] });

}
