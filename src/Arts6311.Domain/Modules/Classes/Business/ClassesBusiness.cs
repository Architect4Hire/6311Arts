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
/// each run holds a SKU no other record in the CMS has and names an instructor who is not retired (ADR 0037, 0044);
/// and Square's record of price, seats and sold-out is never written from here (square.md, ADR 0032).
/// </summary>
internal sealed class ClassesBusiness(IClassesDataLayer dataLayer, IHtmlSanitizer sanitizer, IClock clock) : IClassesBusiness
{
    private const string What = "this class";

    /// <summary>What one run's resolution came to: the row to keep (if any) and the SKU it ends up with.</summary>
    private sealed record RunPlan(ClassRunViewModel Sent, ClassRun? Kept, string Sku);

    public async Task<OperationResult<CursorPage<ClassListRow>>> ListAsync(
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
                return OperationResult.Failure<CursorPage<ClassListRow>>(
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

        return OperationResult.Success(new CursorPage<ClassListRow>(rows, next));
    }

    /// <remarks>
    /// Its own cursor scope, so a cursor from the status list is not valid here and the other way about. Status is
    /// not part of it: the Deleted view shows every deleted record, whatever it was when it went (ADR 0052).
    /// </remarks>
    public async Task<OperationResult<CursorPage<ClassDeletedServiceModel>>> ListDeletedAsync(
        ClassListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var scope = string.Create(CultureInfo.InvariantCulture, $"classes|deleted|search={search}");

        ClassListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded || !Guid.TryParseExact(decoded.Value!.TieBreaker, "N", out var afterId))
            {
                return OperationResult.Failure<CursorPage<ClassDeletedServiceModel>>(
                    OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));
            }

            after = new ClassListPosition(decoded.Value.SortValue, afterId);
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListDeletedAsync(after, limit + 1, search, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(last.Title, last.Id.ToString("N")).Encode(scope);
        }

