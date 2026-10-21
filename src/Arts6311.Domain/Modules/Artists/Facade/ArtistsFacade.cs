using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Artists.Business;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Artists.Facade;

/// <summary>
/// Validates shape, asks Media about the portrait, then calls Business. Nothing is cached here: the CMS reads are one
/// indexed row or one page, and the site's artist reads are cached by the Site module, which composes them with the
/// shop's work (ADR 0042).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The portrait is Media's to vouch for, so the facade asks it</strong> — cross-module traffic is facade to
/// facade (backend.md), and Business calls only its DataLayer. On create and edit a portrait must exist in the
/// library; alt text may follow. On publish it must also have alt text (media.md).
/// </para>
/// <para>
/// <strong>Publish calls Business twice</strong>: once to learn which portrait the artist has, then to publish with
/// Media's answer about it. That is safe because the publish is checked against the editor's <c>If-Match</c>: if
/// the portrait changed in between, so did the row version, and the publish is refused as a conflict (ADR 0031).
/// </para>
/// </remarks>
internal sealed class ArtistsFacade(
    IArtistsBusiness business,
    IMediaFacade media,
    IValidator<ArtistCreateViewModel> createValidator,
    IValidator<ArtistUpdateViewModel> updateValidator,
    IValidator<ArtistListViewModel> listValidator,
    IValidator<ContentScheduleViewModel> scheduleValidator) : IArtistsFacade
{
    private const string What = "this artist";

    public async Task<OperationResult<CursorPage<ArtistSummaryServiceModel>>> ListAsync(
        ArtistListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<ArtistSummaryServiceModel>>(error);
        }

        return await business.ListAsync(request, cancellationToken);
    }

    public Task<OperationResult<ArtistServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        business.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<ArtistSummaryServiceModel>> GetSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        // Another module's request, not a person's: one that is too large is a bug in the caller, not a refusal.
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ids.Count, ArtistPolicy.LookupMaxCount, nameof(ids));
        return business.GetSummariesAsync(ids, cancellationToken);
    }

    public Task<IReadOnlyList<ArtistSummaryServiceModel>> FindBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slugs);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slugs.Count, ArtistPolicy.LookupMaxCount, nameof(slugs));
        return business.FindBySlugsAsync(slugs, cancellationToken);
    }

    public async Task<IReadOnlyList<SiteArtistServiceModel>> ListForSiteAsync(CancellationToken cancellationToken)
    {
        var rows = await business.ListForSiteAsync(cancellationToken);

        // One question for the whole roster: which portraits have renditions, and where they are.
        var portraits = await media.DescribeForSiteAsync(
            [.. rows.Select(row => row.PortraitMediaId).OfType<Guid>()], cancellationToken);

        return [.. rows.Select(row => row.ToServiceModel(Portrait(row, portraits)))];
    }

    public async Task<SiteArtistServiceModel?> GetForSiteAsync(string slug, CancellationToken cancellationToken)
    {
        if (!Slugs.IsValid(slug) || await business.GetForSiteAsync(slug, cancellationToken) is not { } row)
        {
            return null;
        }

        return row.ToServiceModel(Portrait(row, await DescribePortraitAsync(row, cancellationToken)));
    }

    public async Task<OperationResult<PreviewRecord<SiteArtistServiceModel>>> GetForPreviewAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var found = await business.GetForPreviewAsync(id, cancellationToken);
        if (!found.Succeeded)
        {
            return OperationResult.Failure<PreviewRecord<SiteArtistServiceModel>>(found.Error!);
        }

        var row = found.Value!.Record;
        return OperationResult.Success(new PreviewRecord<SiteArtistServiceModel>(
            row.ToServiceModel(Portrait(row, await DescribePortraitAsync(row, cancellationToken))), found.Value.Status));
    }

    public Task<IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel>> DescribeCreditsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return business.DescribeCreditsAsync(ids, cancellationToken);
    }

    public Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) =>
        business.ListSitemapAsync(cancellationToken);

    private Task<IReadOnlyDictionary<Guid, SiteImageServiceModel>> DescribePortraitAsync(
        ArtistSiteRow row, CancellationToken cancellationToken) =>
        media.DescribeForSiteAsync(row.PortraitMediaId is { } id ? [id] : [], cancellationToken);

    private static SiteImageServiceModel? Portrait(ArtistSiteRow row, IReadOnlyDictionary<Guid, SiteImageServiceModel> portraits) =>
        row.PortraitMediaId is { } id && portraits.TryGetValue(id, out var portrait) ? portrait : null;

    public async Task<OperationResult<ArtistServiceModel>> CreateAsync(
        ArtistCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        if (await createValidator.ValidateOrErrorAsync(create, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ArtistServiceModel>(error);
        }

        // A new artist is a Draft, so its portrait need only exist; alt text can follow before publishing.
        if (create.PortraitMediaId is { } portraitId && !(await AskMediaAsync(portraitId, cancellationToken)).Exists)
        {
            return OperationResult.Failure<ArtistServiceModel>(PortraitMissing());
        }

        return await business.CreateAsync(create, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<ArtistServiceModel>> UpdateAsync(
        Guid id, ArtistUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ArtistServiceModel>(preconditionError);
        }

        if (await updateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ArtistServiceModel>(error);
        }

        // Business decides whether the answer matters: a published artist's new portrait needs alt text too.
        ArtistPortraitCheck? portrait = null;
        if (update.PortraitMediaId is { } portraitId)
        {
            portrait = await AskMediaAsync(portraitId, cancellationToken);
            if (!portrait.Exists)
            {
                return OperationResult.Failure<ArtistServiceModel>(PortraitMissing());
            }
        }

        return await business.UpdateAsync(id, update, ifMatch, portrait, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<ArtistServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        // A request that can never succeed is refused before Media is asked anything.
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ArtistServiceModel>(preconditionError);
        }

        var portrait = await PortraitOfAsync(id, cancellationToken);
        if (!portrait.Succeeded)
        {
            return OperationResult.Failure<ArtistServiceModel>(portrait.Error!);
        }

        return await business.TransitionAsync(
            id, PublishCommand.Publish, ifMatch, portrait.Value, actorUserId, cancellationToken);
    }

    public Task<OperationResult<ArtistServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        business.TransitionAsync(id, PublishCommand.Unpublish, ifMatch, portrait: null, actorUserId, cancellationToken);

    public async Task<OperationResult<ArtistServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ArtistServiceModel>(preconditionError);
        }

        if (await scheduleValidator.ValidateOrErrorAsync(schedule, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ArtistServiceModel>(error);
        }

        // Scheduling is a publish with a delay, so the portrait is vouched for now as well: the same two Business
        // calls, safe for the same reason — the If-Match covers anything that changed in between (ADR 0031).
        var portrait = await PortraitOfAsync(id, cancellationToken);
        if (!portrait.Succeeded)
        {
            return OperationResult.Failure<ArtistServiceModel>(portrait.Error!);
        }

        return await business.ScheduleAsync(
            id, schedule.ScheduledFor, ifMatch, portrait.Value, actorUserId, cancellationToken);
    }

    public Task<OperationResult<ArtistServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        business.UnscheduleAsync(id, ifMatch, actorUserId, cancellationToken);

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
                $"Reading artist {id} for its scheduled publish failed ({portrait.Error.Code}).");
    }

    /// <summary>
    /// What Media says about the artist's current portrait, or <see cref="ArtistPortraitCheck.None"/> when there
    /// is none. Business checks the answer is about the portrait the artist still has.
    /// </summary>
    private async Task<OperationResult<ArtistPortraitCheck>> PortraitOfAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await business.GetAsync(id, cancellationToken);
        if (!current.Succeeded)
        {
            return OperationResult.Failure<ArtistPortraitCheck>(current.Error!);
        }

        return OperationResult.Success(current.Value!.PortraitMediaId is { } portraitId
            ? await AskMediaAsync(portraitId, cancellationToken)
            : ArtistPortraitCheck.None);
    }

    public Task<OperationResult<ArtistServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        business.TransitionAsync(id, PublishCommand.Archive, ifMatch, portrait: null, actorUserId, cancellationToken);

    /// <summary>What Media says about one image: whether it is an original in the library, and whether it has alt text.</summary>
    private async Task<ArtistPortraitCheck> AskMediaAsync(Guid portraitId, CancellationToken cancellationToken)
    {
        var answer = await media.CheckAttachableAsync([portraitId], cancellationToken);
        return new ArtistPortraitCheck(
            portraitId, Exists: !answer.Missing.Contains(portraitId), HasAltText: !answer.MissingAltText.Contains(portraitId));
    }

    private static OperationError PortraitMissing() => OperationError.Validation(
        "The portrait is not in the media library.",
        [("portraitMediaId", "Choose an image from the media library.")]);
}
