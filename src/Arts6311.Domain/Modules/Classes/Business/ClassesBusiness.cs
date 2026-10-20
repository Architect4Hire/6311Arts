using System.Globalization;
using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Html;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Classes.Data;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Classes.Business;

/// <summary>
/// The class rules: the body is sanitized on write and only the sanitizer's output is kept (ADR 0034); every picture
/// in it is a library original with its own alt text; slugs are unique and a published one's change owes a redirect;
/// each session holds a SKU no other record in the CMS has (ADR 0037); and Square's record of price, seats and
/// sold-out is never written from here (square.md, ADR 0032).
/// </summary>
internal sealed class ClassesBusiness(IClassesDataLayer dataLayer, IHtmlSanitizer sanitizer, IClock clock) : IClassesBusiness
{
    private const string What = "this class";

    /// <summary>What one session's resolution came to: the row to keep (if any) and the SKU it ends up with.</summary>
    private sealed record SessionPlan(ClassSessionViewModel Sent, ClassSession? Kept, string Sku);

    public async Task<OperationResult<CursorPage<ClassSummaryServiceModel>>> ListAsync(
        ClassListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var scope = string.Create(CultureInfo.InvariantCulture, $"classes|status={request.Status}|search={search}");

        ClassListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded || !Guid.TryParseExact(decoded.Value!.TieBreaker, "N", out var afterId))
            {
                return OperationResult.Failure<CursorPage<ClassSummaryServiceModel>>(
                    OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));
            }

            after = new ClassListPosition(decoded.Value.SortValue, afterId);
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListAsync(after, limit + 1, request.Status, search, clock.UtcNow, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(last.Title, last.Id.ToString("N")).Encode(scope);
        }

        return OperationResult.Success(new CursorPage<ClassSummaryServiceModel>(rows, next));
    }

    public async Task<OperationResult<ClassServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var offering = await dataLayer.FindAsync(id, cancellationToken);
        return offering is null ? NotFound() : OperationResult.Success(ToServiceModel(offering));
    }

    public async Task<OperationResult<ClassServiceModel>> CreateAsync(
        ClassCreateViewModel create, ClassMediaCheck media, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(media);

        var title = create.Title.Trim();
        var slug = create.Slug ?? Slugs.FromName(title);

        var (body, bodyError) = PrepareBody(create.Body, media);
        if (bodyError is not null)
        {
            return OperationResult.Failure<ClassServiceModel>(bodyError);
        }

        if (await dataLayer.SlugExistsAsync(slug, exceptId: null, cancellationToken))
        {
            return OperationResult.Failure<ClassServiceModel>(SlugTaken(slug));
        }

        var now = clock.UtcNow;
        var offering = new ClassOffering
        {
            Id = Guid.CreateVersion7(now),
            Title = title,
            Slug = slug,
            Summary = Clean(create.Summary),
            ClassType = Clean(create.ClassType),
            Level = Clean(create.Level),
            Instructor = Clean(create.Instructor),
            Cadence = Clean(create.Cadence),
            StartingPriceCents = create.StartingPriceCents,
            HeroMediaId = create.HeroMediaId,
            OfferingNumber = await dataLayer.NextOfferingNumberAsync(cancellationToken),
            Status = PublishStatus.Draft,
        };
        SetBody(offering, body);

        var sessions = await ApplySessionsAsync(offering, create.Sessions ?? [], now, cancellationToken);
        if (!sessions.Succeeded)
        {
            return OperationResult.Failure<ClassServiceModel>(sessions.Error!);
        }

        var summary = AuditSummary("Created as Draft", [], body);
        return await dataLayer.CreateAsync(offering, sessions.Value!, actorUserId, summary, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => OperationResult.Success(ToServiceModel(offering)),
            ClassWriteOutcome.SlugTaken => OperationResult.Failure<ClassServiceModel>(SlugTaken(slug)),
            ClassWriteOutcome.SkuTaken => OperationResult.Failure<ClassServiceModel>(await SkuRaceAsync(offering, cancellationToken)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ClassServiceModel>> UpdateAsync(
        Guid id,
        ClassUpdateViewModel update,
        string? ifMatch,
        ClassMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var offering = await dataLayer.FindAsync(id, cancellationToken);
        if (offering is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(offering.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ClassServiceModel>(versionError);
        }

        var isPublic = offering.Status is PublishStatus.Published or PublishStatus.Scheduled;
        var changes = new List<string>();

        if (update.Title is not null && update.Title.Trim() is var title && !string.Equals(title, offering.Title, StringComparison.Ordinal))
        {
            offering.Title = title;
            changes.Add("title");
        }

        PublishedPathChange? pathChange = null;
        if (update.Slug is not null && !string.Equals(update.Slug, offering.Slug, StringComparison.Ordinal))
        {
            if (await dataLayer.SlugExistsAsync(update.Slug, offering.Id, cancellationToken))
            {
                return OperationResult.Failure<ClassServiceModel>(SlugTaken(update.Slug));
            }

            // A slug that has ever been public is someone's bookmark: the old address is redirected to the new one,
            // staged with this save (content.md, ADR 0035).
            pathChange = PublishedPathChange.For(ClassPolicy.SitePathPrefix, offering.Slug, update.Slug, offering.PublishedAt);
            offering.Slug = update.Slug;
            changes.Add(pathChange is null ? "slug" : $"slug (redirecting {pathChange.FromPath})");
        }

        if (update.Summary is not null && Clean(update.Summary) is var summaryText && !string.Equals(summaryText, offering.Summary, StringComparison.Ordinal))
        {
            // content.md: what the site needs is checked at publish, and published content keeps it.
            if (summaryText is null && isPublic)
            {
                return OperationResult.Failure<ClassServiceModel>(OperationError.Validation(
                    "This class is published, so it keeps a summary.",
                    [("summary", "The site's card and search results show the summary. Edit it rather than clearing it.")]));
            }

            offering.Summary = summaryText;
            changes.Add(summaryText is null ? "summary cleared" : "summary");
        }

        SanitizedHtml? body = null;
        if (update.Body is not null)
        {
            (body, var bodyError) = PrepareBody(update.Body, media ?? ClassMediaCheck.None);
            if (bodyError is not null)
            {
                return OperationResult.Failure<ClassServiceModel>(bodyError);
            }

            var stored = body!.IsEmpty ? null : body.Html;
            if (!string.Equals(stored, offering.Body, StringComparison.Ordinal))
            {
                SetBody(offering, body);
                changes.Add(stored is null ? "description cleared" : "description");
            }
        }

        foreach (var (field, sent, current, apply) in TextFields(update, offering))
        {
            if (sent is null)
            {
                continue;
            }

            var cleaned = Clean(sent);
            if (!string.Equals(cleaned, current, StringComparison.Ordinal))
            {
                apply(cleaned);
                changes.Add(cleaned is null ? $"{field} cleared" : field);
            }
        }

        if (update.StartingPriceCents is { } price || update.RemoveStartingPrice == true)
        {
            var wanted = update.RemoveStartingPrice == true ? (long?)null : update.StartingPriceCents;
            if (wanted != offering.StartingPriceCents)
            {
                // ADR 0032: the starting price is a draft field only until Square holds the record. From then on a
                // change is the change-price command (5.4a), never a field write.
                if (!StartingPriceEditable(offering))
                {
                    return OperationResult.Failure<ClassServiceModel>(OperationError.Validation(
                        "Square holds this class's price now.",
                        [("startingPriceCents", "Use the change-price action instead: the starting price only applies before the class reaches Square.")]));
                }

                offering.StartingPriceCents = wanted;
                changes.Add(wanted is null ? "starting price cleared" : "starting price");
            }
        }

        if (update.HeroMediaId is { } heroId && heroId != offering.HeroMediaId)
        {
            // media.md: an image on published content has alt text. A draft may wait for it until publish; content
            // that is (or is about to be) public may not take an image without it.
            if (isPublic && HeroError(heroId, media, "This class is published, so a new hero image needs alt text first.") is { } heroError)
            {
                return OperationResult.Failure<ClassServiceModel>(heroError);
            }

            offering.HeroMediaId = heroId;
            changes.Add("hero");
        }
        else if (update.RemoveHero == true && offering.HeroMediaId is not null)
        {
            offering.HeroMediaId = null;
            changes.Add("hero removed");
        }

        var skuChanges = SkuChanges.None;
        if (update.Sessions is { } sentSessions)
        {
            var before = Fingerprint(offering);
            var plan = await ApplySessionsAsync(offering, sentSessions, clock.UtcNow, cancellationToken);
            if (!plan.Succeeded)
            {
                return OperationResult.Failure<ClassServiceModel>(plan.Error!);
            }

            skuChanges = plan.Value!;
            if (!string.Equals(before, Fingerprint(offering), StringComparison.Ordinal))
            {
                changes.Add(offering.Sessions.Count == 0 ? "sessions cleared" : "sessions");
            }
        }

        if (changes.Count == 0)
        {
            return OperationResult.Success(ToServiceModel(offering));
        }

        var auditLine = AuditSummary("Updated", changes, body);
        return await dataLayer.UpdateAsync(offering, skuChanges, actorUserId, auditLine, pathChange, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => OperationResult.Success(ToServiceModel(offering)),
            ClassWriteOutcome.SlugTaken => OperationResult.Failure<ClassServiceModel>(SlugTaken(offering.Slug)),
            ClassWriteOutcome.SkuTaken => OperationResult.Failure<ClassServiceModel>(await SkuRaceAsync(offering, cancellationToken)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ClassServiceModel>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ClassMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var offering = await dataLayer.FindAsync(id, cancellationToken);
        if (offering is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(offering.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ClassServiceModel>(versionError);
        }

        var from = offering.Status;
        if (PublishLifecycle.Next(from, command) is not { } to)
        {
            return OperationResult.Failure<ClassServiceModel>(TransitionInvalid(from, command));
        }

        if (command == PublishCommand.Publish)
        {
            if (PublishRequirementError(offering, media) is { } requirementError)
            {
                return OperationResult.Failure<ClassServiceModel>(requirementError);
            }

            offering.PublishedAt ??= clock.UtcNow;
            offering.ScheduledFor = null;
        }
        else if (command == PublishCommand.Archive)
        {
            // An archived offering is never published by the scheduled publisher, so a time left on it would only
            // mislead the staff reading it.
            offering.ScheduledFor = null;
        }

        offering.Status = to;

        return await dataLayer.TransitionAsync(offering, command, from, actorUserId, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => OperationResult.Success(ToServiceModel(offering)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ClassServiceModel>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        ClassMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var offering = await dataLayer.FindAsync(id, cancellationToken);
        if (offering is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(offering.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ClassServiceModel>(versionError);
        }

        var from = offering.Status;
        if (PublishLifecycle.Next(from, PublishCommand.Schedule) is not { } to)
        {
            return OperationResult.Failure<ClassServiceModel>(TransitionInvalid(from, PublishCommand.Schedule));
        }

        // A time already past would be published by the next pass, a minute later, which is not what "schedule"
        // means: staff who want it out now have the publish command for that.
        if (scheduledFor <= clock.UtcNow)
        {
            return OperationResult.Failure<ClassServiceModel>(OperationError.Validation(
                "Choose a time in the future.",
                [("scheduledFor", "This time has passed. Publish the class now instead, or pick a later time.")]));
        }

        // Checked now as well as at publish, so staff hear about a missing summary, price or session while they are
        // looking at the screen rather than at midnight, when only the audit trail is watching (ADR 0036).
        if (PublishRequirementError(offering, media) is { } requirementError)
        {
            return OperationResult.Failure<ClassServiceModel>(requirementError);
        }

        offering.ScheduledFor = scheduledFor;
        offering.Status = to;

        return await dataLayer.TransitionAsync(offering, PublishCommand.Schedule, from, actorUserId, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => OperationResult.Success(ToServiceModel(offering)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ClassServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var offering = await dataLayer.FindAsync(id, cancellationToken);
        if (offering is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(offering.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ClassServiceModel>(versionError);
        }

        var from = offering.Status;
        if (PublishLifecycle.Next(from, PublishCommand.Unschedule) is not { } to)
        {
            return OperationResult.Failure<ClassServiceModel>(TransitionInvalid(from, PublishCommand.Unschedule));
        }

        offering.ScheduledFor = null;
        offering.Status = to;

        return await dataLayer.TransitionAsync(offering, PublishCommand.Unschedule, from, actorUserId, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => OperationResult.Success(ToServiceModel(offering)),
            _ => Conflict(),
        };
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        dataLayer.ListDueAsync(asOf, take, cancellationToken);

    public async Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, ClassMediaCheck? media, CancellationToken cancellationToken)
    {
        var offering = await dataLayer.FindAsync(id, cancellationToken);

        // Read again inside this scope, because the due list is a snapshot: staff may have published, rescheduled or
        // archived the offering since, and another Worker may have taken it.
        if (offering is null
            || offering.Status != PublishStatus.Scheduled
            || offering.ScheduledFor is null
            || offering.ScheduledFor > asOf)
        {
            return ScheduledPublishOutcome.Skipped;
        }

        // The hero the facade asked Media about is no longer this offering's: whoever changed it is mid-edit, so this
        // pass leaves the offering to them and the next one asks again.
        if (offering.HeroMediaId is not null && media?.HeroMediaId != offering.HeroMediaId)
        {
            return ScheduledPublishOutcome.Lost;
        }

        var from = offering.Status;
        if (PublishRequirementError(offering, media) is { } requirementError)
        {
            // Back to Draft rather than left Scheduled: a record whose time has passed would otherwise be retried
            // every minute for ever, and nobody would be told (ADR 0036).
            offering.ScheduledFor = null;
            offering.Status = PublishStatus.Draft;

            return await dataLayer.AbandonScheduleAsync(offering, from, requirementError.Message, cancellationToken) switch
            {
                ClassWriteOutcome.Saved => ScheduledPublishOutcome.Unscheduled,
                _ => ScheduledPublishOutcome.Lost,
            };
        }

        offering.PublishedAt ??= clock.UtcNow;
        offering.ScheduledFor = null;
        offering.Status = PublishStatus.Published;

        return await dataLayer.TransitionAsync(
            offering, PublishCommand.Publish, from, ScheduledPublishPolicy.Actor, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => ScheduledPublishOutcome.Published,

            // The row version moved under this save: another Worker published it, or staff changed it. Either way it
            // is theirs now.
            _ => ScheduledPublishOutcome.Lost,
        };
    }

    public async Task<int> SeedAsync(IReadOnlyList<ClassSeed> seeds, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seeds);

        var now = clock.UtcNow;
        var taken = await dataLayer.GetSlugsAsync(cancellationToken);
        var number = await dataLayer.NextOfferingNumberAsync(cancellationToken);
        var offerings = new List<ClassOffering>();
        var reservations = new List<SkuReservation>();

        // Every SKU the seed could suggest, asked once: a seed is not worth failing the whole migration over, so an
        // offering whose suggestions someone has taken is skipped like one whose slug is taken. Which seed takes
        // which number depends on the slugs already in use, so the question covers the whole range of numbers.
        var mostSessions = seeds.Count == 0 ? 0 : seeds.Max(seed => seed.Sessions.Count);
        var candidates = Enumerable
            .Range(number, seeds.Count)
            .SelectMany(offering => Enumerable
                .Range(1, mostSessions)
                .Select(session => SkuPolicy.ForClassSession(offering, session)))
            .ToList();
        var owned = await dataLayer.SkuOwnersAsync(candidates, cancellationToken);

        foreach (var seed in seeds)
        {
            var slug = Slugs.FromName(seed.Title);
            if (!taken.Add(slug))
            {
                continue;
            }

            var wanted = Enumerable
                .Range(1, seed.Sessions.Count)
                .Select(session => SkuPolicy.ForClassSession(number, session))
                .ToList();
            if (wanted.Any(owned.ContainsKey))
            {
                number++;
                continue;
            }

            var offering = new ClassOffering
            {
                Id = Guid.CreateVersion7(now),
                Title = seed.Title,
                Slug = slug,
                Summary = seed.Summary,
                ClassType = seed.ClassType,
                Level = seed.Level,
                Instructor = seed.Instructor,
                Cadence = seed.Cadence,
                StartingPriceCents = seed.StartingPriceCents,
                OfferingNumber = number++,
                Status = PublishStatus.Published,
                PublishedAt = now,
            };

            // Seeds go through the same sanitizer as anything staff write: no write path skips it (content.md).
            SetBody(offering, seed.Body is null ? null : sanitizer.Sanitize(seed.Body));

            for (var index = 0; index < seed.Sessions.Count; index++)
            {
                var session = seed.Sessions[index];
                var sku = SkuPolicy.ForClassSession(offering.OfferingNumber, index + 1);
                offering.Sessions.Add(new ClassSession
                {
                    Id = Guid.CreateVersion7(now),
                    ClassOfferingId = offering.Id,
                    StartsAt = session.StartsAt.ToUniversalTime(),
                    EndsAt = session.EndsAt.ToUniversalTime(),
                    DisplayTimeZone = GalleryTimeZone.Id,
                    Sku = sku,
                    Capacity = session.Capacity,
                });
                reservations.Add(new SkuReservation(sku, offering.Sessions[^1].Id));
            }

            offerings.Add(offering);
        }

        return offerings.Count == 0
            ? 0
            : await dataLayer.AddSeedAsync(offerings, new SkuChanges(reservations, []), actor, cancellationToken);
    }

    /// <summary>
    /// Makes <paramref name="offering"/>'s sessions exactly <paramref name="sent"/>, and returns the SKU
    /// reservations and releases the save owes the shared registry (ADR 0037).
    /// </summary>
    /// <remarks>
    /// A session that stays is updated in place, keeping its id — and so its Square link and its projections. A
    /// session Square already holds money or seats for cannot be dropped: it exists in Square and may have sales.
    /// </remarks>
    private async Task<OperationResult<SkuChanges>> ApplySessionsAsync(
        ClassOffering offering,
        IReadOnlyList<ClassSessionViewModel> sent,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = offering.Sessions.ToDictionary(session => session.Id);
        var keptIds = new HashSet<Guid>();

        foreach (var row in sent)
        {
            if (row.Id is not { } id)
            {
                continue;
            }

            if (!existing.ContainsKey(id) || !keptIds.Add(id))
            {
                return OperationResult.Failure<SkuChanges>(OperationError.Validation(
                    "A session in the list is not this class's.",
                    [("sessions", "Reload the class and try again: one of these sessions is no longer part of it, or is listed twice.")]));
            }
        }

        var dropped = offering.Sessions.Where(session => !keptIds.Contains(session.Id)).ToList();
        if (dropped.Find(session => session.HasSquareRecord) is { } sold)
        {
            return OperationResult.Failure<SkuChanges>(OperationError.Validation(
                "A session Square already holds can't be removed here.",
                [("sessions", $"Session {sold.Sku} is in Square, so it may have sales. Archive the class instead of removing the session.")]));
        }

        // Ordered by start, so a suggested SKU's number follows the order staff read the sessions in.
        var ordered = sent.OrderBy(row => row.StartsAt!.Value).ToList();
        var plans = new List<SessionPlan?>(ordered.Count);
        foreach (var row in ordered)
        {
            var kept = row.Id is { } id ? existing[id] : null;
            var sku = SkuPolicy.Normalize(row.Sku) ?? kept?.Sku;
            plans.Add(sku is null ? null : new SessionPlan(row, kept, sku));
        }

        var suggestions = await SuggestSkusAsync(
            offering, plans.Count(plan => plan is null), plans.Where(plan => plan is not null).Select(plan => plan!.Sku), cancellationToken);

        var queue = new Queue<string>(suggestions);
        for (var index = 0; index < plans.Count; index++)
        {
            if (plans[index] is null)
            {
                plans[index] = new SessionPlan(ordered[index], Kept: null, queue.Dequeue());
            }
        }

        var resolved = plans.Select(plan => plan!).ToList();
        var skus = resolved.Select(plan => plan.Sku).ToList();
        if (skus.Distinct(StringComparer.Ordinal).Count() != skus.Count)
        {
            return OperationResult.Failure<SkuChanges>(OperationError.Validation(
                "Two sessions have the same SKU.",
                [("sessions", "Each session is sold under its own SKU. Give every session a different one.")]));
        }

        // A SKU this offering's own sessions are letting go in this very save is free to take: the DataLayer stages
        // the releases before the reservations (ADR 0037).
        var releasing = dropped.Select(session => session.Id)
            .Concat(resolved
                .Where(plan => plan.Kept is not null && !string.Equals(plan.Kept.Sku, plan.Sku, StringComparison.Ordinal))
                .Select(plan => plan.Kept!.Id))
            .ToHashSet();

        var owners = await dataLayer.SkuOwnersAsync(skus, cancellationToken);
        var clashes = resolved
            .Where(plan => owners.TryGetValue(plan.Sku, out var owner)
                && !(owner.OwnerType == CatalogOwnerTypes.ClassSession
                    && (owner.OwnerId == plan.Kept?.Id || releasing.Contains(owner.OwnerId))))
            .Select(plan => plan.Sku)
            .ToList();

        if (clashes.Count > 0)
        {
            return OperationResult.Failure<SkuChanges>(SkuTaken(clashes));
        }

        // Checked before anything is mutated: a refusal must leave the tracked aggregate as it was found.
        foreach (var plan in resolved)
        {
            if (plan.Kept is { SeatsRemaining: { } remaining } session
                && plan.Sent.Capacity!.Value < session.Capacity - remaining)
            {
                // square.md: seats are Square's record and are never recomputed here, so a capacity below the seats
                // already taken is refused rather than driving Square's count negative (ADR 0032). 5.4a moves this
                // to the seat-adjustment command.
                return OperationResult.Failure<SkuChanges>(OperationError.Validation(
                    "That session has more seats taken than that.",
                    [("sessions", string.Create(CultureInfo.InvariantCulture,
                        $"Session {session.Sku} has {session.Capacity - remaining} of {session.Capacity} seats taken, so its capacity can't go below that."))]));
            }
        }

        var reserve = new List<SkuReservation>();
        var release = dropped.Select(session => new SkuReservation(session.Sku, session.Id)).ToList();
        offering.Sessions.RemoveAll(session => !keptIds.Contains(session.Id));

        foreach (var plan in resolved)
        {
            var startsAt = plan.Sent.StartsAt!.Value.ToUniversalTime();
            var endsAt = plan.Sent.EndsAt!.Value.ToUniversalTime();
            var zone = Clean(plan.Sent.DisplayTimeZone) ?? plan.Kept?.DisplayTimeZone ?? GalleryTimeZone.Id;
            var capacity = plan.Sent.Capacity!.Value;

            if (plan.Kept is { } session)
            {
                if (!string.Equals(session.Sku, plan.Sku, StringComparison.Ordinal))
                {
                    release.Add(new SkuReservation(session.Sku, session.Id));
                    reserve.Add(new SkuReservation(plan.Sku, session.Id));
                }

                session.StartsAt = startsAt;
                session.EndsAt = endsAt;
                session.DisplayTimeZone = zone;
                session.Sku = plan.Sku;
                session.Capacity = capacity;
                continue;
            }

            // A new session carries no projection: price, seats and sold-out are Square's to report (ADR 0032).
            var added = new ClassSession
            {
                Id = Guid.CreateVersion7(now),
                ClassOfferingId = offering.Id,
                StartsAt = startsAt,
                EndsAt = endsAt,
                DisplayTimeZone = zone,
                Sku = plan.Sku,
                Capacity = capacity,
            };
            offering.Sessions.Add(added);
            reserve.Add(new SkuReservation(plan.Sku, added.Id));
        }

        return OperationResult.Success(new SkuChanges(reserve, release));
    }

    /// <summary>
    /// <paramref name="wanted"/> free SKUs for this offering, as <c>CLS-&lt;offering&gt;-&lt;n&gt;</c>: the first
    /// numbers no record in the CMS holds and this write is not already using (ADR 0037).
    /// </summary>
    private async Task<IReadOnlyList<string>> SuggestSkusAsync(
        ClassOffering offering, int wanted, IEnumerable<string> alreadyUsed, CancellationToken cancellationToken)
    {
        if (wanted == 0)
        {
            return [];
        }

        var used = alreadyUsed.ToHashSet(StringComparer.Ordinal);

        // Enough candidates that even if every one of this offering's sessions and every suggestion collided, there
        // is still a free number in the batch — asked in one query rather than one per session.
        var candidates = Enumerable
            .Range(1, wanted + offering.Sessions.Count + used.Count + 1)
            .Select(number => SkuPolicy.ForClassSession(offering.OfferingNumber, number))
            .Where(candidate => !used.Contains(candidate))
            .ToList();

        var owners = await dataLayer.SkuOwnersAsync(candidates, cancellationToken);
        var free = candidates.Where(candidate => !owners.ContainsKey(candidate)).Take(wanted).ToList();

        // Every suggestion in the batch is taken: fall back to the offering's id, which nothing else can hold. Staff
        // can edit it, and the registry still refuses a duplicate.
        while (free.Count < wanted)
        {
            free.Add(string.Create(CultureInfo.InvariantCulture,
                $"{SkuPolicy.ClassPrefix}-{offering.Id.ToString("N")[..8].ToUpperInvariant()}-{free.Count + 1}"));
        }

        return free;
    }

    /// <summary>Who holds the SKUs this offering's sessions now carry, for a clash the save discovered.</summary>
    private async Task<OperationError> SkuRaceAsync(ClassOffering offering, CancellationToken cancellationToken)
    {
        var skus = offering.Sessions.Select(session => session.Sku).ToList();
        var owners = await dataLayer.SkuOwnersAsync(skus, cancellationToken);
        var clashes = skus
            .Where(sku => owners.TryGetValue(sku, out var owner)
                && !(owner.OwnerType == CatalogOwnerTypes.ClassSession
                    && offering.Sessions.Any(session => session.Id == owner.OwnerId)))
            .ToList();

        return SkuTaken(clashes.Count > 0 ? clashes : skus);
    }

    /// <summary>
    /// content.md: what the site needs is checked at publish, not at draft. The title, slug and sessions' shape are
    /// guaranteed by every write; the summary, the starting price, having a session at all and the hero's alt text
    /// are what a draft may leave unfinished. All are reported at once, so the editor fixes everything in one pass.
    /// </summary>
    private static OperationError? PublishRequirementError(ClassOffering offering, ClassMediaCheck? media)
    {
        var problems = new List<(string Field, string Error)>();

        if (string.IsNullOrWhiteSpace(offering.Summary))
        {
            problems.Add(("summary", "Write a summary first: the site's card and search results show it."));
        }

        if (offering.Sessions.Count == 0)
        {
            problems.Add(("sessions", "Add at least one session: a class with no dates can't be booked."));
        }

        if (offering.StartingPriceCents is null)
        {
            problems.Add(("startingPriceCents", "Set what a seat costs. Enter 0 for a free class."));
        }

        if (offering.HeroMediaId is { } heroId)
        {
            if (media is null || media.HeroMediaId != heroId)
            {
                return EntityTag.Conflict(What);
            }

            if (!media.HeroExists)
            {
                problems.Add(("heroMediaId", "The hero image is no longer in the media library. Choose another, or remove it."));
            }
            else if (!media.HeroHasAltText)
            {
                problems.Add(("heroMediaId", "Add alt text to the hero image in Media first."));
            }
        }

        return problems.Count == 0
            ? null
            : OperationError.Validation("This class isn't ready to publish yet.", problems);
    }

    /// <summary>
    /// Whether Media's answer clears <paramref name="heroId"/> for published content. The facade asked about a
    /// particular image; an answer about any other one counts as no answer — a conflict.
    /// </summary>
    private static OperationError? HeroError(Guid heroId, ClassMediaCheck? media, string altTextMessage)
    {
        if (media is null || media.HeroMediaId != heroId)
        {
            return EntityTag.Conflict(What);
        }

        if (!media.HeroExists)
        {
            return OperationError.Validation(
                "The hero image is no longer in the media library.",
                [("heroMediaId", "Choose another image from the media library.")]);
        }

        return media.HeroHasAltText
            ? null
            : OperationError.Validation(altTextMessage, [("heroMediaId", "Add alt text to the image in Media first.")]);
    }

    /// <summary>
    /// The description as it would be stored, or why it can't be: too long once sanitized, a picture without alt
    /// text, or a picture Media doesn't have. The facade asked Media about the same sanitized body; an answer that
    /// doesn't cover a picture counts as Media not having it (ADR 0034).
    /// </summary>
    private (SanitizedHtml? Body, OperationError? Error) PrepareBody(string? raw, ClassMediaCheck media)
    {
        var body = sanitizer.Sanitize(raw);

        if (body.Refused is { } refusal)
        {
            return (null, OperationError.Validation(
                "The class description couldn't be read.",
                [("body", $"{refusal} Paste it as plain text, or simplify its formatting, and try again.")]));
        }

        if (body.Html.Length > ClassPolicy.BodyMaxLength)
        {
            return (null, OperationError.Validation(
                "The description is too long.",
                [("body", string.Create(CultureInfo.InvariantCulture, $"Keep it under {ClassPolicy.BodyMaxLength:N0} characters of formatted text."))]));
        }

        var problems = new List<(string Field, string Error)>();

        var undescribed = body.Images.Count(image => image.AltText is null);
        if (undescribed > 0)
        {
            problems.Add(("body", undescribed == 1
                ? "A picture has no alt text. Describe it for people who can't see it."
                : string.Create(CultureInfo.InvariantCulture, $"{undescribed} pictures have no alt text. Describe each for people who can't see them.")));
        }

        // Not asked about counts as not vouched for: the facade's answer must cover the very body being stored.
        if (body.MediaAssetIds.Any(id => !media.AskedBodyImages.Contains(id) || media.MissingBodyImages.Contains(id)))
        {
            problems.Add(("body", "A picture isn't in the media library, or couldn't be checked. Remove it or choose another, then save again."));
        }

        return problems.Count == 0
            ? (body, null)
            : (null, OperationError.Validation("The class description needs attention.", problems));
    }

    /// <summary>Stores the sanitized body and makes the image references exactly the pictures it shows.</summary>
    private static void SetBody(ClassOffering offering, SanitizedHtml? body)
    {
        offering.Body = body is null || body.IsEmpty ? null : body.Html;

        var wanted = body?.MediaAssetIds ?? [];
        offering.Images.RemoveAll(reference => !wanted.Contains(reference.MediaAssetId));
        foreach (var assetId in wanted.Where(assetId => offering.Images.All(reference => reference.MediaAssetId != assetId)))
        {
            offering.Images.Add(new ClassOfferingMediaReference { ClassOfferingId = offering.Id, MediaAssetId = assetId });
        }
    }

    /// <summary>The short text fields an edit treats alike: sent, current, and how to apply a change.</summary>
    private static IEnumerable<(string Field, string? Sent, string? Current, Action<string?> Apply)> TextFields(
        ClassUpdateViewModel update, ClassOffering offering) =>
    [
        ("type", update.ClassType, offering.ClassType, value => offering.ClassType = value),
        ("level", update.Level, offering.Level, value => offering.Level = value),
        ("instructor", update.Instructor, offering.Instructor, value => offering.Instructor = value),
        ("cadence", update.Cadence, offering.Cadence, value => offering.Cadence = value),
    ];

    /// <summary>The sessions as one comparable string, to tell an edit that changed them from one that did not.</summary>
    private static string Fingerprint(ClassOffering offering) => string.Join(
        "|",
        offering.Sessions
            .OrderBy(session => session.StartsAt)
            .Select(session => string.Create(
                CultureInfo.InvariantCulture,
                $"{session.Id:N}:{session.StartsAt:O}:{session.EndsAt:O}:{session.DisplayTimeZone}:{session.Sku}:{session.Capacity}")));

    /// <summary>
    /// Whether the CMS may still set the starting price: only until Square has reported money or seats for a
    /// session, after which price is Square's record and a change is a command (ADR 0032, 0037).
    /// </summary>
    private static bool StartingPriceEditable(ClassOffering offering) =>
        !offering.Sessions.Any(session => session.HasSquareRecord);

    /// <summary>The audit line, with what the sanitizer removed (in words) when it removed anything.</summary>
    private static string AuditSummary(string action, List<string> changes, SanitizedHtml? body)
    {
        var summary = changes.Count == 0 ? $"{action}." : $"{action}: {string.Join(", ", changes)}.";
        if (body is { Removed.Count: > 0 })
        {
            summary += $" Sanitizer removed: {string.Join("; ", body.Removed)}.";
        }

        return summary.Length <= AuditPolicy.SummaryMaxLength ? summary : summary[..(AuditPolicy.SummaryMaxLength - 1)] + "…";
    }

    private static ClassServiceModel ToServiceModel(ClassOffering offering) => new(
        offering.Id,
        offering.Title,
        offering.Slug,
        offering.Summary,
        offering.Body,
        offering.ClassType,
        offering.Level,
        offering.Instructor,
        offering.Cadence,
        offering.StartingPriceCents,
        CatalogPolicy.Currency,
        StartingPriceEditable(offering),
        offering.OfferingNumber,
        offering.HeroMediaId,
        [.. offering.Images.Select(reference => reference.MediaAssetId)],
        [.. offering.Sessions
            .OrderBy(session => session.StartsAt)
            .Select(session => new ClassSessionServiceModel(
                session.Id,
                session.StartsAt,
                session.EndsAt,
                session.DisplayTimeZone,
                session.Sku,
                session.Capacity,
                session.PriceCents,
                session.SeatsRemaining,
                session.SoldOut,
                session.HasSquareRecord))],
        offering.Status,
        offering.PublishedAt,
        offering.ScheduledFor,
        SlugLocked: offering.PublishedAt is not null,
        offering.LegacyWordPressId,
        offering.CreatedAt,
        offering.UpdatedAt,
        offering.UpdatedBy,
        EntityTag.Format(offering.RowVersion));

    /// <summary>Trimmed, and <c>null</c> when nothing is left: an empty string clears the field.</summary>
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static OperationError TransitionInvalid(PublishStatus from, PublishCommand command) => OperationError.Of(
        ContentErrorCodes.StatusTransitionInvalid,
        $"This class is {from.ToString().ToLowerInvariant()}, so “{command.Verb()}” doesn't apply. Reload it to see its current status.");

    private static OperationResult<ClassServiceModel> NotFound() =>
        OperationResult.Failure<ClassServiceModel>(OperationError.Of(ErrorCodes.NotFound, "There is no such class."));

    private static OperationResult<ClassServiceModel> Conflict() =>
        OperationResult.Failure<ClassServiceModel>(EntityTag.Conflict(What));

    private static OperationError SlugTaken(string slug) => new(
        ContentErrorCodes.SlugTaken,
        "Another class already has this web address.",
        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = [$"“{slug}” is taken. Choose another."] });

    private static OperationError SkuTaken(List<string> skus) => new(
        CatalogErrorCodes.SkuTaken,
        skus.Count == 1
            ? $"The SKU “{skus[0]}” is already in use."
            : "Some of those SKUs are already in use.",
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["sessions"] = [.. skus.Distinct(StringComparer.Ordinal).Select(sku =>
                $"“{sku}” belongs to another class session or shop item. Choose another.")],
        });
}
