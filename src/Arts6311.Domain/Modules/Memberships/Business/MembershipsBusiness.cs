using System.Globalization;
using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Memberships.Data;
using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Memberships.Business;

/// <summary>
/// The membership rules: slugs are unique; a tier is published only once it has a price and something to offer;
/// the perks are an ordered list the CMS owns outright; the starting price and cadence are draft fields until
/// Square holds the plan; and Square's record of the price and the cadence is never written from here (ADR 0005).
/// </summary>
internal sealed class MembershipsBusiness(IMembershipsDataLayer dataLayer, IClock clock) : IMembershipsBusiness
{
    private const string What = "this plan";

    public async Task<OperationResult<CursorPage<MembershipPlanSummaryServiceModel>>> ListAsync(
        MembershipPlanListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var scope = string.Create(CultureInfo.InvariantCulture, $"memberships|status={request.Status}|search={search}");

        MembershipPlanListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded
                || !int.TryParse(decoded.Value!.SortValue, CultureInfo.InvariantCulture, out var afterOrder)
                || !Guid.TryParseExact(decoded.Value.TieBreaker, "N", out var afterId))
            {
                return OperationResult.Failure<CursorPage<MembershipPlanSummaryServiceModel>>(
                    OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));
            }

            after = new MembershipPlanListPosition(afterOrder, afterId);
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListAsync(after, limit + 1, request.Status, search, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(
                last.DisplayOrder.ToString(CultureInfo.InvariantCulture), last.Id.ToString("N")).Encode(scope);
        }

        return OperationResult.Success(new CursorPage<MembershipPlanSummaryServiceModel>(rows, next));
    }

    public async Task<OperationResult<MembershipPlanServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var plan = await dataLayer.FindAsync(id, cancellationToken);
        return plan is null ? NotFound() : OperationResult.Success(ToServiceModel(plan));
    }

    public async Task<OperationResult<MembershipPlanServiceModel>> CreateAsync(
        MembershipPlanCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);

        var name = create.Name.Trim();
        var slug = create.Slug ?? Slugs.FromName(name);

        if (await dataLayer.SlugExistsAsync(slug, exceptId: null, cancellationToken))
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(SlugTaken(slug));
        }

        var plan = new MembershipPlan
        {
            Id = Guid.CreateVersion7(clock.UtcNow),
            Name = name,
            Slug = slug,
            DiscountPercent = create.DiscountPercent ?? 0,
            Featured = create.Featured == true,
            DisplayOrder = create.DisplayOrder ?? await dataLayer.NextDisplayOrderAsync(cancellationToken),
            StartingPriceCents = create.StartingPriceCents,
            StartingCadence = create.StartingCadence ?? MembershipCadence.Annual,
            Status = PublishStatus.Draft,
        };
        ReplacePerks(plan, create.Perks ?? []);

        var summary = AuditSummary("Created as Draft", []);
        return await dataLayer.CreateAsync(plan, actorUserId, summary, cancellationToken) switch
        {
            MembershipWriteOutcome.Saved => OperationResult.Success(ToServiceModel(plan)),
            MembershipWriteOutcome.SlugTaken => OperationResult.Failure<MembershipPlanServiceModel>(SlugTaken(slug)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<MembershipPlanServiceModel>> UpdateAsync(
        Guid id,
        MembershipPlanUpdateViewModel update,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var plan = await dataLayer.FindAsync(id, cancellationToken);
        if (plan is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(plan.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(versionError);
        }

        var isPublic = plan.Status is PublishStatus.Published or PublishStatus.Scheduled;

        // What the edit asks for, worked out before anything is written to the tracked plan.
        var name = update.Name?.Trim();
        var slug = update.Slug is not null && !string.Equals(update.Slug, plan.Slug, StringComparison.Ordinal)
            ? update.Slug
            : null;
        var perks = update.Perks is { } wantedPerks
            && !PerksOf(plan).SequenceEqual(wantedPerks.Select(perk => perk.Trim()), StringComparer.Ordinal)
            ? wantedPerks
            : null;
        var price = update.StartingPriceCents is not null || update.RemoveStartingPrice == true
            ? (Wanted: update.RemoveStartingPrice == true ? (long?)null : update.StartingPriceCents, Sent: true)
            : (Wanted: plan.StartingPriceCents, Sent: false);
        var priceChanges = price.Sent && price.Wanted != plan.StartingPriceCents;
        var cadence = update.StartingCadence is { } wantedCadence && wantedCadence != plan.StartingCadence
            ? wantedCadence
            : (MembershipCadence?)null;

        // Every refusal comes first, so one leaves the tracked plan exactly as it was found: nothing below this
        // point can fail, and an edit that is refused writes nothing anywhere.
        if (perks is { Count: 0 } && isPublic)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(OperationError.Validation(
                "This tier is published, so it keeps its perks.",
                [("perks", "The memberships page lists them. Edit them rather than clearing them.")]));
        }

        // The same rule for the price: the page shows one on every card, and publishing refuses a tier without one
        // (PublishRequirementError), so clearing it while the tier is public would leave a blank card.
        if (priceChanges && price.Wanted is null && isPublic && plan.PriceCents is null)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(OperationError.Validation(
                "This tier is published, so it keeps a price.",
                [("startingPriceCents", "The memberships page shows it on the card. Edit it rather than clearing it.")]));
        }

        if (slug is not null && await dataLayer.SlugExistsAsync(slug, plan.Id, cancellationToken))
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(SlugTaken(slug));
        }

        // ADR 0005: the starting price and cadence are the CMS's only until Square has reported money or a
        // cadence. From then on both are Square's record, and the CMS does not propose a new one.
        if (priceChanges && !StartingPriceEditable(plan))
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(SquareHoldsBilling("startingPriceCents"));
        }

        if (cadence is not null && !StartingPriceEditable(plan))
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(SquareHoldsBilling("startingCadence"));
        }

        var changes = new List<string>();

        if (name is not null && !string.Equals(name, plan.Name, StringComparison.Ordinal))
        {
            plan.Name = name;
            changes.Add("name");
        }

        if (slug is not null)
        {
            // No redirect is owed, whatever the plan's status: every plan is shown on the one page, so its slug was
            // never an address anyone could have (ADR 0040).
            plan.Slug = slug;
            changes.Add("slug");
        }

        if (perks is not null)
        {
            ReplacePerks(plan, perks);
            changes.Add(perks.Count == 0 ? "perks cleared" : "perks");
        }

        if (update.DiscountPercent is { } discount && discount != plan.DiscountPercent)
        {
            plan.DiscountPercent = discount;
            changes.Add("class discount");
        }

        if (update.Featured is { } featured && featured != plan.Featured)
        {
            plan.Featured = featured;
            changes.Add(featured ? "featured" : "no longer featured");
        }

        if (update.DisplayOrder is { } order && order != plan.DisplayOrder)
        {
            plan.DisplayOrder = order;
            changes.Add("order");
        }

        if (priceChanges)
        {
            plan.StartingPriceCents = price.Wanted;
            changes.Add(price.Wanted is null ? "starting price cleared" : "starting price");
        }

        if (cadence is { } wanted)
        {
            plan.StartingCadence = wanted;
            changes.Add("billing cadence");
        }

        if (changes.Count == 0)
        {
            return OperationResult.Success(ToServiceModel(plan));
        }

        return await dataLayer.UpdateAsync(plan, actorUserId, AuditSummary("Updated", changes), cancellationToken) switch
        {
            MembershipWriteOutcome.Saved => OperationResult.Success(ToServiceModel(plan)),
            MembershipWriteOutcome.SlugTaken => OperationResult.Failure<MembershipPlanServiceModel>(SlugTaken(plan.Slug)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<MembershipPlanServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var plan = await dataLayer.FindAsync(id, cancellationToken);
        if (plan is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(plan.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(versionError);
        }

        var from = plan.Status;
        if (PublishLifecycle.Next(from, command) is not { } to)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(TransitionInvalid(from, command));
        }

        if (command == PublishCommand.Publish)
        {
            if (PublishRequirementError(plan) is { } requirementError)
            {
                return OperationResult.Failure<MembershipPlanServiceModel>(requirementError);
            }

            plan.PublishedAt ??= clock.UtcNow;
            plan.ScheduledFor = null;
        }
        else if (command == PublishCommand.Archive)
        {
            // An archived plan is never published by the scheduled publisher, so a time left on it would only
            // mislead the staff reading it.
            plan.ScheduledFor = null;
        }

        plan.Status = to;

        return await dataLayer.TransitionAsync(plan, command, from, actorUserId, cancellationToken) switch
        {
            MembershipWriteOutcome.Saved => OperationResult.Success(ToServiceModel(plan)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<MembershipPlanServiceModel>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var plan = await dataLayer.FindAsync(id, cancellationToken);
        if (plan is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(plan.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(versionError);
        }

        var from = plan.Status;
        if (PublishLifecycle.Next(from, PublishCommand.Schedule) is not { } to)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(TransitionInvalid(from, PublishCommand.Schedule));
        }

        // Stored and answered as UTC, whatever offset the caller sent: the same instant, spelt the one way
        // api-contract.md requires ("+00:00", never a local offset and never "Z").
        var when = scheduledFor.ToUniversalTime();

        // A time already past would be published by the next pass, a minute later, which is not what "schedule"
        // means: staff who want it out now have the publish command for that.
        if (when <= clock.UtcNow)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(OperationError.Validation(
                "Choose a time in the future.",
                [("scheduledFor", "This time has passed. Publish the tier now instead, or pick a later time.")]));
        }

        // Checked now as well as at publish, so staff hear about a missing price or an empty perk list while they
        // are looking at the screen rather than at midnight (ADR 0036).
        if (PublishRequirementError(plan) is { } requirementError)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(requirementError);
        }

        plan.ScheduledFor = when;
        plan.Status = to;

        return await dataLayer.TransitionAsync(plan, PublishCommand.Schedule, from, actorUserId, cancellationToken) switch
        {
            MembershipWriteOutcome.Saved => OperationResult.Success(ToServiceModel(plan)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<MembershipPlanServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var plan = await dataLayer.FindAsync(id, cancellationToken);
        if (plan is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(plan.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(versionError);
        }

        var from = plan.Status;
        if (PublishLifecycle.Next(from, PublishCommand.Unschedule) is not { } to)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(TransitionInvalid(from, PublishCommand.Unschedule));
        }

        plan.ScheduledFor = null;
        plan.Status = to;

        return await dataLayer.TransitionAsync(plan, PublishCommand.Unschedule, from, actorUserId, cancellationToken) switch
        {
            MembershipWriteOutcome.Saved => OperationResult.Success(ToServiceModel(plan)),
            _ => Conflict(),
        };
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        dataLayer.ListDueAsync(asOf, take, cancellationToken);

    public async Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var plan = await dataLayer.FindAsync(id, cancellationToken);

        // Read again inside this scope, because the due list is a snapshot: staff may have published, rescheduled
        // or archived the plan since, and another Worker may have taken it.
        if (plan is null
            || plan.Status != PublishStatus.Scheduled
            || plan.ScheduledFor is null
            || plan.ScheduledFor > asOf)
        {
            return ScheduledPublishOutcome.Skipped;
        }

        var from = plan.Status;
        if (PublishRequirementError(plan) is { } requirementError)
        {
            // Back to Draft rather than left Scheduled: a record whose time has passed would otherwise be retried
            // every minute for ever, and nobody would be told (ADR 0036).
            plan.ScheduledFor = null;
            plan.Status = PublishStatus.Draft;

            return await dataLayer.AbandonScheduleAsync(plan, from, requirementError.Message, cancellationToken) switch
            {
                MembershipWriteOutcome.Saved => ScheduledPublishOutcome.Unscheduled,
                _ => ScheduledPublishOutcome.Lost,
            };
        }

        plan.PublishedAt ??= clock.UtcNow;
        plan.ScheduledFor = null;
        plan.Status = PublishStatus.Published;

        return await dataLayer.TransitionAsync(
            plan, PublishCommand.Publish, from, ScheduledPublishPolicy.Actor, cancellationToken) switch
        {
            MembershipWriteOutcome.Saved => ScheduledPublishOutcome.Published,

            // The row version moved under this save: another Worker published it, or staff changed it. Either way
            // it is theirs now.
            _ => ScheduledPublishOutcome.Lost,
        };
    }

    public async Task<int> SeedAsync(
        IReadOnlyList<MembershipPlanSeed> seeds, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seeds);

        var now = clock.UtcNow;
        var taken = await dataLayer.GetSlugsAsync(cancellationToken);
        var plans = new List<MembershipPlan>();

        foreach (var seed in seeds)
        {
            if (!taken.Add(seed.Slug))
            {
                continue;
            }

            var plan = new MembershipPlan
            {
                Id = Guid.CreateVersion7(now),
                Name = seed.Name,
                Slug = seed.Slug,
                DiscountPercent = seed.DiscountPercent,
                Featured = seed.Featured,
                DisplayOrder = seed.DisplayOrder,
                StartingPriceCents = seed.StartingPriceCents,
                StartingCadence = seed.Cadence,
                Status = PublishStatus.Published,
                PublishedAt = now,
            };
            ReplacePerks(plan, seed.Perks);
            plans.Add(plan);
        }

        return plans.Count == 0 ? 0 : await dataLayer.AddSeedAsync(plans, actor, cancellationToken);
    }

    /// <summary>
    /// content.md: what the site needs is checked at publish, not at draft. The name and slug are guaranteed by
    /// every write; the price and the perks are what a draft may leave unfinished. Both are reported at once, so
    /// the editor fixes everything in one pass.
    /// </summary>
    private static OperationError? PublishRequirementError(MembershipPlan plan)
    {
        var problems = new List<(string Field, string Error)>();

        if (plan.StartingPriceCents is null && plan.PriceCents is null)
        {
            problems.Add(("startingPriceCents", "Set what the tier costs: the page shows a price on every card."));
        }

        if (plan.Perks.Count == 0)
        {
            problems.Add(("perks", "Add at least one perk: a tier with nothing listed gives members nothing to read."));
        }

        return problems.Count == 0
            ? null
            : OperationError.Validation("This tier isn't ready to publish yet.", problems);
    }

    /// <summary>
    /// Makes the perks exactly <paramref name="perks"/>, in that order. A line that stays is rewritten where it
    /// sits rather than removed and added again, which the (plan, position) key would refuse in one save.
    /// </summary>
    private static void ReplacePerks(MembershipPlan plan, IReadOnlyList<string> perks)
    {
        plan.Perks.RemoveAll(perk => perk.Position >= perks.Count);

        for (var position = 0; position < perks.Count; position++)
        {
            var text = perks[position].Trim();
            if (plan.Perks.Find(perk => perk.Position == position) is { } existing)
            {
                existing.Text = text;
            }
            else
            {
                plan.Perks.Add(new MembershipPlanPerk
                {
                    MembershipPlanId = plan.Id,
                    Position = position,
                    Text = text,
                });
            }
        }
    }

    private static IEnumerable<string> PerksOf(MembershipPlan plan) =>
        plan.Perks.OrderBy(perk => perk.Position).Select(perk => perk.Text);

    /// <summary>
    /// Whether the CMS may still set the starting price and cadence: only until Square has reported money or a
    /// cadence, after which both are Square's record (ADR 0005, 0040).
    /// </summary>
    private static bool StartingPriceEditable(MembershipPlan plan) => !plan.HasSquareRecord;

    private static string AuditSummary(string action, List<string> changes) =>
        Trimmed(changes.Count == 0 ? $"{action}." : $"{action}: {string.Join(", ", changes)}.");

    private static string Trimmed(string summary) =>
        summary.Length <= AuditPolicy.SummaryMaxLength ? summary : summary[..(AuditPolicy.SummaryMaxLength - 1)] + "…";

    private static MembershipPlanServiceModel ToServiceModel(MembershipPlan plan) => new(
        plan.Id,
        plan.Name,
        plan.Slug,
        [.. PerksOf(plan)],
        plan.DiscountPercent,
        plan.Featured,
        plan.DisplayOrder,
        plan.StartingPriceCents,
        plan.StartingCadence,
        CatalogPolicy.Currency,
        StartingPriceEditable(plan),
        plan.PriceCents,
        plan.Cadence,
        plan.HasSquareRecord,
        plan.Status,
        plan.PublishedAt,
        plan.ScheduledFor,
        MembershipPolicy.SitePath,
        plan.LegacyWordPressId,
        plan.CreatedAt,
        plan.UpdatedAt,
        plan.UpdatedBy,
        EntityTag.Format(plan.RowVersion));

    private static OperationError TransitionInvalid(PublishStatus from, PublishCommand command) => OperationError.Of(
        ContentErrorCodes.StatusTransitionInvalid,
        $"This tier is {from.ToString().ToLowerInvariant()}, so “{command.Verb()}” doesn't apply. Reload it to see its current status.");

    private static OperationError SquareHoldsBilling(string field) => OperationError.Validation(
        "Square holds this tier's billing now.",
        [(field, "The starting price and cadence only apply before the plan reaches Square; change them there.")]);

    private static OperationResult<MembershipPlanServiceModel> NotFound() =>
        OperationResult.Failure<MembershipPlanServiceModel>(
            OperationError.Of(ErrorCodes.NotFound, "There is no such membership plan."));

    private static OperationResult<MembershipPlanServiceModel> Conflict() =>
        OperationResult.Failure<MembershipPlanServiceModel>(EntityTag.Conflict(What));

    private static OperationError SlugTaken(string slug) => new(
        ContentErrorCodes.SlugTaken,
        "Another membership plan already has this key.",
        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = [$"“{slug}” is taken. Choose another."] });
}
