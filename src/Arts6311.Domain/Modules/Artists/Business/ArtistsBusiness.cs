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
        var order = ArtistListOrder.Columns;

        // The facade's validator has already refused a column the roster does not sort by.
        var sort = order.Resolve(request.Sort)
            ?? throw new ArgumentException("The roster does not sort by that column.", nameof(request));

        ListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = order.Decode(sort, request.Cursor, scope);
            if (!decoded.Succeeded)
            {
                return OperationResult.Failure<CursorPage<ArtistSummaryServiceModel>>(decoded.Error!);
            }

            after = decoded.Value;
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListAsync(sort, after, limit + 1, request.Status, search, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            next = order.Encode(sort, rows[^1], scope);
        }

        return OperationResult.Success(new CursorPage<ArtistSummaryServiceModel>(rows, next));
    }

    /// <remarks>
    /// Its own cursor scope, so a cursor from the status list is not valid here and the other way about. Status is
    /// not part of it: the Deleted view shows every deleted record, whatever it was when it went (ADR 0052).
    /// </remarks>
    public async Task<OperationResult<CursorPage<ArtistDeletedServiceModel>>> ListDeletedAsync(
        ArtistListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var scope = string.Create(CultureInfo.InvariantCulture, $"artists|deleted|search={search}");

        ArtistListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded || !Guid.TryParseExact(decoded.Value!.TieBreaker, "N", out var afterId))
            {
                return OperationResult.Failure<CursorPage<ArtistDeletedServiceModel>>(
                    OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));
            }

            after = new ArtistListPosition(decoded.Value.SortValue, afterId);
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListDeletedAsync(after, limit + 1, search, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(last.Name, last.Id.ToString("N")).Encode(scope);
        }

        return OperationResult.Success(new CursorPage<ArtistDeletedServiceModel>([.. rows.Select(row => row.ToServiceModel())], next));
    }

    public async Task<OperationResult> DeleteAsync(
        Guid id, ContentReferenceCheck references, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var artist = await dataLayer.FindAsync(id, cancellationToken);
        if (artist is null)
        {
            return OperationResult.Failure(OperationError.Of(ErrorCodes.NotFound, "There is no such artist."));
        }

        if (EntityTag.Verify(artist.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure(versionError);
        }

        // Refused while another record still names it, rather than quietly dropping the credit: a published
        // exhibition that lost an artist, or a class run that lost its instructor, would be the worse outcome
        // (ADR 0033, 0052). The facade asked the modules that could name it.
        if (references.Refusal(What) is { } inUse)
        {
            return OperationResult.Failure(OperationError.Of(ContentErrorCodes.InUse, inUse));
        }

        // From any status, Published included: "this was a mistake" is not a lifecycle step, and the delete stages
        // the revalidation that takes it off the site (ADR 0052).
        return await dataLayer.DeleteAsync(artist, actorUserId, cancellationToken) switch
        {
            ArtistWriteOutcome.Saved => OperationResult.Success(),
            _ => OperationResult.Failure(EntityTag.Conflict(What)),
        };
    }

    public async Task<OperationResult<ArtistServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var artist = await dataLayer.FindDeletedAsync(id, cancellationToken);
        if (artist is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(artist.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ArtistServiceModel>(versionError);
        }

        // The delete freed the address, so another record may be living there now. Refused rather than renamed for
        // the staff member: which of the two keeps it is their decision (ADR 0052).
        if (await dataLayer.SlugExistsAsync(artist.Slug, artist.Id, cancellationToken))
        {
            return OperationResult.Failure<ArtistServiceModel>(SlugTaken(artist.Slug));
        }

        var deletedAs = artist.Status;

        // Back as a Draft, never straight back onto the site: what publishing requires may no longer hold, and a
        // restore is a decision to look at the record again, not to publish it (ADR 0052).
        artist.Status = PublishStatus.Draft;
        artist.ScheduledFor = null;

        return await dataLayer.RestoreAsync(artist, deletedAs, actorUserId, cancellationToken) switch
        {
            ArtistWriteOutcome.Saved => OperationResult.Success(ToServiceModel(artist)),
            ArtistWriteOutcome.SlugTaken => OperationResult.Failure<ArtistServiceModel>(SlugTaken(artist.Slug)),
            _ => Conflict(),
        };
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

    public async Task<IReadOnlyList<ArtistSummaryServiceModel>> FindByNamesAsync(
        IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);

        // Upper-cased here and in the query for SQLite's sake: SQL Server's collation already ignores case. SQLite's
        // UPPER() changes ASCII letters only, so there a name differing in the case of an accented letter is missed —
        // the tests' database, not production's.
        var wanted = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return wanted.Count == 0 ? [] : await dataLayer.FindByNamesAsync(wanted, cancellationToken);
    }

    public async Task<IReadOnlyList<ArtistSiteRow>> ListForSiteAsync(CancellationToken cancellationToken) =>
        await dataLayer.ListPublishedAsync(null, cancellationToken);

    public async Task<ArtistSiteRow?> GetForSiteAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        return (await dataLayer.ListPublishedAsync(slug, cancellationToken)).SingleOrDefault();
    }

    public async Task<OperationResult<PreviewRecord<ArtistSiteRow>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var artist = await dataLayer.FindAsync(id, cancellationToken);
        return artist is null
            ? OperationResult.Failure<PreviewRecord<ArtistSiteRow>>(OperationError.Of(ErrorCodes.NotFound, "There is no such artist."))
            : OperationResult.Success(new PreviewRecord<ArtistSiteRow>(
                new ArtistSiteRow(
                    artist.Id, artist.Slug, artist.Name, artist.Medium, artist.Active, artist.PortraitMediaId, artist.Bio,
                    artist.UpdatedAt),
                artist.Status));
    }

    /// <remarks>
    /// An unpublished artist is still credited by name — the exhibition or the piece is theirs whatever their page's
    /// status — but given no slug, because the site has no page to link to (ADR 0042).
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel>> DescribeCreditsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var artists = new List<ArtistSummaryServiceModel>();
        foreach (var chunk in ids.Distinct().Chunk(ArtistPolicy.LookupMaxCount))
        {
            artists.AddRange(await dataLayer.GetSummariesAsync(chunk, cancellationToken));
        }

        return artists.ToDictionary(
            artist => artist.Id,
            artist => new SiteArtistCreditServiceModel(
                artist.Name, artist.Status == PublishStatus.Published ? artist.Slug : null));
    }

    public async Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) =>
        [.. (await dataLayer.ListPublishedAsync(null, cancellationToken))
            .Select(row => new SitemapEntryServiceModel(
                SitePaths.AddressOf(ArtistPolicy.SitePathPrefix, row.Slug), row.UpdatedAt))];

    public async Task<OperationResult<ArtistServiceModel>> CreateAsync(
        ArtistCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);

        var name = create.Name.Trim();
        // An address the request names is kept or refused; one made from the name takes the next free number instead.
        var slug = create.Slug ?? await Slugs.FirstFreeAsync(
            Slugs.FromName(name),
            (candidate, token) => dataLayer.SlugExistsAsync(candidate, exceptId: null, token),
            cancellationToken);

        if (create.Slug is not null && await dataLayer.SlugExistsAsync(slug, exceptId: null, cancellationToken))
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
            return OperationResult.Failure<ArtistServiceModel>(TransitionInvalid(from, command));
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
        else if (command == PublishCommand.Archive)
        {
            // An archived artist is never published by the scheduled publisher, so a time left on it would only
            // mislead the staff reading it.
            artist.ScheduledFor = null;
        }

        artist.Status = to;

        return await dataLayer.TransitionAsync(artist, command, from, actorUserId, cancellationToken) switch
        {
            ArtistWriteOutcome.Saved => OperationResult.Success(ToServiceModel(artist)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ArtistServiceModel>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
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
        if (PublishLifecycle.Next(from, PublishCommand.Schedule) is not { } to)
        {
            return OperationResult.Failure<ArtistServiceModel>(TransitionInvalid(from, PublishCommand.Schedule));
        }

        // A time already past would be published by the next pass, a minute later, which is not what "schedule"
        // means: staff who want it out now have the publish command for that.
        if (scheduledFor <= clock.UtcNow)
        {
            return OperationResult.Failure<ArtistServiceModel>(OperationError.Validation(
                "Choose a time in the future.",
                [("scheduledFor", "This time has passed. Publish the artist now instead, or pick a later time.")]));
        }

        // Checked now as well as at publish, so staff hear about a missing portrait while they are looking at the
        // screen rather than at midnight, when only the audit trail is watching (ADR 0036).
        if (PublishRequirementError(artist, portrait) is { } requirementError)
        {
            return OperationResult.Failure<ArtistServiceModel>(requirementError);
        }

        artist.ScheduledFor = scheduledFor;
        artist.Status = to;

        return await dataLayer.TransitionAsync(artist, PublishCommand.Schedule, from, actorUserId, cancellationToken) switch
        {
            ArtistWriteOutcome.Saved => OperationResult.Success(ToServiceModel(artist)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ArtistServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
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
        if (PublishLifecycle.Next(from, PublishCommand.Unschedule) is not { } to)
        {
            return OperationResult.Failure<ArtistServiceModel>(TransitionInvalid(from, PublishCommand.Unschedule));
        }

        artist.ScheduledFor = null;
        artist.Status = to;

        return await dataLayer.TransitionAsync(artist, PublishCommand.Unschedule, from, actorUserId, cancellationToken) switch
        {
            ArtistWriteOutcome.Saved => OperationResult.Success(ToServiceModel(artist)),
            _ => Conflict(),
        };
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        dataLayer.ListDueAsync(asOf, take, cancellationToken);

    public async Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, ArtistPortraitCheck? portrait, CancellationToken cancellationToken)
    {
        var artist = await dataLayer.FindAsync(id, cancellationToken);

        // Read again inside this scope, because the due list is a snapshot: staff may have published, rescheduled
        // or archived the artist since, and another Worker may have taken it.
        if (artist is null || artist.Status != PublishStatus.Scheduled || artist.ScheduledFor is null || artist.ScheduledFor > asOf)
        {
            return ScheduledPublishOutcome.Skipped;
        }

        // The portrait the facade asked Media about is no longer this artist's: whoever changed it is mid-edit, so
        // this pass leaves the artist to them and the next one asks again.
        if (artist.PortraitMediaId is not null && portrait?.PortraitMediaId != artist.PortraitMediaId)
        {
            return ScheduledPublishOutcome.Lost;
        }

        var from = artist.Status;
        if (PublishRequirementError(artist, portrait) is { } requirementError)
        {
            // Back to Draft rather than left Scheduled: a record whose time has passed would otherwise be retried
            // every minute for ever, and nobody would be told (ADR 0036).
            artist.ScheduledFor = null;
            artist.Status = PublishStatus.Draft;

            return await dataLayer.AbandonScheduleAsync(artist, from, requirementError.Message, cancellationToken) switch
            {
                ArtistWriteOutcome.Saved => ScheduledPublishOutcome.Unscheduled,
                _ => ScheduledPublishOutcome.Lost,
            };
        }

        artist.PublishedAt ??= clock.UtcNow;
        artist.ScheduledFor = null;
        artist.Status = PublishStatus.Published;

        return await dataLayer.TransitionAsync(
            artist, PublishCommand.Publish, from, ScheduledPublishPolicy.Actor, cancellationToken) switch
        {
            ArtistWriteOutcome.Saved => ScheduledPublishOutcome.Published,

            // The row version moved under this save: another Worker published it, or staff changed it. Either way
            // it is theirs now.
            _ => ScheduledPublishOutcome.Lost,
        };
    }

    private static OperationError TransitionInvalid(PublishStatus from, PublishCommand command) => OperationError.Of(
        ContentErrorCodes.StatusTransitionInvalid,
        $"This artist is {from.ToString().ToLowerInvariant()}, so “{command.Verb()}” doesn't apply. Reload it to see its current status.");

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
