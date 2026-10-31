using System.Globalization;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Instructors.Data;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Instructors.Business;

/// <summary>
/// The instructor rules (ADR 0043, the artist rules of ADR 0031): slugs unique and redirected once published, status
/// changes only along the lifecycle, a portrait with alt text before publishing, and every write checked against the
/// row version the editor read.
/// </summary>
/// <remarks>
/// The linked artist is Artists' to vouch for, so the facade asks it before calling here; Business only records the
/// id and hands it back in <see cref="InstructorDetail"/> for the facade to describe.
/// </remarks>
internal sealed class InstructorsBusiness(IInstructorsDataLayer dataLayer, IClock clock) : IInstructorsBusiness
{
    private const string What = "this instructor";

    public async Task<OperationResult<CursorPage<InstructorSummaryServiceModel>>> ListAsync(
        InstructorListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var scope = string.Create(CultureInfo.InvariantCulture, $"instructors|status={request.Status}|search={search}");

        var order = InstructorListOrder.Columns;

        // The facade's validator has already refused a column this list does not sort by.
        var sort = order.Resolve(request.Sort)
            ?? throw new ArgumentException("This list does not sort by that column.", nameof(request));

        ListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = order.Decode(sort, request.Cursor, scope);
            if (!decoded.Succeeded)
            {
                return OperationResult.Failure<CursorPage<InstructorSummaryServiceModel>>(decoded.Error!);
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

        return OperationResult.Success(new CursorPage<InstructorSummaryServiceModel>(rows, next));
    }

    /// <remarks>
    /// Its own cursor scope, so a cursor from the status list is not valid here and the other way about. Status is
    /// not part of it: the Deleted view shows every deleted record, whatever it was when it went (ADR 0052).
    /// </remarks>
    public async Task<OperationResult<CursorPage<InstructorDeletedServiceModel>>> ListDeletedAsync(
        InstructorListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var scope = string.Create(CultureInfo.InvariantCulture, $"instructors|deleted|search={search}");

        InstructorListPosition? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded || !Guid.TryParseExact(decoded.Value!.TieBreaker, "N", out var afterId))
            {
                return OperationResult.Failure<CursorPage<InstructorDeletedServiceModel>>(
                    OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list."));
            }

            after = new InstructorListPosition(decoded.Value.SortValue, afterId);
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

        return OperationResult.Success(new CursorPage<InstructorDeletedServiceModel>([.. rows.Select(row => row.ToServiceModel())], next));
    }

    public async Task<OperationResult> DeleteAsync(
        Guid id, ContentReferenceCheck references, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var instructor = await dataLayer.FindAsync(id, cancellationToken);
        if (instructor is null)
        {
            return OperationResult.Failure(OperationError.Of(ErrorCodes.NotFound, "There is no such instructor."));
        }

        if (EntityTag.Verify(instructor.RowVersion, ifMatch, What) is { } versionError)
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
        return await dataLayer.DeleteAsync(instructor, actorUserId, cancellationToken) switch
        {
            InstructorWriteOutcome.Saved => OperationResult.Success(),
            _ => OperationResult.Failure(EntityTag.Conflict(What)),
        };
    }

    public async Task<OperationResult<InstructorDetail>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var instructor = await dataLayer.FindDeletedAsync(id, cancellationToken);
        if (instructor is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(instructor.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<InstructorDetail>(versionError);
        }

        // The delete freed the address, so another record may be living there now. Refused rather than renamed for
        // the staff member: which of the two keeps it is their decision (ADR 0052).
        if (await dataLayer.SlugExistsAsync(instructor.Slug, instructor.Id, cancellationToken))
        {
            return OperationResult.Failure<InstructorDetail>(SlugTaken(instructor.Slug));
        }

        var deletedAs = instructor.Status;

        // Back as a Draft, never straight back onto the site: what publishing requires may no longer hold, and a
        // restore is a decision to look at the record again, not to publish it (ADR 0052).
        instructor.Status = PublishStatus.Draft;
        instructor.ScheduledFor = null;

        return await dataLayer.RestoreAsync(instructor, deletedAs, actorUserId, cancellationToken) switch
        {
            InstructorWriteOutcome.Saved => OperationResult.Success(ToServiceModel(instructor)),
            InstructorWriteOutcome.SlugTaken => OperationResult.Failure<InstructorDetail>(SlugTaken(instructor.Slug)),
            _ => Conflict(),
        };
    }


    public async Task<OperationResult<InstructorDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var instructor = await dataLayer.FindAsync(id, cancellationToken);
        return instructor is null ? NotFound() : OperationResult.Success(ToServiceModel(instructor));
    }

