using System.Globalization;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Exhibitions.Data;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Exhibitions.Business;

/// <summary>
/// The exhibition rules: slugs unique and locked once published, an end date never before the start, status changes
/// only along the lifecycle, a summary and a described hero before publishing, and every write checked against the
/// row version the editor read.
/// </summary>
internal sealed class ExhibitionsBusiness(IExhibitionsDataLayer dataLayer, IClock clock) : IExhibitionsBusiness
{
    private const string What = "this exhibition";

    private const string DateFormat = "yyyy-MM-dd";

    public async Task<OperationResult<CursorPage<ExhibitionSummaryServiceModel>>> ListAsync(
        ExhibitionListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var scope = string.Create(CultureInfo.InvariantCulture, $"exhibitions|status={request.Status}|search={search}");

        ExhibitionListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded
                || !DateOnly.TryParseExact(decoded.Value!.SortValue, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var afterDate)
                || !Guid.TryParseExact(decoded.Value.TieBreaker, "N", out var afterId))
            {
                return OperationResult.Failure<CursorPage<ExhibitionSummaryServiceModel>>(
                    OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));
            }

            after = new ExhibitionListPosition(afterDate, afterId);
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListAsync(after, limit + 1, request.Status, search, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(last.StartDate.ToString(DateFormat, CultureInfo.InvariantCulture), last.Id.ToString("N"))
                .Encode(scope);
        }

        return OperationResult.Success(new CursorPage<ExhibitionSummaryServiceModel>(rows, next));
    }

    public async Task<OperationResult<ExhibitionDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var exhibition = await dataLayer.FindAsync(id, cancellationToken);
        return exhibition is null ? NotFound() : OperationResult.Success(ToDetail(exhibition));
    }

    public async Task<OperationResult<ExhibitionDetail>> CreateAsync(
        ExhibitionCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);

        var title = create.Title.Trim();
        var slug = create.Slug ?? Slugs.FromName(title);

        if (await dataLayer.SlugExistsAsync(slug, exceptId: null, cancellationToken))
        {
            return OperationResult.Failure<ExhibitionDetail>(SlugTaken(slug));
        }

        var exhibition = new Exhibition
        {
            Id = Guid.CreateVersion7(clock.UtcNow),
            Title = title,
            Slug = slug,
            Summary = CleanText(create.Summary),
            Body = CleanText(create.Body),
            StartDate = create.StartDate!.Value,
            EndDate = create.EndDate!.Value,
            HeroMediaId = create.HeroMediaId,
            Status = PublishStatus.Draft,
        };

        if (create.Reception is { } reception)
        {
            SetReception(exhibition, reception);
        }

        ReplaceCredits(exhibition, create.ArtistIds ?? []);

        return await dataLayer.CreateAsync(exhibition, actorUserId, cancellationToken) switch
        {
            ExhibitionWriteOutcome.Saved => OperationResult.Success(ToDetail(exhibition)),
            ExhibitionWriteOutcome.SlugTaken => OperationResult.Failure<ExhibitionDetail>(SlugTaken(slug)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ExhibitionDetail>> UpdateAsync(
        Guid id,
        ExhibitionUpdateViewModel update,
        string? ifMatch,
        ExhibitionHeroCheck? hero,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var exhibition = await dataLayer.FindAsync(id, cancellationToken);
        if (exhibition is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(exhibition.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ExhibitionDetail>(versionError);
        }

        var isPublic = exhibition.Status is PublishStatus.Published or PublishStatus.Scheduled;
        var changes = new List<string>();

        if (update.Title is not null && update.Title.Trim() is var title && !string.Equals(title, exhibition.Title, StringComparison.Ordinal))
        {
            exhibition.Title = title;
            changes.Add("title");
        }

        PublishedPathChange? pathChange = null;
        if (update.Slug is not null && !string.Equals(update.Slug, exhibition.Slug, StringComparison.Ordinal))
        {
            if (await dataLayer.SlugExistsAsync(update.Slug, exhibition.Id, cancellationToken))
            {
                return OperationResult.Failure<ExhibitionDetail>(SlugTaken(update.Slug));
            }

            // A slug that has ever been public is someone's bookmark: the old address is redirected to the new one,
            // staged with this save (content.md, ADR 0035).
            pathChange = PublishedPathChange.For(ExhibitionPolicy.SitePathPrefix, exhibition.Slug, update.Slug, exhibition.PublishedAt);
            exhibition.Slug = update.Slug;
            changes.Add(pathChange is null ? "slug" : $"slug (redirecting {pathChange.FromPath})");
        }

        if (update.Summary is not null && CleanText(update.Summary) is var summary && !string.Equals(summary, exhibition.Summary, StringComparison.Ordinal))
        {
            // content.md: what the site needs is checked at publish, and published content keeps it.
            if (summary is null && isPublic)
            {
                return OperationResult.Failure<ExhibitionDetail>(OperationError.Validation(
                    "This exhibition is published, so it keeps a summary.",
                    [("summary", "The site's card and search results show the summary. Edit it rather than clearing it.")]));
            }

            exhibition.Summary = summary;
            changes.Add(summary is null ? "summary cleared" : "summary");
        }

        if (update.Body is not null && CleanText(update.Body) is var body && !string.Equals(body, exhibition.Body, StringComparison.Ordinal))
        {
            exhibition.Body = body;
            changes.Add(body is null ? "body cleared" : "body");
        }

        var startDate = update.StartDate ?? exhibition.StartDate;
        var endDate = update.EndDate ?? exhibition.EndDate;
        if (startDate != exhibition.StartDate || endDate != exhibition.EndDate)
        {
            // The validator compares the two dates when both are sent; one alone is compared with the one stored.
            if (endDate < startDate)
            {
                return OperationResult.Failure<ExhibitionDetail>(OperationError.Validation(
                    "The show can't end before it starts.",
                    [(update.EndDate is null ? "startDate" : "endDate",
                        $"The show runs {exhibition.StartDate:yyyy-MM-dd} to {exhibition.EndDate:yyyy-MM-dd}; change both dates together.")]));
            }

            exhibition.StartDate = startDate;
            exhibition.EndDate = endDate;
            changes.Add("dates");
        }

        if (update.Reception is { } reception)
        {
            var before = (exhibition.ReceptionStartsAt, exhibition.ReceptionEndsAt, exhibition.ReceptionNote);
            SetReception(exhibition, reception);
            if (before != (exhibition.ReceptionStartsAt, exhibition.ReceptionEndsAt, exhibition.ReceptionNote))
            {
                changes.Add("reception");
            }
        }
        else if (update.RemoveReception == true && exhibition.ReceptionStartsAt is not null)
        {
            exhibition.ReceptionStartsAt = null;
            exhibition.ReceptionEndsAt = null;
            exhibition.ReceptionNote = null;
            changes.Add("reception removed");
        }

        if (update.HeroMediaId is { } heroId && heroId != exhibition.HeroMediaId)
        {
            // media.md: an image on published content has alt text. A draft may wait for it until publish; content
            // that is (or is about to be) public may not take an image without it.
            if (isPublic && HeroError(heroId, hero, "This exhibition is published, so a new hero image needs alt text first.") is { } heroError)
            {
                return OperationResult.Failure<ExhibitionDetail>(heroError);
            }

            exhibition.HeroMediaId = heroId;
            changes.Add("hero");
        }
        else if (update.RemoveHero == true && exhibition.HeroMediaId is not null)
        {
            exhibition.HeroMediaId = null;
            changes.Add("hero removed");
        }

        if (update.ArtistIds is { } artistIds && !CreditedIds(exhibition).SequenceEqual(artistIds))
        {
            ReplaceCredits(exhibition, artistIds);
            changes.Add(artistIds.Count == 0 ? "artists cleared" : "artists");
        }

        if (changes.Count == 0)
        {
            return OperationResult.Success(ToDetail(exhibition));
        }

        return await dataLayer.UpdateAsync(exhibition, actorUserId, $"Updated: {string.Join(", ", changes)}.", pathChange, cancellationToken) switch
        {
            ExhibitionWriteOutcome.Saved => OperationResult.Success(ToDetail(exhibition)),
            ExhibitionWriteOutcome.SlugTaken => OperationResult.Failure<ExhibitionDetail>(SlugTaken(exhibition.Slug)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ExhibitionDetail>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ExhibitionHeroCheck? hero,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var exhibition = await dataLayer.FindAsync(id, cancellationToken);
        if (exhibition is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(exhibition.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ExhibitionDetail>(versionError);
        }

        var from = exhibition.Status;
        if (PublishLifecycle.Next(from, command) is not { } to)
        {
            return OperationResult.Failure<ExhibitionDetail>(OperationError.Of(
                ContentErrorCodes.StatusTransitionInvalid,
                $"This exhibition is {from.ToString().ToLowerInvariant()}, so “{command.Verb()}” doesn't apply. Reload it to see its current status."));
        }

        if (command == PublishCommand.Publish)
        {
            if (PublishRequirementError(exhibition, hero) is { } requirementError)
            {
                return OperationResult.Failure<ExhibitionDetail>(requirementError);
            }

            exhibition.PublishedAt ??= clock.UtcNow;
            exhibition.ScheduledFor = null;
        }

        exhibition.Status = to;

        return await dataLayer.TransitionAsync(exhibition, command, from, actorUserId, cancellationToken) switch
        {
            ExhibitionWriteOutcome.Saved => OperationResult.Success(ToDetail(exhibition)),
            _ => Conflict(),
        };
    }

    public async Task<int> SeedAsync(IReadOnlyList<ExhibitionSeed> seeds, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seeds);

        var now = clock.UtcNow;
        var taken = await dataLayer.GetSlugsAsync(cancellationToken);
        var exhibitions = new List<Exhibition>();

        foreach (var seed in seeds)
        {
            var slug = Slugs.FromName(seed.Title);
            if (!taken.Add(slug))
            {
                continue;
            }

            var exhibition = new Exhibition
            {
                Id = Guid.CreateVersion7(now),
                Title = seed.Title,
                Slug = slug,
                Summary = seed.Summary,
                StartDate = seed.StartDate,
                EndDate = seed.EndDate,
                ReceptionStartsAt = seed.Reception?.StartsAt.ToUniversalTime(),
                ReceptionEndsAt = seed.Reception?.EndsAt.ToUniversalTime(),
                ReceptionNote = seed.Reception?.Note,
                Status = PublishStatus.Published,
                PublishedAt = now,
            };
            ReplaceCredits(exhibition, seed.ArtistIds);
            exhibitions.Add(exhibition);
        }

        return exhibitions.Count == 0 ? 0 : await dataLayer.AddSeedAsync(exhibitions, actor, cancellationToken);
    }

    /// <summary>
    /// content.md: what the site needs is checked at publish, not at draft. The title, slug and dates are guaranteed by
    /// every write; the summary and the hero's alt text are what a draft may leave unfinished. Both are reported at
    /// once, so the editor fixes everything in one pass.
    /// </summary>
    private static OperationError? PublishRequirementError(Exhibition exhibition, ExhibitionHeroCheck? hero)
    {
        var problems = new List<(string Field, string Error)>();

        if (string.IsNullOrWhiteSpace(exhibition.Summary))
        {
            problems.Add(("summary", "Write a summary first: the site's card and search results show it."));
        }

        if (exhibition.HeroMediaId is { } heroId)
        {
            if (hero is null || hero.HeroMediaId != heroId)
            {
                return EntityTag.Conflict(What);
            }

            if (!hero.Exists)
            {
                problems.Add(("heroMediaId", "The hero image is no longer in the media library. Choose another, or remove it."));
            }
            else if (!hero.HasAltText)
            {
                problems.Add(("heroMediaId", "Add alt text to the hero image in Media first."));
            }
        }

        return problems.Count == 0
            ? null
            : OperationError.Validation("This exhibition isn't ready to publish yet.", problems);
    }

    /// <summary>
    /// Whether Media's answer clears <paramref name="heroId"/> for published content. The facade asked about a
    /// particular image; an answer about any other one counts as no answer — a conflict.
    /// </summary>
    private static OperationError? HeroError(Guid heroId, ExhibitionHeroCheck? hero, string altTextMessage)
    {
        if (hero is null || hero.HeroMediaId != heroId)
        {
            return EntityTag.Conflict(What);
        }

        if (!hero.Exists)
        {
            return OperationError.Validation(
                "The hero image is no longer in the media library.",
                [("heroMediaId", "Choose another image from the media library.")]);
        }

        return hero.HasAltText
            ? null
            : OperationError.Validation(altTextMessage, [("heroMediaId", "Add alt text to the image in Media first.")]);
    }

    /// <summary>The reception as sent, kept as UTC; an empty note is no note.</summary>
    private static void SetReception(Exhibition exhibition, ExhibitionReceptionViewModel reception)
    {
        exhibition.ReceptionStartsAt = reception.StartsAt!.Value.ToUniversalTime();
        exhibition.ReceptionEndsAt = reception.EndsAt!.Value.ToUniversalTime();
        exhibition.ReceptionNote = Clean(reception.Note);
    }

    private static IEnumerable<Guid> CreditedIds(Exhibition exhibition) =>
        exhibition.Artists.OrderBy(credit => credit.Position).Select(credit => credit.ArtistId);

    /// <summary>
    /// Makes the credits exactly <paramref name="artistIds"/>, in that order. A credit that stays is updated in place
    /// rather than removed and added again, which the (exhibition, artist) key would refuse in one save.
    /// </summary>
    private static void ReplaceCredits(Exhibition exhibition, IReadOnlyList<Guid> artistIds)
    {
        var existing = exhibition.Artists.ToDictionary(credit => credit.ArtistId);
        exhibition.Artists.RemoveAll(credit => !artistIds.Contains(credit.ArtistId));

        for (var position = 0; position < artistIds.Count; position++)
        {
            if (existing.TryGetValue(artistIds[position], out var credit))
            {
                credit.Position = position;
            }
            else
            {
                exhibition.Artists.Add(new ExhibitionArtist
                {
                    ExhibitionId = exhibition.Id,
                    ArtistId = artistIds[position],
                    Position = position,
                });
            }
        }
    }

    private static ExhibitionDetail ToDetail(Exhibition exhibition) => new(
        exhibition.Id,
        exhibition.Title,
        exhibition.Slug,
        exhibition.Summary,
        exhibition.Body,
        exhibition.StartDate,
        exhibition.EndDate,
        exhibition is { ReceptionStartsAt: { } startsAt, ReceptionEndsAt: { } endsAt }
            ? new ExhibitionReceptionServiceModel(startsAt, endsAt, exhibition.ReceptionNote)
            : null,
        exhibition.HeroMediaId,
        [.. CreditedIds(exhibition)],
        exhibition.Status,
        exhibition.PublishedAt,
        exhibition.ScheduledFor,
        exhibition.LegacyWordPressId,
        exhibition.CreatedAt,
        exhibition.UpdatedAt,
        exhibition.UpdatedBy,
        EntityTag.Format(exhibition.RowVersion));

    /// <summary>Trimmed, and <c>null</c> when nothing is left: an empty string clears the field.</summary>
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>As <see cref="Clean"/>, with line endings made <c>\n</c> so paragraphs compare and render alike.</summary>
    private static string? CleanText(string? text) => Clean(text?.ReplaceLineEndings("\n"));

    private static OperationResult<ExhibitionDetail> NotFound() =>
        OperationResult.Failure<ExhibitionDetail>(OperationError.Of(ErrorCodes.NotFound, "There is no such exhibition."));

    private static OperationResult<ExhibitionDetail> Conflict() =>
        OperationResult.Failure<ExhibitionDetail>(EntityTag.Conflict(What));

    private static OperationError SlugTaken(string slug) => new(
        ContentErrorCodes.SlugTaken,
        "Another exhibition already has this web address.",
        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = [$"“{slug}” is taken. Choose another."] });

}
