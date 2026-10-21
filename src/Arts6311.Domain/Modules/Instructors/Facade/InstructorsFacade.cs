using Arts6311.Domain.Managers.Caching;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Business;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Instructors.Facade;

/// <summary>
/// Validates shape, asks Media about the portrait and Artists about the artist link, then calls Business, and finishes
/// each answer with the artist as Artists describes them now. The CMS reads are never cached; the site's are, under the
/// <c>instructor</c> and <c>artist</c> type tags (ADR 0042, 0043).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The portrait is Media's to vouch for and the artist Artists'</strong>, so the facade asks them — cross-module
/// traffic is facade to facade (backend.md), and Business calls only its DataLayer. On create and edit a portrait must
/// exist in the library, and alt text may follow; on publish it must also have alt text (media.md). A linked artist
/// must be on the roster and not archived, as a shop item's credit (ADR 0033).
/// </para>
/// <para>
/// <strong>Publish calls Business twice</strong>: once to learn which portrait the instructor has, then to publish with
/// Media's answer about it. That is safe because the publish is checked against the editor's <c>If-Match</c>: if
/// the portrait changed in between, so did the row version, and the publish is refused as a conflict (ADR 0031).
/// </para>
/// </remarks>
internal sealed class InstructorsFacade(
    IInstructorsBusiness business,
    IMediaFacade media,
    IArtistsFacade artists,
    IValidator<InstructorCreateViewModel> createValidator,
    IValidator<InstructorUpdateViewModel> updateValidator,
    IValidator<InstructorListViewModel> listValidator,
    IValidator<ContentScheduleViewModel> scheduleValidator,
    IAppCache cache) : IInstructorsFacade
{
    private const string What = "this instructor";

    internal static readonly string SiteListKey = CacheKeys.List(SiteContentTypes.Instructor, "site");

    /// <summary>
    /// A site read shows instructors and the page of the artist they are linked to, which exists only while that
    /// artist is published, so a change to either type clears it (ADR 0042).
    /// </summary>
    internal static readonly string[] SiteTags =
        [CacheTags.Type(SiteContentTypes.Instructor), CacheTags.Type(SiteContentTypes.Artist)];

    public async Task<OperationResult<CursorPage<InstructorSummaryServiceModel>>> ListAsync(
        InstructorListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<InstructorSummaryServiceModel>>(error);
        }

        return await business.ListAsync(request, cancellationToken);
    }

    public async Task<OperationResult<InstructorServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await CompleteAsync(await business.GetAsync(id, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<InstructorSummaryServiceModel>> GetSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        // Another module's request, not a person's: one that is too large is a bug in the caller, not a refusal.
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ids.Count, InstructorPolicy.LookupMaxCount, nameof(ids));
        return business.GetSummariesAsync(ids, cancellationToken);
    }

    public Task<IReadOnlyList<InstructorSummaryServiceModel>> FindBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slugs);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slugs.Count, InstructorPolicy.LookupMaxCount, nameof(slugs));
        return business.FindBySlugsAsync(slugs, cancellationToken);
    }

    public async Task<OperationResult<IReadOnlyList<SiteInstructorServiceModel>>> ListForSiteAsync(
        CancellationToken cancellationToken) =>
        OperationResult.Success(await cache.GetOrCreateAsync<IReadOnlyList<SiteInstructorServiceModel>>(
            SiteListKey,
            async token =>
            {
                var rows = await business.ListForSiteAsync(token);

                // One question each to Media and Artists for the whole list.
                var portraits = await media.DescribeForSiteAsync(
                    [.. rows.Select(row => row.PortraitMediaId).OfType<Guid>()], token);
                var credits = await artists.DescribeCreditsAsync([.. rows.Select(row => row.ArtistId).OfType<Guid>()], token);

                return [.. rows.Select(row => row.ToServiceModel(Portrait(row, portraits), Artist(row, credits)))];
            },
            SiteTags,
            SiteCachePolicy.Lifetime,
            cancellationToken));

    public async Task<OperationResult<SiteInstructorServiceModel>> GetForSiteAsync(
        string slug, CancellationToken cancellationToken)
    {
        // A malformed slug can't name an instructor, and must not become a cache key (CacheKeys refuses a ':').
        if (!Slugs.IsValid(slug))
        {
            return SiteNotFound();
        }

        var instructor = await cache.GetOrCreateFoundAsync(
            CacheKeys.Item(SiteContentTypes.Instructor, slug),
            async token => await business.GetForSiteAsync(slug, token) is { } row
                ? await ToSiteModelAsync(row, token)
                : null,
            SiteTags,
            SiteCachePolicy.Lifetime,
            cancellationToken);

        return instructor is null ? SiteNotFound() : OperationResult.Success(instructor);
    }

    public async Task<OperationResult<PreviewRecord<SiteInstructorServiceModel>>> GetForPreviewAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var found = await business.GetForPreviewAsync(id, cancellationToken);
        if (!found.Succeeded)
        {
            return OperationResult.Failure<PreviewRecord<SiteInstructorServiceModel>>(found.Error!);
        }

        return OperationResult.Success(new PreviewRecord<SiteInstructorServiceModel>(
            await ToSiteModelAsync(found.Value!.Record, cancellationToken), found.Value.Status));
    }

    public Task<IReadOnlyDictionary<Guid, SiteInstructorCreditServiceModel>> DescribeCreditsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return business.DescribeCreditsAsync(ids, cancellationToken);
    }

    public Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) =>
        business.ListSitemapAsync(cancellationToken);

    private async Task<SiteInstructorServiceModel> ToSiteModelAsync(InstructorSiteRow row, CancellationToken cancellationToken)
    {
        var portraits = await media.DescribeForSiteAsync(row.PortraitMediaId is { } id ? [id] : [], cancellationToken);
        var credits = await artists.DescribeCreditsAsync(row.ArtistId is { } artistId ? [artistId] : [], cancellationToken);
        return row.ToServiceModel(Portrait(row, portraits), Artist(row, credits));
    }

    private static SiteImageServiceModel? Portrait(
        InstructorSiteRow row, IReadOnlyDictionary<Guid, SiteImageServiceModel> portraits) =>
        row.PortraitMediaId is { } id && portraits.TryGetValue(id, out var portrait) ? portrait : null;

    private static SiteArtistCreditServiceModel? Artist(
        InstructorSiteRow row, IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel> credits) =>
        row.ArtistId is { } id && credits.TryGetValue(id, out var credit) ? credit : null;

    private static OperationResult<SiteInstructorServiceModel> SiteNotFound() =>
        OperationResult.Failure<SiteInstructorServiceModel>(OperationError.Of(ErrorCodes.NotFound, "There is no such instructor."));

    public async Task<OperationResult<InstructorServiceModel>> CreateAsync(
        InstructorCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        if (await createValidator.ValidateOrErrorAsync(create, cancellationToken) is { } error)
        {
            return OperationResult.Failure<InstructorServiceModel>(error);
        }

        if (await CheckArtistAsync(create.ArtistId, cancellationToken) is { } artistError)
        {
            return OperationResult.Failure<InstructorServiceModel>(artistError);
        }

        // A new instructor is a Draft, so its portrait need only exist; alt text can follow before publishing.
        if (create.PortraitMediaId is { } portraitId && !(await AskMediaAsync(portraitId, cancellationToken)).Exists)
        {
            return OperationResult.Failure<InstructorServiceModel>(PortraitMissing());
        }

        return await CompleteAsync(await business.CreateAsync(create, actorUserId, cancellationToken), cancellationToken);
    }

    public async Task<OperationResult<InstructorServiceModel>> UpdateAsync(
        Guid id, InstructorUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<InstructorServiceModel>(preconditionError);
        }

        if (await updateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<InstructorServiceModel>(error);
        }

        // An artist link sent unchanged is not a new link, so it is not refused for an artist archived since
        // (ADR 0033: an archived record gains no *new* links). Only a change is asked about.
        if (update.ArtistId is { } artistId)
        {
            var current = await business.GetAsync(id, cancellationToken);
            if (current.Succeeded
                && current.Value!.ArtistId != artistId
                && await CheckArtistAsync(artistId, cancellationToken) is { } artistError)
            {
                return OperationResult.Failure<InstructorServiceModel>(artistError);
            }
        }

        // Business decides whether the answer matters: a published instructor's new portrait needs alt text too.
        InstructorPortraitCheck? portrait = null;
        if (update.PortraitMediaId is { } portraitId)
        {
            portrait = await AskMediaAsync(portraitId, cancellationToken);
            if (!portrait.Exists)
            {
                return OperationResult.Failure<InstructorServiceModel>(PortraitMissing());
            }
        }

        return await CompleteAsync(
            await business.UpdateAsync(id, update, ifMatch, portrait, actorUserId, cancellationToken), cancellationToken);
    }

    public async Task<OperationResult<InstructorServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        // A request that can never succeed is refused before Media is asked anything.
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<InstructorServiceModel>(preconditionError);
        }

        var portrait = await PortraitOfAsync(id, cancellationToken);
        if (!portrait.Succeeded)
        {
            return OperationResult.Failure<InstructorServiceModel>(portrait.Error!);
        }

        return await CompleteAsync(
            await business.TransitionAsync(id, PublishCommand.Publish, ifMatch, portrait.Value, actorUserId, cancellationToken),
            cancellationToken);
    }

    public async Task<OperationResult<InstructorServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        await CompleteAsync(
            await business.TransitionAsync(id, PublishCommand.Unpublish, ifMatch, portrait: null, actorUserId, cancellationToken),
            cancellationToken);

    public async Task<OperationResult<InstructorServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<InstructorServiceModel>(preconditionError);
        }

        if (await scheduleValidator.ValidateOrErrorAsync(schedule, cancellationToken) is { } error)
        {
            return OperationResult.Failure<InstructorServiceModel>(error);
        }

        // Scheduling is a publish with a delay, so the portrait is vouched for now as well: the same two Business
        // calls, safe for the same reason — the If-Match covers anything that changed in between (ADR 0031).
        var portrait = await PortraitOfAsync(id, cancellationToken);
        if (!portrait.Succeeded)
        {
            return OperationResult.Failure<InstructorServiceModel>(portrait.Error!);
        }

        return await CompleteAsync(
            await business.ScheduleAsync(id, schedule.ScheduledFor, ifMatch, portrait.Value, actorUserId, cancellationToken),
            cancellationToken);
    }

    public async Task<OperationResult<InstructorServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        await CompleteAsync(await business.UnscheduleAsync(id, ifMatch, actorUserId, cancellationToken), cancellationToken);

    public async Task<OperationResult<InstructorServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        await CompleteAsync(
            await business.TransitionAsync(id, PublishCommand.Archive, ifMatch, portrait: null, actorUserId, cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        return business.ListDueAsync(asOf, take, cancellationToken);
    }

    public async Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var portrait = await PortraitOfAsync(id, cancellationToken);
        if (portrait.Succeeded)
        {
            return await business.PublishScheduledAsync(id, asOf, portrait.Value, cancellationToken);
        }

        // Gone since the due list was read: nothing for this pass to do. Anything else — Media unreachable, say —
        // is a failure the pass logs and counts, not a quiet "nothing due" that would repeat for ever (ADR 0036).
        return portrait.Error!.Code == ErrorCodes.NotFound
            ? ScheduledPublishOutcome.Skipped
            : throw new InvalidOperationException(
                $"Reading instructor {id} for its scheduled publish failed ({portrait.Error.Code}).");
    }

    /// <summary>
    /// What Media says about the instructor's current portrait, or <see cref="InstructorPortraitCheck.None"/> when
    /// there is none. Business checks the answer is about the portrait the instructor still has.
    /// </summary>
    private async Task<OperationResult<InstructorPortraitCheck>> PortraitOfAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await business.GetAsync(id, cancellationToken);
        if (!current.Succeeded)
        {
            return OperationResult.Failure<InstructorPortraitCheck>(current.Error!);
        }

        return OperationResult.Success(current.Value!.Model.PortraitMediaId is { } portraitId
            ? await AskMediaAsync(portraitId, cancellationToken)
            : InstructorPortraitCheck.None);
    }

    /// <summary>What Media says about one image: whether it is an original in the library, and whether it has alt text.</summary>
    private async Task<InstructorPortraitCheck> AskMediaAsync(Guid portraitId, CancellationToken cancellationToken)
    {
        var answer = await media.CheckAttachableAsync([portraitId], cancellationToken);
        return new InstructorPortraitCheck(
            portraitId, Exists: !answer.Missing.Contains(portraitId), HasAltText: !answer.MissingAltText.Contains(portraitId));
    }

    private static OperationError PortraitMissing() => OperationError.Validation(
        "The portrait is not in the media library.",
        [("portraitMediaId", "Choose an image from the media library.")]);

    /// <summary>
    /// The link must be to an artist on the roster that is not archived: archive is how an artist is retired (ADR
    /// 0033). Draft artists may be linked; the site links to their page only once it is published.
    /// </summary>
    private async Task<OperationError?> CheckArtistAsync(Guid? artistId, CancellationToken cancellationToken)
    {
        if (artistId is not { } id)
        {
            return null;
        }

        var described = await artists.GetSummariesAsync([id], cancellationToken);
        var found = described.Count == 0 ? null : described[0];
        if (found is null)
        {
            return OperationError.Validation(
                "That artist is not on the roster.",
                [("artistId", "Reload the roster and choose again: this artist is no longer on it.")]);
        }

        return found.Status == PublishStatus.Archived
            ? OperationError.Validation(
                "An archived artist can't be linked.",
                [("artistId", $"{found.Name} is archived, so an instructor can't be linked to them.")])
            : null;
    }

    /// <summary>Business's answer, finished with the linked artist as the Artists module describes them now.</summary>
    private async Task<OperationResult<InstructorServiceModel>> CompleteAsync(
        OperationResult<InstructorDetail> result, CancellationToken cancellationToken)
    {
        if (!result.Succeeded)
        {
            return OperationResult.Failure<InstructorServiceModel>(result.Error!);
        }

        var detail = result.Value!;
        if (detail.ArtistId is not { } artistId)
        {
            return OperationResult.Success(detail.Model);
        }

        var described = await artists.GetSummariesAsync([artistId], cancellationToken);
        var artist = described.Count == 0
            ? null
            : new InstructorArtistServiceModel(described[0].Id, described[0].Name, described[0].Slug, described[0].Status);
        return OperationResult.Success(detail.Model with { Artist = artist });
    }
}
