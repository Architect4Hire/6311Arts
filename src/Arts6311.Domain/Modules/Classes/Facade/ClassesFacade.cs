using Arts6311.Domain.Managers.Caching;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Html;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Classes.Business;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Arts6311.Domain.Modules.Instructors.Facade;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Classes.Facade;

/// <summary>
/// Validates shape, asks Media about the hero and the body's pictures and Instructors about each run's instructor,
/// then calls Business, and finishes each answer with the instructors as Instructors describes them now. The CMS reads
/// are never cached; the site's are, under the <c>class-offering</c> and <c>instructor</c> type tags (ADR 0042, 0044).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Media and Instructors are asked here, never in Business</strong> — cross-module traffic is facade to facade
/// (backend.md). Media vouches for the hero, as it does for an artist's portrait (ADR 0031), and for each picture in
/// the sanitized body, as it does for a page's (ADR 0034). The body is sanitized here only to learn which pictures it
/// shows; Business sanitizes it again as the single writer of what is stored, and the sanitizer is idempotent.
/// Instructors says which of the runs' instructors exist and whether they are archived; Business decides what that
/// means for each run (ADR 0044).
/// </para>
/// <para>
/// <strong>Publish calls Business twice</strong>: once to learn which hero the offering has and which instructors
/// its runs name, then to publish with Media's and Instructors' answers about them. The publish is checked against the editor's <c>If-Match</c>, so a hero changed in
/// between moves the row version and the publish is refused as a conflict (ADR 0031).
/// </para>
/// <para>
/// <strong>No Square call happens here</strong> (ADR 0032): each run's SKU, capacity and starting price are CMS
/// fields, and the push that creates the Square objects from them arrives in 5.x as an outbox message.
/// </para>
/// </remarks>
internal sealed class ClassesFacade(
    IClassesBusiness business,
    IMediaFacade media,
    IInstructorsFacade instructors,
    IHtmlSanitizer sanitizer,
    IValidator<ClassCreateViewModel> createValidator,
    IValidator<ClassUpdateViewModel> updateValidator,
    IValidator<ClassListViewModel> listValidator,
    IValidator<ContentScheduleViewModel> scheduleValidator,
    IAppCache cache) : IClassesFacade
{
    private const string What = "this class";

    internal static readonly string SiteListKey = CacheKeys.List(ClassAudit.ResourceType, "site");

    /// <summary>
    /// Every site read of a class carries the type tags of what it shows: classes, and the instructors their runs name
    /// (ADR 0042, 0044). Any change to either clears them all.
    /// </summary>
    internal static readonly string[] SiteTags =
        [CacheTags.Type(ClassAudit.ResourceType), CacheTags.Type(SiteContentTypes.Instructor)];

    public async Task<OperationResult<CursorPage<ClassSummaryServiceModel>>> ListAsync(
        ClassListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<ClassSummaryServiceModel>>(error);
        }

        var page = await business.ListAsync(request, cancellationToken);
        if (!page.Succeeded)
        {
            return OperationResult.Failure<CursorPage<ClassSummaryServiceModel>>(page.Error!);
        }

        // One question for the whole page: who teaches these classes, as Instructors names them now.
        var ids = page.Value!.Items.SelectMany(row => row.InstructorIds).Distinct().ToList();
        var names = new Dictionary<Guid, string>();
        foreach (var chunk in ids.Chunk(ClassPolicy.ListInstructorsMaxCount))
        {
            foreach (var instructor in await instructors.GetSummariesAsync(chunk, cancellationToken))
            {
                names[instructor.Id] = instructor.Name;
            }
        }

        return OperationResult.Success(new CursorPage<ClassSummaryServiceModel>(
            [.. page.Value.Items.Select(row => row.ToServiceModel(names))], page.Value.NextCursor));
    }

    public async Task<OperationResult<ClassServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await CompleteAsync(await business.GetAsync(id, cancellationToken), cancellationToken);

    public async Task<OperationResult<ClassServiceModel>> CreateAsync(
        ClassCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        if (await createValidator.ValidateOrErrorAsync(create, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ClassServiceModel>(error);
        }

        // A new offering is a Draft, so its hero need only exist; alt text can follow before publishing.
        var check = await AskBodyAsync(create.Body, create.HeroMediaId, cancellationToken);
        if (create.HeroMediaId is not null && !check.HeroExists)
        {
            return OperationResult.Failure<ClassServiceModel>(HeroMissing());
        }

        var named = await AskInstructorsAsync(create.Runs, cancellationToken);
        return await CompleteAsync(
            await business.CreateAsync(create, check, named, actorUserId, cancellationToken), cancellationToken);
    }

    public async Task<OperationResult<ClassServiceModel>> UpdateAsync(
        Guid id, ClassUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ClassServiceModel>(preconditionError);
        }

        if (await updateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ClassServiceModel>(error);
        }

        // Business decides whether the answer matters: a published offering's new hero needs alt text too.
        ClassMediaCheck? check = null;
        if (update.HeroMediaId is not null || update.Body is not null)
        {
            check = await AskBodyAsync(update.Body, update.HeroMediaId, cancellationToken);
            if (update.HeroMediaId is not null && !check.HeroExists)
            {
                return OperationResult.Failure<ClassServiceModel>(HeroMissing());
            }
        }

        var named = update.Runs is null ? null : await AskInstructorsAsync(update.Runs, cancellationToken);
        return await CompleteAsync(
            await business.UpdateAsync(id, update, ifMatch, check, named, actorUserId, cancellationToken), cancellationToken);
    }

    public async Task<OperationResult<ClassServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        // A request that can never succeed is refused before Media is asked anything.
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ClassServiceModel>(preconditionError);
        }

        var checks = await ChecksOfAsync(id, cancellationToken);
        if (!checks.Succeeded)
        {
            return OperationResult.Failure<ClassServiceModel>(checks.Error!);
        }

        return await CompleteAsync(
            await business.TransitionAsync(
                id, PublishCommand.Publish, ifMatch, checks.Value!.Media, checks.Value.Instructors, actorUserId,
                cancellationToken),
            cancellationToken);
    }

    public Task<OperationResult<ClassServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Unpublish, ifMatch, actorUserId, cancellationToken);

    public Task<OperationResult<ClassServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Archive, ifMatch, actorUserId, cancellationToken);

    public async Task<OperationResult<CursorPage<ClassDeletedServiceModel>>> ListDeletedAsync(
        ClassListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<ClassDeletedServiceModel>>(error);
        }

        return await business.ListDeletedAsync(request, cancellationToken);
    }

    /// <remarks>
    /// Nothing is evicted here. A delete that takes a record off the site stages <c>site.revalidate-requested</c>
    /// in its own save, exactly as a status command does, and the Worker's handler clears the tags (ADR 0036,
    /// 0049) — no cache call follows a save (outbox.md).
    /// </remarks>
    public async Task<OperationResult> DeleteAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure(preconditionError);
        }

        return await business.DeleteAsync(id, ifMatch, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<ClassServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ClassServiceModel>(preconditionError);
        }

        return await CompleteAsync(
            await business.RestoreAsync(id, ifMatch, actorUserId, cancellationToken), cancellationToken);
    }

    public async Task<OperationResult<ClassServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ClassServiceModel>(preconditionError);
        }

        if (await scheduleValidator.ValidateOrErrorAsync(schedule, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ClassServiceModel>(error);
        }

        // Scheduling is a publish with a delay, so the hero is vouched for now as well: the same two Business calls,
        // safe for the same reason — the If-Match covers anything that changed in between (ADR 0031).
        var checks = await ChecksOfAsync(id, cancellationToken);
        if (!checks.Succeeded)
        {
            return OperationResult.Failure<ClassServiceModel>(checks.Error!);
        }

        return await CompleteAsync(
            await business.ScheduleAsync(
                id, schedule.ScheduledFor, ifMatch, checks.Value!.Media, checks.Value.Instructors, actorUserId,
                cancellationToken),
            cancellationToken);
    }

    public async Task<OperationResult<ClassServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ClassServiceModel>(preconditionError);
        }

        return await CompleteAsync(await business.UnscheduleAsync(id, ifMatch, actorUserId, cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        return business.ListDueAsync(asOf, take, cancellationToken);
    }

    public async Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var checks = await ChecksOfAsync(id, cancellationToken);
        if (checks.Succeeded)
        {
            return await business.PublishScheduledAsync(
                id, asOf, checks.Value!.Media, checks.Value.Instructors, cancellationToken);
        }

        // Gone since the due list was read: nothing for this pass to do. Anything else — Media unreachable, say — is
        // a failure the pass logs and counts, not a quiet "nothing due" that would repeat for ever (ADR 0036).
        return checks.Error!.Code == ErrorCodes.NotFound
            ? ScheduledPublishOutcome.Skipped
            : throw new InvalidOperationException(
                $"Reading class {id} for its scheduled publish failed ({checks.Error.Code}).");
    }

    public async Task<OperationResult<IReadOnlyList<SiteClassSummaryServiceModel>>> ListForSiteAsync(
        SiteClassListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Instructor is not { } instructorSlug)
        {
            return OperationResult.Success(await ReadSiteListAsync(SiteListKey, instructorId: null, cancellationToken));
        }

        // An instructor is filtered by their page's slug, so only a published instructor has classes to show. Anything
        // else — a draft, a typo, a slug that could never be one — is an empty list, never a failure, and not cached:
        // a cache key per wrong guess would let anyone fill the cache.
        if (!Slugs.IsValid(instructorSlug))
        {
            return OperationResult.Success<IReadOnlyList<SiteClassSummaryServiceModel>>([]);
        }

        var found = await instructors.FindBySlugsAsync([instructorSlug], cancellationToken);
        if (found is not [{ Status: PublishStatus.Published } instructor])
        {
            return OperationResult.Success<IReadOnlyList<SiteClassSummaryServiceModel>>([]);
        }

        return OperationResult.Success(await ReadSiteListAsync(
            CacheKeys.List(ClassAudit.ResourceType, $"site|instructor={instructor.Slug}"), instructor.Id, cancellationToken));
    }

    public async Task<OperationResult<SiteClassServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken)
    {
        // A malformed slug can't name a class, and must not become a cache key (CacheKeys refuses a ':').
        if (!Slugs.IsValid(slug))
        {
            return SiteNotFound();
        }

        var offering = await cache.GetOrCreateFoundAsync(
            CacheKeys.Item(ClassAudit.ResourceType, slug),
            async token => await business.GetForSiteAsync(slug, token) is { } row
                ? await ToSiteModelAsync(row, token)
                : null,
            SiteTags,
            SiteCachePolicy.Lifetime,
            cancellationToken);

        return offering is null ? SiteNotFound() : OperationResult.Success(offering);
    }

    public async Task<OperationResult<PreviewRecord<SiteClassServiceModel>>> GetForPreviewAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var found = await business.GetForPreviewAsync(id, cancellationToken);
        if (!found.Succeeded)
        {
            return OperationResult.Failure<PreviewRecord<SiteClassServiceModel>>(found.Error!);
        }

        return OperationResult.Success(new PreviewRecord<SiteClassServiceModel>(
            await ToSiteModelAsync(found.Value!.Record, cancellationToken), found.Value.Status));
    }

    public Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) =>
        business.ListSitemapAsync(cancellationToken);

    private async Task<IReadOnlyList<SiteClassSummaryServiceModel>> ReadSiteListAsync(
        string key, Guid? instructorId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync<IReadOnlyList<SiteClassSummaryServiceModel>>(
            key,
            async token =>
            {
                var rows = await business.ListForSiteAsync(instructorId, token);

                // One question each to Media and Instructors for the whole list.
                var images = await media.DescribeForSiteAsync([.. rows.Select(row => row.HeroMediaId).OfType<Guid>()], token);
                var named = await instructors.DescribeCreditsAsync(
                    [.. rows.SelectMany(row => row.Runs).Select(run => run.InstructorId).OfType<Guid>()], token);
                return [.. rows.Select(row => row.ToSummary(images, named))];
            },
            SiteTags,
            SiteCachePolicy.Lifetime,
            cancellationToken);

    private async Task<SiteClassServiceModel> ToSiteModelAsync(ClassSiteRow row, CancellationToken cancellationToken)
    {
        var images = await media.DescribeForSiteAsync(row.HeroMediaId is { } hero ? [hero] : [], cancellationToken);
        var named = await instructors.DescribeCreditsAsync(
            [.. row.Runs.Select(run => run.InstructorId).OfType<Guid>()], cancellationToken);
        return row.ToServiceModel(images, named);
    }

    private static OperationResult<SiteClassServiceModel> SiteNotFound() =>
        OperationResult.Failure<SiteClassServiceModel>(OperationError.Of(ErrorCodes.NotFound, "There is no such class."));

    /// <summary>
    /// A command that asks no other module first. Without <c>If-Match</c> it is a 428 before anything is read.
    /// </summary>
    private async Task<OperationResult<ClassServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ClassServiceModel>(preconditionError);
        }

        return await CompleteAsync(
            await business.TransitionAsync(
                id, command, ifMatch, media: null, instructors: null, actorUserId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// What Instructors says about every instructor the runs name: which exist, with their names and statuses. One
    /// question for the whole write; <see cref="ClassPolicy.RunsMaxCount"/> keeps it inside Instructors' lookup limit.
    /// </summary>
    private async Task<ClassInstructorCheck> AskInstructorsAsync(
        IReadOnlyList<ClassRunViewModel>? runs, CancellationToken cancellationToken)
    {
        var ids = (runs ?? []).Select(run => run.InstructorId).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return ClassInstructorCheck.None;
        }

        var found = await instructors.GetSummariesAsync(ids, cancellationToken);
        return new ClassInstructorCheck(found.ToDictionary(
            instructor => instructor.Id, instructor => new ClassInstructorFact(instructor.Name, instructor.Status)));
    }

    /// <summary>Business's answer, finished with each run's instructor as the Instructors module describes them now.</summary>
    private async Task<OperationResult<ClassServiceModel>> CompleteAsync(
        OperationResult<ClassDetail> result, CancellationToken cancellationToken)
    {
        if (!result.Succeeded)
        {
            return OperationResult.Failure<ClassServiceModel>(result.Error!);
        }

        var detail = result.Value!;
        var ids = detail.RunInstructors.Values.Distinct().ToList();
        if (ids.Count == 0)
        {
            return OperationResult.Success(detail.Model);
        }

        var described = (await instructors.GetSummariesAsync(ids, cancellationToken)).ToDictionary(
            instructor => instructor.Id,
            instructor => new ClassInstructorServiceModel(instructor.Id, instructor.Name, instructor.Slug, instructor.Status));
        return OperationResult.Success(detail.Complete(described));
    }

    /// <summary>
    /// What Media says about the offering's current hero and what Instructors says about the instructors its runs
    /// name: the two questions a publish or a schedule has to ask about the record as it stands. Business checks
    /// each answer is about what the offering still holds.
    /// </summary>
    private async Task<OperationResult<ClassPublishChecks>> ChecksOfAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await business.GetAsync(id, cancellationToken);
        if (!current.Succeeded)
        {
            return OperationResult.Failure<ClassPublishChecks>(current.Error!);
        }

        var detail = current.Value!;
        var media = detail.Model.HeroMediaId is { } heroId
            ? await AskHeroCheckAsync(heroId, cancellationToken)
            : ClassMediaCheck.None;

        // An instructor a run names may have been deleted since (ADR 0052), and publishing needs a real one. The
        // ids come from the detail, not the model: the model's Instructor is what this answer fills in.
        var named = detail.RunInstructors.Values.Distinct().ToList();
        var teaching = named.Count == 0
            ? ClassInstructorCheck.None
            : new ClassInstructorCheck((await instructors.GetSummariesAsync(named, cancellationToken))
                .ToDictionary(found => found.Id, found => new ClassInstructorFact(found.Name, found.Status)));

        return OperationResult.Success(new ClassPublishChecks(media, teaching));
    }

    /// <summary>
    /// What Media says about a body's pictures and, when one is sent, the hero: one question for both, because
    /// <c>CheckAttachableAsync</c> takes them together.
    /// </summary>
    private async Task<ClassMediaCheck> AskBodyAsync(string? body, Guid? heroId, CancellationToken cancellationToken)
    {
        var bodyImages = sanitizer.Sanitize(body).MediaAssetIds;
        var asked = heroId is { } hero ? bodyImages.Append(hero).Distinct().ToList() : [.. bodyImages];
        if (asked.Count == 0)
        {
            return ClassMediaCheck.None;
        }

        var answer = await media.CheckAttachableAsync(asked, cancellationToken);
        return new ClassMediaCheck(
            heroId,
            HeroExists: heroId is null || !answer.Missing.Contains(heroId.Value),
            HeroHasAltText: heroId is null || !answer.MissingAltText.Contains(heroId.Value),
            AskedBodyImages: bodyImages.ToHashSet(),
            MissingBodyImages: answer.Missing.ToHashSet());
    }

    /// <summary>What Media says about one hero image, with no body in the question.</summary>
    private async Task<ClassMediaCheck> AskHeroCheckAsync(Guid heroId, CancellationToken cancellationToken)
    {
        var answer = await media.CheckAttachableAsync([heroId], cancellationToken);
        return new ClassMediaCheck(
            heroId,
            HeroExists: !answer.Missing.Contains(heroId),
            HeroHasAltText: !answer.MissingAltText.Contains(heroId),
            AskedBodyImages: new HashSet<Guid>(),
            MissingBodyImages: new HashSet<Guid>());
    }

    private static OperationError HeroMissing() => OperationError.Validation(
        "The hero image is not in the media library.",
        [("heroMediaId", "Choose an image from the media library.")]);
}
