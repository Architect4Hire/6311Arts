using Arts6311.Domain.Managers.Caching;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Business;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Exhibitions.Facade;

/// <summary>
/// Validates shape, asks Media about the hero and Artists about the credits, then calls Business, and finishes each
/// answer with the credited artists as Artists describes them. The CMS reads are never cached; the site's are,
/// under the <c>exhibition</c> and <c>artist</c> type tags, so a renamed artist's credit is cleared too (ADR 0042).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Other modules are asked here, never in Business</strong> — cross-module traffic is facade to facade
/// (backend.md). Media vouches for the hero, as it does for an artist's portrait (ADR 0031). Artists vouches for the
/// credits: on create and edit every id must be an artist on the roster and not archived, which does not depend on
/// the exhibition, so the facade refuses it outright. Publish does not ask again (ADR 0033).
/// </para>
/// <para>
/// <strong>Publish calls Business twice</strong>: once to learn which hero the exhibition has, then to publish with
/// Media's answer about it. The publish is checked against the editor's <c>If-Match</c>, so a hero changed in
/// between moves the row version and the publish is refused as a conflict (ADR 0031).
/// </para>
/// </remarks>
internal sealed class ExhibitionsFacade(
    IExhibitionsBusiness business,
    IMediaFacade media,
    IArtistsFacade artists,
    IValidator<ExhibitionCreateViewModel> createValidator,
    IValidator<ExhibitionUpdateViewModel> updateValidator,
    IValidator<ExhibitionListViewModel> listValidator,
    IValidator<ContentScheduleViewModel> scheduleValidator,
    IAppCache cache) : IExhibitionsFacade
{
    private const string What = "this exhibition";

    internal static readonly string SiteListKey = CacheKeys.List(ExhibitionAudit.ResourceType, "site");

    /// <summary>A site read shows exhibitions and credits artists, so a change to either type clears it (ADR 0042).</summary>
    internal static readonly string[] SiteTags =
        [CacheTags.Type(ExhibitionAudit.ResourceType), CacheTags.Type(SiteContentTypes.Artist)];

    public async Task<OperationResult<CursorPage<ExhibitionSummaryServiceModel>>> ListAsync(
        ExhibitionListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<ExhibitionSummaryServiceModel>>(error);
        }

        // The list carries a count of credits, not names, so it asks Artists nothing.
        return await business.ListAsync(request, cancellationToken);
    }

    public async Task<OperationResult<ExhibitionServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await CompleteAsync(await business.GetAsync(id, cancellationToken), cancellationToken);

    public async Task<OperationResult<ExhibitionServiceModel>> CreateAsync(
        ExhibitionCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        if (await createValidator.ValidateOrErrorAsync(create, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(error);
        }

        // A new exhibition is a Draft, so its hero need only exist; alt text can follow before publishing.
        if (create.HeroMediaId is { } heroId && !(await AskMediaAsync(heroId, cancellationToken)).Exists)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(HeroMissing());
        }

        if (await CheckArtistsAsync(create.ArtistIds, cancellationToken) is { } artistError)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(artistError);
        }

        return await CompleteAsync(await business.CreateAsync(create, actorUserId, cancellationToken), cancellationToken);
    }

    public async Task<OperationResult<ExhibitionServiceModel>> UpdateAsync(
        Guid id, ExhibitionUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(preconditionError);
        }

        if (await updateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(error);
        }

        // Business decides whether the answer matters: a published exhibition's new hero needs alt text too.
        ExhibitionHeroCheck? hero = null;
        if (update.HeroMediaId is { } heroId)
        {
            hero = await AskMediaAsync(heroId, cancellationToken);
            if (!hero.Exists)
            {
                return OperationResult.Failure<ExhibitionServiceModel>(HeroMissing());
            }
        }

        if (await CheckArtistsAsync(update.ArtistIds, cancellationToken) is { } artistError)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(artistError);
        }

        return await CompleteAsync(
            await business.UpdateAsync(id, update, ifMatch, hero, actorUserId, cancellationToken), cancellationToken);
    }

    public async Task<OperationResult<ExhibitionServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        // A request that can never succeed is refused before Media is asked anything.
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(preconditionError);
        }

        var hero = await HeroOfAsync(id, cancellationToken);
        if (!hero.Succeeded)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(hero.Error!);
        }

        return await CompleteAsync(
            await business.TransitionAsync(id, PublishCommand.Publish, ifMatch, hero.Value, actorUserId, cancellationToken),
            cancellationToken);
    }

    public async Task<OperationResult<ExhibitionServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(preconditionError);
        }

        if (await scheduleValidator.ValidateOrErrorAsync(schedule, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(error);
        }

        // Scheduling is a publish with a delay, so the hero is vouched for now as well: the same two Business
        // calls, safe for the same reason — the If-Match covers anything that changed in between (ADR 0031).
        var hero = await HeroOfAsync(id, cancellationToken);
        if (!hero.Succeeded)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(hero.Error!);
        }

        return await CompleteAsync(
            await business.ScheduleAsync(id, schedule.ScheduledFor, ifMatch, hero.Value, actorUserId, cancellationToken),
            cancellationToken);
    }

    public async Task<OperationResult<ExhibitionServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(preconditionError);
        }

        return await CompleteAsync(
            await business.UnscheduleAsync(id, ifMatch, actorUserId, cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        return business.ListDueAsync(asOf, take, cancellationToken);
    }

    public async Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var hero = await HeroOfAsync(id, cancellationToken);
        if (hero.Succeeded)
        {
            return await business.PublishScheduledAsync(id, asOf, hero.Value, cancellationToken);
        }

        // Gone since the due list was read: nothing for this pass to do. Anything else — Media unreachable, say —
        // is a failure the pass logs and counts, not a quiet "nothing due" that would repeat for ever (ADR 0036).
        return hero.Error!.Code == ErrorCodes.NotFound
            ? ScheduledPublishOutcome.Skipped
            : throw new InvalidOperationException(
                $"Reading exhibition {id} for its scheduled publish failed ({hero.Error.Code}).");
    }

    public async Task<OperationResult<IReadOnlyList<SiteExhibitionSummaryServiceModel>>> ListForSiteAsync(
        CancellationToken cancellationToken) =>
        OperationResult.Success(await cache.GetOrCreateAsync<IReadOnlyList<SiteExhibitionSummaryServiceModel>>(
            SiteListKey,
            async token =>
            {
                var rows = await business.ListForSiteAsync(token);
                var (credits, images) = await DescribeAsync(rows, token);
                return [.. rows.Select(row => row.ToSummary(credits, images))];
            },
            SiteTags,
            SiteCachePolicy.Lifetime,
            cancellationToken));

    public async Task<OperationResult<SiteExhibitionServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken)
    {
        // A malformed slug can't name an exhibition, and must not become a cache key (CacheKeys refuses a ':').
        if (!Slugs.IsValid(slug))
        {
            return SiteNotFound();
        }

        var exhibition = await cache.GetOrCreateFoundAsync(
            CacheKeys.Item(ExhibitionAudit.ResourceType, slug),
            async token =>
            {
                if (await business.GetForSiteAsync(slug, token) is not { } row)
                {
                    return null;
                }

                var (credits, images) = await DescribeAsync([row], token);
                return row.ToServiceModel(credits, images);
            },
            SiteTags,
            SiteCachePolicy.Lifetime,
            cancellationToken);

        return exhibition is null ? SiteNotFound() : OperationResult.Success(exhibition);
    }

    public async Task<OperationResult<PreviewRecord<SiteExhibitionServiceModel>>> GetForPreviewAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var found = await business.GetForPreviewAsync(id, cancellationToken);
        if (!found.Succeeded)
        {
            return OperationResult.Failure<PreviewRecord<SiteExhibitionServiceModel>>(found.Error!);
        }

        var row = found.Value!.Record;
        var (credits, images) = await DescribeAsync([row], cancellationToken);
        return OperationResult.Success(
            new PreviewRecord<SiteExhibitionServiceModel>(row.ToServiceModel(credits, images), found.Value.Status));
    }

    public Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) =>
        business.ListSitemapAsync(cancellationToken);

    /// <summary>One question each to Artists and Media for every row at once: who is credited, and what the heroes are.</summary>
    private async Task<(IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel> Credits, IReadOnlyDictionary<Guid, SiteImageServiceModel> Images)>
        DescribeAsync(IReadOnlyList<ExhibitionSiteRow> rows, CancellationToken cancellationToken)
    {
        var credits = await artists.DescribeCreditsAsync([.. rows.SelectMany(row => row.ArtistIds)], cancellationToken);
        var images = await media.DescribeForSiteAsync([.. rows.Select(row => row.HeroMediaId).OfType<Guid>()], cancellationToken);
        return (credits, images);
    }

    private static OperationResult<SiteExhibitionServiceModel> SiteNotFound() =>
        OperationResult.Failure<SiteExhibitionServiceModel>(OperationError.Of(ErrorCodes.NotFound, "There is no such exhibition."));

    /// <summary>
    /// What Media says about the exhibition's current hero, or <see cref="ExhibitionHeroCheck.None"/> when there is
    /// none. Business checks the answer is about the hero the exhibition still has.
    /// </summary>
    private async Task<OperationResult<ExhibitionHeroCheck>> HeroOfAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await business.GetAsync(id, cancellationToken);
        if (!current.Succeeded)
        {
            return OperationResult.Failure<ExhibitionHeroCheck>(current.Error!);
        }

        return OperationResult.Success(current.Value!.HeroMediaId is { } heroId
            ? await AskMediaAsync(heroId, cancellationToken)
            : ExhibitionHeroCheck.None);
    }

    public Task<OperationResult<ExhibitionServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Unpublish, ifMatch, actorUserId, cancellationToken);

    public Task<OperationResult<ExhibitionServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Archive, ifMatch, actorUserId, cancellationToken);

    /// <summary>A command that asks no other module. Without If-Match it is a 428 before anything is read.</summary>
    private async Task<OperationResult<ExhibitionServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(preconditionError);
        }

        return await CompleteAsync(
            await business.TransitionAsync(id, command, ifMatch, hero: null, actorUserId, cancellationToken),
            cancellationToken);
    }

    /// <summary>Business's answer, finished with each credited artist as the Artists module describes them now.</summary>
    private async Task<OperationResult<ExhibitionServiceModel>> CompleteAsync(
        OperationResult<ExhibitionDetail> result, CancellationToken cancellationToken)
    {
        if (!result.Succeeded)
        {
            return OperationResult.Failure<ExhibitionServiceModel>(result.Error!);
        }

        var detail = result.Value!;
        IReadOnlyList<ArtistSummaryServiceModel> credits = detail.ArtistIds.Count == 0
            ? []
            : await artists.GetSummariesAsync(detail.ArtistIds, cancellationToken);

        return OperationResult.Success(detail.ToServiceModel(credits.ToDictionary(
            artist => artist.Id,
            artist => new ExhibitionArtistServiceModel(artist.Id, artist.Name, artist.Slug, artist.Status))));
    }

    /// <summary>
    /// Every credited id must be an artist on the roster, and not archived: archive is how an artist is retired, and a
    /// retired artist gains no new credits. Draft artists may be credited; the site decides what it shows (Phase 3).
    /// </summary>
    private async Task<OperationError?> CheckArtistsAsync(IReadOnlyList<Guid>? artistIds, CancellationToken cancellationToken)
    {
        if (artistIds is null || artistIds.Count == 0)
        {
            return null;
        }

        var found = (await artists.GetSummariesAsync(artistIds, cancellationToken)).ToDictionary(artist => artist.Id);

        if (artistIds.Any(id => !found.ContainsKey(id)))
        {
            return OperationError.Validation(
                "An artist in the list is not on the roster.",
                [("artistIds", "One of these artists is no longer on the roster. Reload the list and choose again.")]);
        }

        var archived = artistIds.Select(id => found[id]).Where(artist => artist.Status == PublishStatus.Archived).ToList();
        return archived.Count == 0
            ? null
            : OperationError.Validation(
                "An archived artist can't be credited.",
                archived.Select(artist => ("artistIds", $"{artist.Name} is archived, so can't be credited. Remove them from the list.")));
    }

    /// <summary>What Media says about one image: whether it is an original in the library, and whether it has alt text.</summary>
    private async Task<ExhibitionHeroCheck> AskMediaAsync(Guid heroId, CancellationToken cancellationToken)
    {
        var answer = await media.CheckAttachableAsync([heroId], cancellationToken);
        return new ExhibitionHeroCheck(
            heroId, Exists: !answer.Missing.Contains(heroId), HasAltText: !answer.MissingAltText.Contains(heroId));
    }

    private static OperationError HeroMissing() => OperationError.Validation(
        "The hero image is not in the media library.",
        [("heroMediaId", "Choose an image from the media library.")]);
}