        return OperationResult.Success(new CursorPage<ClassDeletedServiceModel>([.. rows.Select(row => row.ToServiceModel())], next));
    }

    public async Task<OperationResult> DeleteAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var offering = await dataLayer.FindAsync(id, cancellationToken);
        if (offering is null)
        {
            return OperationResult.Failure(OperationError.Of(ErrorCodes.NotFound, "There is no such class."));
        }

        if (EntityTag.Verify(offering.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure(versionError);
        }

        // Every run's SKU goes back to the pool with the class: the one that replaces it can use them (ADR 0037).
        var released = new SkuChanges([], [.. offering.Runs.Select(run => new SkuReservation(run.Sku, run.Id))]);

        // From any status, Published included: "this was a mistake" is not a lifecycle step, and the delete stages
        // the revalidation that takes it off the site (ADR 0052).
        return await dataLayer.DeleteAsync(offering, released, actorUserId, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => OperationResult.Success(),
            _ => OperationResult.Failure(EntityTag.Conflict(What)),
        };
    }

    public async Task<OperationResult<ClassDetail>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var offering = await dataLayer.FindDeletedAsync(id, cancellationToken);
        if (offering is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(offering.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ClassDetail>(versionError);
        }

        // The delete freed the address, so another record may be living there now. Refused rather than renamed for
        // the staff member: which of the two keeps it is their decision (ADR 0052).
        if (await dataLayer.SlugExistsAsync(offering.Slug, offering.Id, cancellationToken))
        {
            return OperationResult.Failure<ClassDetail>(SlugTaken(offering.Slug));
        }

        // The delete released each run's SKU, so they may be someone else's now.
        var wanted = offering.Runs.Select(run => run.Sku).ToList();
        var owners = await dataLayer.SkuOwnersAsync(wanted, cancellationToken);
        var clashes = offering.Runs
            .Where(run => owners.TryGetValue(run.Sku, out var owner) && owner.OwnerId != run.Id)
            .Select(run => run.Sku)
            .ToList();
        if (clashes.Count > 0)
        {
            return OperationResult.Failure<ClassDetail>(SkuTaken(clashes));
        }

        var reserved = new SkuChanges([.. offering.Runs.Select(run => new SkuReservation(run.Sku, run.Id))], []);

        var deletedAs = offering.Status;

        // Back as a Draft, never straight back onto the site: what publishing requires may no longer hold, and a
        // restore is a decision to look at the record again, not to publish it (ADR 0052).
        offering.Status = PublishStatus.Draft;
        offering.ScheduledFor = null;

        return await dataLayer.RestoreAsync(offering, reserved, deletedAs, actorUserId, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => OperationResult.Success(ToDetail(offering)),
            ClassWriteOutcome.SlugTaken => OperationResult.Failure<ClassDetail>(SlugTaken(offering.Slug)),
            ClassWriteOutcome.SkuTaken => OperationResult.Failure<ClassDetail>(SkuTaken(wanted)),
            _ => Conflict(),
        };
    }


    public async Task<OperationResult<ClassDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var offering = await dataLayer.FindAsync(id, cancellationToken);
        return offering is null ? NotFound() : OperationResult.Success(ToDetail(offering));
    }

    public async Task<OperationResult<ClassDetail>> CreateAsync(
        ClassCreateViewModel create,
        ClassMediaCheck media,
        ClassInstructorCheck instructors,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(instructors);

        var title = create.Title.Trim();
        // An address the request names is kept or refused; one made from the title takes the next free number instead.
        var slug = create.Slug ?? await Slugs.FirstFreeAsync(
            Slugs.FromName(title),
            (candidate, token) => dataLayer.SlugExistsAsync(candidate, exceptId: null, token),
            cancellationToken);

        var (body, bodyError) = PrepareBody(create.Body, media);
        if (bodyError is not null)
        {
            return OperationResult.Failure<ClassDetail>(bodyError);
        }

        if (create.Slug is not null && await dataLayer.SlugExistsAsync(slug, exceptId: null, cancellationToken))
        {
            return OperationResult.Failure<ClassDetail>(SlugTaken(slug));
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
            HeroMediaId = create.HeroMediaId,
            OfferingNumber = await dataLayer.NextOfferingNumberAsync(cancellationToken),
            Status = PublishStatus.Draft,
        };
        SetBody(offering, body);

        var runs = await ApplyRunsAsync(offering, create.Runs ?? [], instructors, now, cancellationToken);
        if (!runs.Succeeded)
        {
            return OperationResult.Failure<ClassDetail>(runs.Error!);
        }

        var summary = AuditSummary("Created as Draft", [], body);
        return await dataLayer.CreateAsync(offering, runs.Value!, actorUserId, summary, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => OperationResult.Success(ToDetail(offering)),
            ClassWriteOutcome.SlugTaken => OperationResult.Failure<ClassDetail>(SlugTaken(slug)),
            ClassWriteOutcome.SkuTaken => OperationResult.Failure<ClassDetail>(await SkuRaceAsync(offering, cancellationToken)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ClassDetail>> UpdateAsync(
        Guid id,
        ClassUpdateViewModel update,
        string? ifMatch,
        ClassMediaCheck? media,
        ClassInstructorCheck? instructors,
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
            return OperationResult.Failure<ClassDetail>(versionError);
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
                return OperationResult.Failure<ClassDetail>(SlugTaken(update.Slug));
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
                return OperationResult.Failure<ClassDetail>(OperationError.Validation(
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
                return OperationResult.Failure<ClassDetail>(bodyError);
            }

            var stored = body!.IsEmpty ? null : body.Html;
            if (!string.Equals(stored, offering.Body, StringComparison.Ordinal))
            {
                SetBody(offering, body);
                changes.Add(stored is null ? "more details cleared" : "more details");
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

        if (update.HeroMediaId is { } heroId && heroId != offering.HeroMediaId)
        {
            // media.md: an image on published content has alt text. A draft may wait for it until publish; content
            // that is (or is about to be) public may not take an image without it.
            if (isPublic && HeroError(heroId, media, "This class is published, so a new hero image needs alt text first.") is { } heroError)
            {
                return OperationResult.Failure<ClassDetail>(heroError);
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
        if (update.Runs is { } sentRuns)
        {
            var before = Fingerprint(offering);
            var plan = await ApplyRunsAsync(
                offering, sentRuns, instructors ?? ClassInstructorCheck.None, clock.UtcNow, cancellationToken);
            if (!plan.Succeeded)
            {
                return OperationResult.Failure<ClassDetail>(plan.Error!);
            }

            skuChanges = plan.Value!;
            if (!string.Equals(before, Fingerprint(offering), StringComparison.Ordinal))
            {
                changes.Add(offering.Runs.Count == 0 ? "runs cleared" : "runs");
            }
        }

        if (changes.Count == 0)
        {
            return OperationResult.Success(ToDetail(offering));
        }

        var auditLine = AuditSummary("Updated", changes, body);
        return await dataLayer.UpdateAsync(offering, skuChanges, actorUserId, auditLine, pathChange, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => OperationResult.Success(ToDetail(offering)),
            ClassWriteOutcome.SlugTaken => OperationResult.Failure<ClassDetail>(SlugTaken(offering.Slug)),
            ClassWriteOutcome.SkuTaken => OperationResult.Failure<ClassDetail>(await SkuRaceAsync(offering, cancellationToken)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ClassDetail>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ClassMediaCheck? media,
        ClassInstructorCheck? instructors,
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
            return OperationResult.Failure<ClassDetail>(versionError);
        }

        var from = offering.Status;
        if (PublishLifecycle.Next(from, command) is not { } to)
        {
            return OperationResult.Failure<ClassDetail>(TransitionInvalid(from, command));
        }

        if (command == PublishCommand.Publish)
        {
            if (PublishRequirementError(offering, media, instructors) is { } requirementError)
            {
                return OperationResult.Failure<ClassDetail>(requirementError);
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
            ClassWriteOutcome.Saved => OperationResult.Success(ToDetail(offering)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ClassDetail>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        ClassMediaCheck? media,
        ClassInstructorCheck? instructors,
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
            return OperationResult.Failure<ClassDetail>(versionError);
        }

        var from = offering.Status;
        if (PublishLifecycle.Next(from, PublishCommand.Schedule) is not { } to)
        {
            return OperationResult.Failure<ClassDetail>(TransitionInvalid(from, PublishCommand.Schedule));
        }

        // A time already past would be published by the next pass, a minute later, which is not what "schedule"
        // means: staff who want it out now have the publish command for that.
        if (scheduledFor <= clock.UtcNow)
        {
            return OperationResult.Failure<ClassDetail>(OperationError.Validation(
                "Choose a time in the future.",
                [("scheduledFor", "This time has passed. Publish the class now instead, or pick a later time.")]));
        }

        // Checked now as well as at publish, so staff hear about a missing summary, instructor or session while they
        // are looking at the screen rather than at midnight, when only the audit trail is watching (ADR 0036).
        if (PublishRequirementError(offering, media, instructors) is { } requirementError)
        {
            return OperationResult.Failure<ClassDetail>(requirementError);
        }

        offering.ScheduledFor = scheduledFor;
        offering.Status = to;

        return await dataLayer.TransitionAsync(offering, PublishCommand.Schedule, from, actorUserId, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => OperationResult.Success(ToDetail(offering)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<ClassDetail>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var offering = await dataLayer.FindAsync(id, cancellationToken);
        if (offering is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(offering.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<ClassDetail>(versionError);
        }

        var from = offering.Status;
        if (PublishLifecycle.Next(from, PublishCommand.Unschedule) is not { } to)
        {
            return OperationResult.Failure<ClassDetail>(TransitionInvalid(from, PublishCommand.Unschedule));
        }

        offering.ScheduledFor = null;
        offering.Status = to;

        return await dataLayer.TransitionAsync(offering, PublishCommand.Unschedule, from, actorUserId, cancellationToken) switch
        {
            ClassWriteOutcome.Saved => OperationResult.Success(ToDetail(offering)),
            _ => Conflict(),
        };
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        dataLayer.ListDueAsync(asOf, take, cancellationToken);

    public async Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, ClassMediaCheck? media, ClassInstructorCheck? instructors, CancellationToken cancellationToken)
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
        if (PublishRequirementError(offering, media, instructors) is { } requirementError)
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
        var mostRuns = seeds.Count == 0 ? 0 : seeds.Max(seed => seed.Runs.Count);
        var candidates = Enumerable
            .Range(number, seeds.Count)
            .SelectMany(offering => Enumerable.Range(1, mostRuns).Select(run => SkuPolicy.ForClassRun(offering, run)))
            .ToList();
        var owned = await dataLayer.SkuOwnersAsync(candidates, cancellationToken);

        foreach (var seed in seeds)
        {
            var slug = Slugs.FromName(seed.Title);
            if (!taken.Add(slug))
            {
                continue;
            }

            var wanted = Enumerable.Range(1, seed.Runs.Count).Select(run => SkuPolicy.ForClassRun(number, run)).ToList();
            if (wanted.Any(owned.ContainsKey))
            {
                number++;
                continue;
            }

            // Published, as the spike lists it — unless a run's instructor could not be found, because publishing
            // needs every run to have one (content.md). Such a class is seeded as a Draft for staff to finish.
            var publishable = seed.Runs.All(run => run.InstructorId is not null);
            var offering = new ClassOffering
            {
                Id = Guid.CreateVersion7(now),
                Title = seed.Title,
                Slug = slug,
                Summary = seed.Summary,
                ClassType = seed.ClassType,
                Level = seed.Level,
                OfferingNumber = number++,
                Status = publishable ? PublishStatus.Published : PublishStatus.Draft,
                PublishedAt = publishable ? now : null,
            };

            // Seeds go through the same sanitizer as anything staff write: no write path skips it (content.md).
            SetBody(offering, seed.Body is null ? null : sanitizer.Sanitize(seed.Body));

            for (var index = 0; index < seed.Runs.Count; index++)
            {
                var spec = seed.Runs[index];
                var run = new ClassRun
                {
                    Id = Guid.CreateVersion7(now),
                    ClassOfferingId = offering.Id,
                    Label = spec.Label,
                    InstructorId = spec.InstructorId,
                    Cadence = spec.Cadence,
                    DisplayTimeZone = GalleryTimeZone.Id,
                    Sku = SkuPolicy.ForClassRun(offering.OfferingNumber, index + 1),
                    Capacity = spec.Capacity,
                    StartingPriceCents = spec.StartingPriceCents,
                };

                run.Sessions.AddRange(spec.Sessions.Select(session => new ClassSession
                {
                    Id = Guid.CreateVersion7(now),
                    ClassRunId = run.Id,
                    StartsAt = session.StartsAt.ToUniversalTime(),
                    EndsAt = session.EndsAt.ToUniversalTime(),
                }));

                offering.Runs.Add(run);
                reservations.Add(new SkuReservation(run.Sku, run.Id));
            }

            offerings.Add(offering);
        }

        return offerings.Count == 0
            ? 0
            : await dataLayer.AddSeedAsync(offerings, new SkuChanges(reservations, []), actor, cancellationToken);
    }

    /// <summary>
    /// Makes <paramref name="offering"/>'s runs exactly <paramref name="sent"/>, each with exactly its sent sessions,
    /// and returns the SKU reservations and releases the save owes the shared registry (ADR 0037, 0044).
    /// </summary>
    /// <remarks>
    /// A run that stays is updated in place, keeping its id — and so its Square link and its projections. A run Square
    /// already holds money or seats for cannot be dropped: it exists in Square and may have sales. Every refusal is
    /// decided before anything is mutated, so a refused write leaves the tracked aggregate as it was found.
    /// </remarks>
    private async Task<OperationResult<SkuChanges>> ApplyRunsAsync(
        ClassOffering offering,
        IReadOnlyList<ClassRunViewModel> sent,
        ClassInstructorCheck instructors,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = offering.Runs.ToDictionary(run => run.Id);
        var keptIds = new HashSet<Guid>();

        foreach (var row in sent)
        {
            if (row.Id is not { } id)
            {
                if (row.Sessions?.Any(session => session.Id is not null) == true)
                {
                    return Invalid("A new run can't keep another run's sessions. Reload the class and try again.");
                }

                continue;
            }

            if (!existing.TryGetValue(id, out var kept) || !keptIds.Add(id))
            {
                return Invalid("Reload the class and try again: one of these runs is no longer part of it, or is listed twice.");
            }

            var ownSessions = kept.Sessions.Select(session => session.Id).ToHashSet();
            if (row.Sessions?.Any(session => session.Id is { } sessionId && !ownSessions.Contains(sessionId)) == true)
            {
                return Invalid("Reload the class and try again: a session in the list is not that run's.");
            }
        }

        var dropped = offering.Runs.Where(run => !keptIds.Contains(run.Id)).ToList();
        if (dropped.Find(run => run.HasSquareRecord) is { } sold)
        {
            return OperationResult.Failure<SkuChanges>(OperationError.Validation(
                "A run Square already holds can't be removed here.",
                [("runs", $"Run {sold.Sku} is in Square, so it may have sales. Archive the class instead of removing the run.")]));
        }

        foreach (var row in sent)
        {
            var kept = row.Id is { } id ? existing[id] : null;
            if (InstructorError(row, kept, instructors) is { } instructorError)
            {
                return OperationResult.Failure<SkuChanges>(instructorError);
            }

            // ADR 0032: the starting price is a draft field only until Square holds the run. From then on a change is
            // the change-price command (5.4a), never a field write; sending it unchanged, or not at all, is fine.
            if (kept is { HasSquareRecord: true }
                && row.StartingPriceCents is { } price
                && price != kept.StartingPriceCents)
            {
                return OperationResult.Failure<SkuChanges>(OperationError.Validation(
                    "Square holds this run's price now.",
                    [("runs", $"Run {kept.Sku}: use the change-price action instead. The starting price only applies before the run reaches Square.")]));
            }

            if (kept is { SeatsRemaining: { } remaining } && row.Capacity!.Value < kept.Capacity - remaining)
            {
                // square.md: seats are Square's record and are never recomputed here, so a capacity below the seats
                // already taken is refused rather than driving Square's count negative (ADR 0032). 5.9 moves this to
                // the seat-adjustment command.
                return OperationResult.Failure<SkuChanges>(OperationError.Validation(
                    "That run has more seats taken than that.",
                    [("runs", string.Create(CultureInfo.InvariantCulture,
                        $"Run {kept.Sku} has {kept.Capacity - remaining} of {kept.Capacity} seats taken, so its capacity can't go below that."))]));
            }
        }

        // Ordered by first session, so a suggested SKU's number follows the order staff read the runs in; a run with
        // no sessions yet comes last, in the order it was sent.
        var ordered = sent
            .Select((row, index) => (Row: row, Index: index))
            .OrderBy(entry => entry.Row.Sessions is { Count: > 0 } sessions ? sessions.Min(session => session.StartsAt!.Value) : DateTimeOffset.MaxValue)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Row)
            .ToList();

        var plans = new List<RunPlan?>(ordered.Count);
        foreach (var row in ordered)
        {
            var kept = row.Id is { } id ? existing[id] : null;
            var sku = SkuPolicy.Normalize(row.Sku) ?? kept?.Sku;
            plans.Add(sku is null ? null : new RunPlan(row, kept, sku));
        }

        var suggestions = await SuggestSkusAsync(
            offering, plans.Count(plan => plan is null), plans.Where(plan => plan is not null).Select(plan => plan!.Sku), cancellationToken);

        var queue = new Queue<string>(suggestions);
        for (var index = 0; index < plans.Count; index++)
        {
            plans[index] ??= new RunPlan(ordered[index], Kept: null, queue.Dequeue());
        }

        var resolved = plans.Select(plan => plan!).ToList();
        var skus = resolved.Select(plan => plan.Sku).ToList();
        if (skus.Distinct(StringComparer.Ordinal).Count() != skus.Count)
        {
            return Invalid("Each run is sold under its own SKU. Give every run a different one.", "Two runs have the same SKU.");
        }

        // A SKU this offering's own runs are letting go in this very save is free to take: the DataLayer stages the
        // releases before the reservations (ADR 0037).
        var releasing = dropped.Select(run => run.Id)
            .Concat(resolved
                .Where(plan => plan.Kept is not null && !string.Equals(plan.Kept.Sku, plan.Sku, StringComparison.Ordinal))
                .Select(plan => plan.Kept!.Id))
            .ToHashSet();

        var owners = await dataLayer.SkuOwnersAsync(skus, cancellationToken);
        var clashes = resolved
            .Where(plan => owners.TryGetValue(plan.Sku, out var owner)
                && !(owner.OwnerType == CatalogOwnerTypes.ClassRun
                    && (owner.OwnerId == plan.Kept?.Id || releasing.Contains(owner.OwnerId))))
            .Select(plan => plan.Sku)
            .ToList();

        if (clashes.Count > 0)
        {
            return OperationResult.Failure<SkuChanges>(SkuTaken(clashes));
        }

        // Nothing below refuses: from here the aggregate is changed.
        var reserve = new List<SkuReservation>();
        var release = dropped.Select(run => new SkuReservation(run.Sku, run.Id)).ToList();
        offering.Runs.RemoveAll(run => !keptIds.Contains(run.Id));

        foreach (var plan in resolved)
        {
            var row = plan.Sent;
            var run = plan.Kept;
            if (run is null)
            {
                // A new run carries no projection: price, seats and sold-out are Square's to report (ADR 0032).
                run = new ClassRun { Id = Guid.CreateVersion7(now), ClassOfferingId = offering.Id };
                offering.Runs.Add(run);
                reserve.Add(new SkuReservation(plan.Sku, run.Id));
            }
            else if (!string.Equals(run.Sku, plan.Sku, StringComparison.Ordinal))
            {
                release.Add(new SkuReservation(run.Sku, run.Id));
                reserve.Add(new SkuReservation(plan.Sku, run.Id));
            }

            run.Label = Clean(row.Label);
            run.InstructorId = row.InstructorId;
            run.Cadence = Clean(row.Cadence);
            run.DisplayTimeZone = Clean(row.DisplayTimeZone) ?? (plan.Kept?.DisplayTimeZone is { Length: > 0 } zone ? zone : GalleryTimeZone.Id);
            run.Sku = plan.Sku;
            run.Capacity = row.Capacity!.Value;
            if (!run.HasSquareRecord)
            {
                run.StartingPriceCents = row.StartingPriceCents;
            }

            ApplySessions(run, row.Sessions ?? [], now);
        }

        return OperationResult.Success(new SkuChanges(reserve, release));
    }

    /// <summary>Makes a run's sittings exactly <paramref name="sent"/>: kept ones by id, new ones added, the rest gone.</summary>
    private static void ApplySessions(ClassRun run, IReadOnlyList<ClassSessionViewModel> sent, DateTimeOffset now)
    {
        var keep = sent.Where(row => row.Id is not null).Select(row => row.Id!.Value).ToHashSet();
        run.Sessions.RemoveAll(session => !keep.Contains(session.Id));

        foreach (var row in sent)
        {
            var session = row.Id is { } id ? run.Sessions.First(existing => existing.Id == id) : null;
            if (session is null)
            {
                session = new ClassSession { Id = Guid.CreateVersion7(now), ClassRunId = run.Id };
                run.Sessions.Add(session);
            }

            session.StartsAt = row.StartsAt!.Value.ToUniversalTime();
            session.EndsAt = row.EndsAt!.Value.ToUniversalTime();
        }
    }

    /// <summary>
    /// The instructor a run names must be one the Instructors module has, and not one it has retired — unless the run
    /// already had them, so a class whose teacher has since retired can still be edited (ADR 0033, 0044).
    /// </summary>
    private static OperationError? InstructorError(ClassRunViewModel row, ClassRun? kept, ClassInstructorCheck instructors)
    {
        if (row.InstructorId is not { } instructorId)
        {
            return null;
        }

        if (!instructors.Known.TryGetValue(instructorId, out var instructor))
        {
            return OperationError.Validation(
                "An instructor in the list is not one the centre has.",
                [("runs", "Reload the instructors and choose again: one of them is no longer on the list.")]);
        }

        return instructor.Status == PublishStatus.Archived && kept?.InstructorId != instructorId
            ? OperationError.Validation(
                "An archived instructor can't be given a run.",
                [("runs", $"{instructor.Name} is archived, so they can't be given a new run.")])
            : null;
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

        // Enough candidates that even if every one of this offering's runs and every suggestion collided, there is
        // still a free number in the batch — asked in one query rather than one per run.
        var candidates = Enumerable
            .Range(1, wanted + offering.Runs.Count + used.Count + 1)
            .Select(number => SkuPolicy.ForClassRun(offering.OfferingNumber, number))
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

    /// <summary>Who holds the SKUs this offering's runs now carry, for a clash the save discovered.</summary>
    private async Task<OperationError> SkuRaceAsync(ClassOffering offering, CancellationToken cancellationToken)
    {
        var skus = offering.Runs.Select(run => run.Sku).ToList();
        var owners = await dataLayer.SkuOwnersAsync(skus, cancellationToken);
        var clashes = skus
            .Where(sku => owners.TryGetValue(sku, out var owner)
                && !(owner.OwnerType == CatalogOwnerTypes.ClassRun && offering.Runs.Any(run => run.Id == owner.OwnerId)))
            .ToList();

        return SkuTaken(clashes.Count > 0 ? clashes : skus);
    }

    /// <summary>
    /// content.md: what the site needs is checked at publish, not at draft. The title, slug and runs' shape are
    /// guaranteed by every write; the summary, having a run at all, and each run's instructor, sessions and starting
    /// price, and the hero's alt text, are what a draft may leave unfinished. All are reported at once, so the editor
    /// fixes everything in one pass.
    /// </summary>
    private static OperationError? PublishRequirementError(
        ClassOffering offering, ClassMediaCheck? media, ClassInstructorCheck? instructors)
    {
        var problems = new List<(string Field, string Error)>();

        if (string.IsNullOrWhiteSpace(offering.Summary))
        {
            problems.Add(("summary", "Write a summary first: the site's card and search results show it."));
        }

        if (offering.Runs.Count == 0)
        {
            problems.Add(("runs", "Add at least one run: a class with no dates can't be booked."));
        }

        foreach (var run in offering.Runs.OrderBy(run => run.Sku, StringComparer.Ordinal))
        {
            var name = run.Label is null ? $"Run {run.Sku}" : $"Run {run.Sku} ({run.Label})";

            if (run.InstructorId is null)
            {
                problems.Add(("runs", $"{name} needs an instructor."));
            }
            else if (instructors is not null && !instructors.Known.ContainsKey(run.InstructorId.Value))
            {
                // The instructor was deleted since the run named them (ADR 0052). Publishing needs a real one, and
                // the site would otherwise show the run with no teacher.
                problems.Add(("runs", $"{name}'s instructor has been deleted. Choose another, or restore them."));
            }

            if (run.Sessions.Count == 0)
            {
                problems.Add(("runs", $"{name} needs at least one session."));
            }

            if (run.StartingPriceCents is null && !run.HasSquareRecord)
            {
                problems.Add(("runs", $"{name} needs a price. Enter 0 for a free class."));
            }
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
                "“More details” couldn't be read.",
                [("body", $"{refusal} Paste it as plain text, or simplify its formatting, and try again.")]));
        }

        if (body.Html.Length > ClassPolicy.BodyMaxLength)
        {
            return (null, OperationError.Validation(
                "“More details” is too long.",
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
            : (null, OperationError.Validation("“More details” needs attention.", problems));
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
    ];

    /// <summary>The runs and their sessions as one comparable string, to tell an edit that changed them from one that did not.</summary>
    private static string Fingerprint(ClassOffering offering) => string.Join(
        "|",
        offering.Runs
            .OrderBy(run => run.Id)
            .Select(run => string.Create(
                CultureInfo.InvariantCulture,
                $"{run.Id:N}:{run.Label}:{run.InstructorId:N}:{run.Cadence}:{run.DisplayTimeZone}:{run.Sku}:{run.Capacity}:{run.StartingPriceCents}:")
                + string.Join(",", run.Sessions
                    .OrderBy(session => session.Id)
                    .Select(session => string.Create(CultureInfo.InvariantCulture, $"{session.Id:N}@{session.StartsAt:O}-{session.EndsAt:O}")))));

    public async Task<IReadOnlyList<ClassSiteRow>> ListForSiteAsync(Guid? instructorId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var offerings = await dataLayer.ListPublishedAsync(slug: null, endingAfter: now, cancellationToken);

        return
        [
            .. offerings
                .Select(offering => ToSiteRow(offering, now, instructorId))
                .Where(row => row.Runs.Count > 0)
                .OrderBy(row => row.Runs[0].StartsAt)
                .ThenBy(row => row.Title, StringComparer.Ordinal)
                .ThenBy(row => row.Id),
        ];
    }

    public async Task<ClassSiteRow?> GetForSiteAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);

        var offering = (await dataLayer.ListPublishedAsync(slug, endingAfter: null, cancellationToken)).SingleOrDefault();
        return offering is null ? null : ToSiteRow(offering, clock.UtcNow, instructorId: null);
    }

    public async Task<OperationResult<PreviewRecord<ClassSiteRow>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var offering = await dataLayer.FindAsync(id, cancellationToken);
        return offering is null
            ? OperationResult.Failure<PreviewRecord<ClassSiteRow>>(OperationError.Of(ErrorCodes.NotFound, "There is no such class."))
            : OperationResult.Success(new PreviewRecord<ClassSiteRow>(
                ToSiteRow(offering, clock.UtcNow, instructorId: null), offering.Status));
    }

    public async Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) =>
        [.. (await dataLayer.ListPublishedAsync(slug: null, endingAfter: null, cancellationToken))
            .OrderBy(offering => offering.Slug, StringComparer.Ordinal)
            .Select(offering => new SitemapEntryServiceModel(
                SitePaths.AddressOf(ClassPolicy.SitePathPrefix, offering.Slug), offering.UpdatedAt))];

    /// <summary>
    /// The site's view at <paramref name="now"/>: a run is shown until its last session ends, so one in progress still
    /// is, with its whole schedule — a seat buys every sitting. Given <paramref name="instructorId"/>, only that
    /// instructor's runs.
    /// </summary>
    private static ClassSiteRow ToSiteRow(ClassOffering offering, DateTimeOffset now, Guid? instructorId) => new(
        offering.Id,
        offering.Slug,
        offering.Title,
        offering.Summary,
        offering.Body,
        offering.ClassType,
        offering.Level,
        offering.HeroMediaId,
        [.. offering.Runs
            .Where(run => run.Sessions.Any(session => session.EndsAt > now))
            .Where(run => instructorId is null || run.InstructorId == instructorId)
            .Select(run => new ClassSiteRunRow(
                run.Id,
                run.Label,
                run.InstructorId,
                run.Cadence,
                run.DisplayTimeZone,
                run.Capacity,
                run.PriceCents,
                run.SeatsRemaining,
                run.SoldOut,
                SessionsOf(run)))
            .OrderBy(run => run.StartsAt)
            .ThenBy(run => run.Id)],
        offering.UpdatedAt);

    private static IReadOnlyList<ClassSessionServiceModel> SessionsOf(ClassRun run) =>
        [.. run.Sessions
            .OrderBy(session => session.StartsAt)
            .ThenBy(session => session.Id)
            .Select(session => new ClassSessionServiceModel(session.Id, session.StartsAt, session.EndsAt))];

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

    /// <summary>
    /// The CMS model, runs by their first session (a run with none yet last), with each run's instructor left for the
    /// facade to describe. A run's starting price is editable only until Square has reported money or seats for it,
    /// after which price is Square's record and a change is a command (ADR 0032, 0037).
    /// </summary>
    private static ClassDetail ToDetail(ClassOffering offering)
    {
        var runs = offering.Runs
            .OrderBy(run => run.Sessions.Count == 0 ? DateTimeOffset.MaxValue : run.Sessions.Min(session => session.StartsAt))
            .ThenBy(run => run.Id)
            .ToList();

        var model = new ClassServiceModel(
            offering.Id,
            offering.Title,
            offering.Slug,
            offering.Summary,
            offering.Body,
            offering.ClassType,
            offering.Level,
            offering.OfferingNumber,
            offering.HeroMediaId,
            [.. offering.Images.Select(reference => reference.MediaAssetId)],
            [.. runs.Select(run => new ClassRunServiceModel(
                run.Id,
                run.Label,
                Instructor: null,
                run.Cadence,
                run.DisplayTimeZone,
                run.Sku,
                run.Capacity,
                run.StartingPriceCents,
                CatalogPolicy.Currency,
                StartingPriceEditable: !run.HasSquareRecord,
                run.PriceCents,
                run.SeatsRemaining,
                run.SoldOut,
                run.HasSquareRecord,
                SessionsOf(run)))],
            offering.Status,
            offering.PublishedAt,
            offering.ScheduledFor,
            SlugLocked: offering.PublishedAt is not null,
            offering.LegacyWordPressId,
            offering.CreatedAt,
            offering.UpdatedAt,
            offering.UpdatedBy,
            EntityTag.Format(offering.RowVersion));

        return new ClassDetail(
            model,
            runs.Where(run => run.InstructorId is not null).ToDictionary(run => run.Id, run => run.InstructorId!.Value));
    }

    /// <summary>Trimmed, and <c>null</c> when nothing is left: an empty string clears the field.</summary>
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static OperationResult<SkuChanges> Invalid(string detail, string message = "A run in the list is not this class's.") =>
        OperationResult.Failure<SkuChanges>(OperationError.Validation(message, [("runs", detail)]));

    private static OperationError TransitionInvalid(PublishStatus from, PublishCommand command) => OperationError.Of(
        ContentErrorCodes.StatusTransitionInvalid,
        $"This class is {from.ToString().ToLowerInvariant()}, so “{command.Verb()}” doesn't apply. Reload it to see its current status.");

    private static OperationResult<ClassDetail> NotFound() =>
        OperationResult.Failure<ClassDetail>(OperationError.Of(ErrorCodes.NotFound, "There is no such class."));

    private static OperationResult<ClassDetail> Conflict() =>
        OperationResult.Failure<ClassDetail>(EntityTag.Conflict(What));

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
            ["runs"] = [.. skus.Distinct(StringComparer.Ordinal).Select(sku =>
                $"“{sku}” belongs to another class run or shop item. Choose another.")],
        });
}