    public async Task<IReadOnlyList<InstructorSummaryServiceModel>> GetSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Count == 0 ? [] : await dataLayer.GetSummariesAsync([.. ids.Distinct()], cancellationToken);
    }

    public async Task<IReadOnlyList<InstructorSummaryServiceModel>> FindBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slugs);
        return slugs.Count == 0
            ? []
            : await dataLayer.FindBySlugsAsync([.. slugs.Distinct(StringComparer.Ordinal)], cancellationToken);
    }

    public async Task<IReadOnlyList<InstructorSiteRow>> ListForSiteAsync(CancellationToken cancellationToken) =>
        await dataLayer.ListPublishedAsync(null, cancellationToken);

    public async Task<InstructorSiteRow?> GetForSiteAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        return (await dataLayer.ListPublishedAsync(slug, cancellationToken)).SingleOrDefault();
    }

    public async Task<OperationResult<PreviewRecord<InstructorSiteRow>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var instructor = await dataLayer.FindAsync(id, cancellationToken);
        return instructor is null
            ? OperationResult.Failure<PreviewRecord<InstructorSiteRow>>(OperationError.Of(ErrorCodes.NotFound, "There is no such instructor."))
            : OperationResult.Success(new PreviewRecord<InstructorSiteRow>(
                new InstructorSiteRow(
                    instructor.Id, instructor.Slug, instructor.Name, instructor.Medium, instructor.Active,
                    instructor.PortraitMediaId, instructor.Bio, instructor.Website, instructor.Instagram, instructor.ArtistId,
                    instructor.UpdatedAt),
                instructor.Status));
    }

    /// <remarks>
    /// An unpublished instructor is still named — they teach the run whatever their page's status — but given no
    /// slug, because the site has no page to link to (ADR 0042).
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, SiteInstructorCreditServiceModel>> DescribeCreditsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var instructors = new List<InstructorSummaryServiceModel>();
        foreach (var chunk in ids.Distinct().Chunk(InstructorPolicy.LookupMaxCount))
        {
            instructors.AddRange(await dataLayer.GetSummariesAsync(chunk, cancellationToken));
        }

        return instructors.ToDictionary(
            instructor => instructor.Id,
            instructor => new SiteInstructorCreditServiceModel(
                instructor.Name, instructor.Status == PublishStatus.Published ? instructor.Slug : null));
    }

    public async Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) =>
        [.. (await dataLayer.ListPublishedAsync(null, cancellationToken))
            .Select(row => new SitemapEntryServiceModel(
                SitePaths.AddressOf(InstructorPolicy.SitePathPrefix, row.Slug), row.UpdatedAt))];

    public async Task<OperationResult<InstructorDetail>> CreateAsync(
        InstructorCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
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
            return OperationResult.Failure<InstructorDetail>(SlugTaken(slug));
        }

        var instructor = new Instructor
        {
            Id = Guid.CreateVersion7(clock.UtcNow),
            Name = name,
            Slug = slug,
            Medium = Clean(create.Medium),
            Bio = CleanBio(create.Bio),
            Active = create.Active,
            PortraitMediaId = create.PortraitMediaId,
            Website = Clean(create.Website),
            Instagram = CleanInstagram(create.Instagram),
            ArtistId = create.ArtistId,
            Status = PublishStatus.Draft,
        };

        return await dataLayer.CreateAsync(instructor, actorUserId, cancellationToken) switch
        {
            InstructorWriteOutcome.Saved => OperationResult.Success(ToServiceModel(instructor)),
            InstructorWriteOutcome.SlugTaken => OperationResult.Failure<InstructorDetail>(SlugTaken(slug)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<InstructorDetail>> UpdateAsync(
        Guid id,
        InstructorUpdateViewModel update,
        string? ifMatch,
        InstructorPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var instructor = await dataLayer.FindAsync(id, cancellationToken);
        if (instructor is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(instructor.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<InstructorDetail>(versionError);
        }

        var changes = new List<string>();

        if (update.Name is not null && update.Name.Trim() is var name && !string.Equals(name, instructor.Name, StringComparison.Ordinal))
        {
            instructor.Name = name;
            changes.Add("name");
        }

        PublishedPathChange? pathChange = null;
        if (update.Slug is not null && !string.Equals(update.Slug, instructor.Slug, StringComparison.Ordinal))
        {
            if (await dataLayer.SlugExistsAsync(update.Slug, instructor.Id, cancellationToken))
            {
                return OperationResult.Failure<InstructorDetail>(SlugTaken(update.Slug));
            }

            // A slug that has ever been public is someone's bookmark: the old address is redirected to the new one,
            // staged with this save (content.md, ADR 0035).
            pathChange = PublishedPathChange.For(InstructorPolicy.SitePathPrefix, instructor.Slug, update.Slug, instructor.PublishedAt);
            instructor.Slug = update.Slug;
            changes.Add(pathChange is null ? "slug" : $"slug (redirecting {pathChange.FromPath})");
        }

        if (update.Medium is not null && Clean(update.Medium) is var medium && !string.Equals(medium, instructor.Medium, StringComparison.Ordinal))
        {
            instructor.Medium = medium;
            changes.Add(medium is null ? "medium cleared" : "medium");
        }

        if (update.Bio is not null && CleanBio(update.Bio) is var bio && !string.Equals(bio, instructor.Bio, StringComparison.Ordinal))
        {
            instructor.Bio = bio;
            changes.Add(bio is null ? "bio cleared" : "bio");
        }

        if (update.Active is { } active && active != instructor.Active)
        {
            instructor.Active = active;
            changes.Add(active ? "active" : "inactive");
        }

        if (update.PortraitMediaId is { } portraitId && portraitId != instructor.PortraitMediaId)
        {
            // media.md: an image on published content has alt text. A draft may wait for it until publish; content
            // that is (or is about to be) public may not take an image without it.
            if (instructor.Status is PublishStatus.Published or PublishStatus.Scheduled
                && PortraitError(portraitId, portrait, "This instructor is published, so a new portrait needs alt text first.") is { } portraitError)
            {
                return OperationResult.Failure<InstructorDetail>(portraitError);
            }

            instructor.PortraitMediaId = portraitId;
            changes.Add("portrait");
        }
        else if (update.RemovePortrait == true && instructor.PortraitMediaId is not null)
        {
            instructor.PortraitMediaId = null;
            changes.Add("portrait removed");
        }

        if (update.Website is not null && Clean(update.Website) is var website
            && !string.Equals(website, instructor.Website, StringComparison.Ordinal))
        {
            instructor.Website = website;
            changes.Add(website is null ? "website cleared" : "website");
        }

        if (update.Instagram is not null && CleanInstagram(update.Instagram) is var instagram
            && !string.Equals(instagram, instructor.Instagram, StringComparison.Ordinal))
        {
            instructor.Instagram = instagram;
            changes.Add(instagram is null ? "instagram cleared" : "instagram");
        }

        if (update.ArtistId is { } artistId && artistId != instructor.ArtistId)
        {
            instructor.ArtistId = artistId;
            changes.Add("artist");
        }
        else if (update.RemoveArtist == true && instructor.ArtistId is not null)
        {
            instructor.ArtistId = null;
            changes.Add("artist removed");
        }

        if (changes.Count == 0)
        {
            return OperationResult.Success(ToServiceModel(instructor));
        }

        return await dataLayer.UpdateAsync(instructor, actorUserId, $"Updated: {string.Join(", ", changes)}.", pathChange, cancellationToken) switch
        {
            InstructorWriteOutcome.Saved => OperationResult.Success(ToServiceModel(instructor)),
            InstructorWriteOutcome.SlugTaken => OperationResult.Failure<InstructorDetail>(SlugTaken(instructor.Slug)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<InstructorDetail>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        InstructorPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var instructor = await dataLayer.FindAsync(id, cancellationToken);
        if (instructor is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(instructor.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<InstructorDetail>(versionError);
        }

        var from = instructor.Status;
        if (PublishLifecycle.Next(from, command) is not { } to)
        {
            return OperationResult.Failure<InstructorDetail>(TransitionInvalid(from, command));
        }

        if (command == PublishCommand.Publish)
        {
            if (PublishRequirementError(instructor, portrait) is { } requirementError)
            {
                return OperationResult.Failure<InstructorDetail>(requirementError);
            }

            instructor.PublishedAt ??= clock.UtcNow;
            instructor.ScheduledFor = null;
        }
        else if (command == PublishCommand.Archive)
        {
            // An archived instructor is never published by the scheduled publisher, so a time left on it would only
            // mislead the staff reading it.
            instructor.ScheduledFor = null;
        }

        instructor.Status = to;

        return await dataLayer.TransitionAsync(instructor, command, from, actorUserId, cancellationToken) switch
        {
            InstructorWriteOutcome.Saved => OperationResult.Success(ToServiceModel(instructor)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<InstructorDetail>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        InstructorPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var instructor = await dataLayer.FindAsync(id, cancellationToken);
        if (instructor is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(instructor.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<InstructorDetail>(versionError);
        }

        var from = instructor.Status;
        if (PublishLifecycle.Next(from, PublishCommand.Schedule) is not { } to)
        {
            return OperationResult.Failure<InstructorDetail>(TransitionInvalid(from, PublishCommand.Schedule));
        }

        // A time already past would be published by the next pass, a minute later, which is not what "schedule"
        // means: staff who want it out now have the publish command for that.
        if (scheduledFor <= clock.UtcNow)
        {
            return OperationResult.Failure<InstructorDetail>(OperationError.Validation(
                "Choose a time in the future.",
                [("scheduledFor", "This time has passed. Publish the instructor now instead, or pick a later time.")]));
        }

        // Checked now as well as at publish, so staff hear about a missing portrait while they are looking at the
        // screen rather than at midnight, when only the audit trail is watching (ADR 0036).
        if (PublishRequirementError(instructor, portrait) is { } requirementError)
        {
            return OperationResult.Failure<InstructorDetail>(requirementError);
        }

        instructor.ScheduledFor = scheduledFor;
        instructor.Status = to;

        return await dataLayer.TransitionAsync(instructor, PublishCommand.Schedule, from, actorUserId, cancellationToken) switch
        {
            InstructorWriteOutcome.Saved => OperationResult.Success(ToServiceModel(instructor)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<InstructorDetail>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var instructor = await dataLayer.FindAsync(id, cancellationToken);
        if (instructor is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(instructor.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<InstructorDetail>(versionError);
        }

        var from = instructor.Status;
        if (PublishLifecycle.Next(from, PublishCommand.Unschedule) is not { } to)
        {
            return OperationResult.Failure<InstructorDetail>(TransitionInvalid(from, PublishCommand.Unschedule));
        }

        instructor.ScheduledFor = null;
        instructor.Status = to;

        return await dataLayer.TransitionAsync(instructor, PublishCommand.Unschedule, from, actorUserId, cancellationToken) switch
        {
            InstructorWriteOutcome.Saved => OperationResult.Success(ToServiceModel(instructor)),
            _ => Conflict(),
        };
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        dataLayer.ListDueAsync(asOf, take, cancellationToken);

    public async Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, InstructorPortraitCheck? portrait, CancellationToken cancellationToken)
    {
        var instructor = await dataLayer.FindAsync(id, cancellationToken);

        // Read again inside this scope, because the due list is a snapshot: staff may have published, rescheduled
        // or archived the instructor since, and another Worker may have taken it.
        if (instructor is null || instructor.Status != PublishStatus.Scheduled || instructor.ScheduledFor is null || instructor.ScheduledFor > asOf)
        {
            return ScheduledPublishOutcome.Skipped;
        }

        // The portrait the facade asked Media about is no longer this instructor's: whoever changed it is mid-edit, so
        // this pass leaves the instructor to them and the next one asks again.
        if (instructor.PortraitMediaId is not null && portrait?.PortraitMediaId != instructor.PortraitMediaId)
        {
            return ScheduledPublishOutcome.Lost;
        }

        var from = instructor.Status;
        if (PublishRequirementError(instructor, portrait) is { } requirementError)
        {
            // Back to Draft rather than left Scheduled: a record whose time has passed would otherwise be retried
            // every minute for ever, and nobody would be told (ADR 0036).
            instructor.ScheduledFor = null;
            instructor.Status = PublishStatus.Draft;

            return await dataLayer.AbandonScheduleAsync(instructor, from, requirementError.Message, cancellationToken) switch
            {
                InstructorWriteOutcome.Saved => ScheduledPublishOutcome.Unscheduled,
                _ => ScheduledPublishOutcome.Lost,
            };
        }

        instructor.PublishedAt ??= clock.UtcNow;
        instructor.ScheduledFor = null;
        instructor.Status = PublishStatus.Published;

        return await dataLayer.TransitionAsync(
            instructor, PublishCommand.Publish, from, ScheduledPublishPolicy.Actor, cancellationToken) switch
        {
            InstructorWriteOutcome.Saved => ScheduledPublishOutcome.Published,

            // The row version moved under this save: another Worker published it, or staff changed it. Either way
            // it is theirs now.
            _ => ScheduledPublishOutcome.Lost,
        };
    }

    private static OperationError TransitionInvalid(PublishStatus from, PublishCommand command) => OperationError.Of(
        ContentErrorCodes.StatusTransitionInvalid,
        $"This instructor is {from.ToString().ToLowerInvariant()}, so “{command.Verb()}” doesn't apply. Reload it to see its current status.");

    /// <summary>
    /// content.md: what the site needs is checked at publish, not at draft. The name and slug are guaranteed by
    /// every write; the portrait is the one thing a draft may leave unfinished.
    /// </summary>
    private static OperationError? PublishRequirementError(Instructor instructor, InstructorPortraitCheck? portrait)
    {
        if (instructor.PortraitMediaId is null)
        {
            return null;
        }

        return PortraitError(
            instructor.PortraitMediaId.Value, portrait, "The portrait needs alt text before this instructor can be published.");
    }

    /// <summary>
    /// Whether Media's answer clears <paramref name="portraitId"/> for published content. The facade asked about a
    /// particular image; an answer about any other one counts as no answer — a conflict, since the row version moved
    /// too and the If-Match check would normally have refused first.
    /// </summary>
    private static OperationError? PortraitError(Guid portraitId, InstructorPortraitCheck? portrait, string altTextMessage)
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
        IReadOnlyList<InstructorSeed> roster, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roster);

        var now = clock.UtcNow;
        var taken = await dataLayer.GetSlugsAsync(cancellationToken);
        var instructors = roster
            .Select(entry => (Entry: entry, Slug: Slugs.FromName(entry.Name)))
            .Where(pair => taken.Add(pair.Slug))
            .Select(pair => new Instructor
            {
                Id = Guid.CreateVersion7(now),
                Name = pair.Entry.Name,
                Slug = pair.Slug,
                Medium = pair.Entry.Medium,
                Bio = CleanBio(pair.Entry.Bio),
                ArtistId = pair.Entry.ArtistId,
                Active = true,
                Status = PublishStatus.Published,
                PublishedAt = now,
            })
            .ToList();

        return instructors.Count == 0 ? 0 : await dataLayer.AddSeedAsync(instructors, actor, cancellationToken);
    }

    /// <summary>The CMS model with the artist left for the facade to describe, and the id it needs to.</summary>
    private static InstructorDetail ToServiceModel(Instructor instructor) => new(new InstructorServiceModel(
        instructor.Id,
        instructor.Name,
        instructor.Slug,
        instructor.Medium,
        instructor.Bio,
        instructor.Active,
        instructor.PortraitMediaId,
        instructor.Website,
        instructor.Instagram,
        Artist: null,
        instructor.Status,
        instructor.PublishedAt,
        instructor.ScheduledFor,
        SlugLocked: instructor.PublishedAt is not null,
        instructor.LegacyWordPressId,
        instructor.CreatedAt,
        instructor.UpdatedAt,
        instructor.UpdatedBy,
        EntityTag.Format(instructor.RowVersion)), instructor.ArtistId);

    /// <summary>Trimmed, and <c>null</c> when nothing is left: an empty string clears the field.</summary>
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>As <see cref="Clean"/>, with line endings made <c>\n</c> so paragraphs compare and render alike.</summary>
    private static string? CleanBio(string? text) =>
        Clean(text?.ReplaceLineEndings("\n"));

    /// <summary>As <see cref="Clean"/>, in the one spelling a handle is kept in (<see cref="InstructorPolicy.NormalizeInstagram"/>).</summary>
    private static string? CleanInstagram(string? text) =>
        Clean(text) is { } handle ? InstructorPolicy.NormalizeInstagram(handle) : null;

    private static OperationResult<InstructorDetail> NotFound() =>
        OperationResult.Failure<InstructorDetail>(OperationError.Of(ErrorCodes.NotFound, "There is no such instructor."));

    private static OperationResult<InstructorDetail> Conflict() =>
        OperationResult.Failure<InstructorDetail>(EntityTag.Conflict(What));

    private static OperationError SlugTaken(string slug) => new(
        ContentErrorCodes.SlugTaken,
        "Another instructor already has this web address.",
        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slug"] = [$"“{slug}” is taken. Choose another."] });

}
