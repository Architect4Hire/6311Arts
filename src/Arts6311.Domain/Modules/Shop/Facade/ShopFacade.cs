using System.Globalization;
using Arts6311.Domain.Managers.Caching;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Html;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Business;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Shop.Facade;

/// <summary>
/// Validates shape, asks Media about the photographs and the description's pictures and Artists about the credit,
/// then calls Business, and finishes each answer with the artist as Artists describes them now. The CMS reads are never
/// cached; the site's are, under the <c>shop-item</c> and <c>artist</c> type tags, so a renamed artist's credit is
/// cleared too (ADR 0042).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Other modules are asked here, never in Business</strong> — cross-module traffic is facade to facade
/// (backend.md). Media vouches for every image, and Artists for the credit: the id must be an artist on the roster
/// that is not archived, which does not depend on the item, so the facade refuses it outright (ADR 0033).
/// </para>
/// <para>
/// <strong>Publish calls Business twice</strong>: once to learn which photographs the item has, then to publish with
/// Media's answer about them. The publish is checked against the editor's <c>If-Match</c>, so a gallery changed in
/// between moves the row version and the publish is refused as a conflict (ADR 0031).
/// </para>
/// <para>
/// <strong>No Square call happens here</strong> (ADR 0032): the SKU, the starting price and the starting quantity
/// are CMS fields, and the push that creates the Square objects from them arrives in 5.x as an outbox message.
/// </para>
/// </remarks>
internal sealed class ShopFacade(
    IShopBusiness business,
    IMediaFacade media,
    IArtistsFacade artists,
    IHtmlSanitizer sanitizer,
    IValidator<ShopItemCreateViewModel> createValidator,
    IValidator<ShopItemUpdateViewModel> updateValidator,
    IValidator<ShopItemListViewModel> listValidator,
    IValidator<ContentScheduleViewModel> scheduleValidator,
    IValidator<SiteShopListViewModel> siteListValidator,
    IAppCache cache) : IShopFacade
{
    private const string What = "this item";

    /// <summary>A site read shows artwork and credits artists, so a change to either type clears it (ADR 0042).</summary>
    internal static readonly string[] SiteTags =
        [CacheTags.Type(ShopAudit.ResourceType), CacheTags.Type(SiteContentTypes.Artist)];

    public async Task<OperationResult<CursorPage<ShopItemSummaryServiceModel>>> ListAsync(
        ShopItemListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<ShopItemSummaryServiceModel>>(error);
        }

        var page = await business.ListAsync(request, cancellationToken);
        if (!page.Succeeded)
        {
            return OperationResult.Failure<CursorPage<ShopItemSummaryServiceModel>>(page.Error!);
        }

        // One question for the whole page: who the credited artists are, as Artists describes them now.
        var credited = page.Value!.Items
            .Select(row => row.ArtistId)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        var described = credited.Count == 0
            ? []
            : (await artists.GetSummariesAsync(credited, cancellationToken)).ToDictionary(artist => artist.Id, Credit);

        return OperationResult.Success(new CursorPage<ShopItemSummaryServiceModel>(
            [.. page.Value.Items.Select(row => row.ToServiceModel(
                row.ArtistId is { } id && described.TryGetValue(id, out var artist) ? artist : null))],
            page.Value.NextCursor));
    }

    public async Task<OperationResult<ShopItemServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await CompleteAsync(await business.GetAsync(id, cancellationToken), cancellationToken);

    public async Task<OperationResult<ShopItemServiceModel>> CreateAsync(
        ShopItemCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        if (await createValidator.ValidateOrErrorAsync(create, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ShopItemServiceModel>(error);
        }

        if (await CheckArtistAsync(create.ArtistId, cancellationToken) is { } artistError)
        {
            return OperationResult.Failure<ShopItemServiceModel>(artistError);
        }

        var check = await AskMediaAsync(create.ImageMediaIds, create.Description, cancellationToken);
        return await CompleteAsync(
            await business.CreateAsync(create, check, actorUserId, cancellationToken), cancellationToken);
    }

    public async Task<OperationResult<ShopItemServiceModel>> UpdateAsync(
        Guid id, ShopItemUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ShopItemServiceModel>(preconditionError);
        }

        if (await updateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ShopItemServiceModel>(error);
        }

        if (await CheckArtistAsync(update.ArtistId, cancellationToken) is { } artistError)
        {
            return OperationResult.Failure<ShopItemServiceModel>(artistError);
        }

        // Business decides whether the answer matters: a published item's new photograph needs alt text too.
        ShopItemMediaCheck? check = null;
        if (update.ImageMediaIds is not null || update.Description is not null)
        {
            check = await AskMediaAsync(update.ImageMediaIds, update.Description, cancellationToken);
        }

        return await CompleteAsync(
            await business.UpdateAsync(id, update, ifMatch, check, actorUserId, cancellationToken), cancellationToken);
    }

    public async Task<OperationResult<ShopItemServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        // A request that can never succeed is refused before Media is asked anything.
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ShopItemServiceModel>(preconditionError);
        }

        var gallery = await GalleryOfAsync(id, cancellationToken);
        if (!gallery.Succeeded)
        {
            return OperationResult.Failure<ShopItemServiceModel>(gallery.Error!);
        }

        return await CompleteAsync(
            await business.TransitionAsync(id, PublishCommand.Publish, ifMatch, gallery.Value, actorUserId, cancellationToken),
            cancellationToken);
    }

    public Task<OperationResult<ShopItemServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Unpublish, ifMatch, actorUserId, cancellationToken);

    public Task<OperationResult<ShopItemServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Archive, ifMatch, actorUserId, cancellationToken);

    public async Task<OperationResult<ShopItemServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ShopItemServiceModel>(preconditionError);
        }

        if (await scheduleValidator.ValidateOrErrorAsync(schedule, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ShopItemServiceModel>(error);
        }

        // Scheduling is a publish with a delay, so the photographs are vouched for now as well: the same two
        // Business calls, safe for the same reason — the If-Match covers anything that changed in between.
        var gallery = await GalleryOfAsync(id, cancellationToken);
        if (!gallery.Succeeded)
        {
            return OperationResult.Failure<ShopItemServiceModel>(gallery.Error!);
        }

        return await CompleteAsync(
            await business.ScheduleAsync(id, schedule.ScheduledFor, ifMatch, gallery.Value, actorUserId, cancellationToken),
            cancellationToken);
    }

    public async Task<OperationResult<ShopItemServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ShopItemServiceModel>(preconditionError);
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
        var gallery = await GalleryOfAsync(id, cancellationToken);
        if (gallery.Succeeded)
        {
            return await business.PublishScheduledAsync(id, asOf, gallery.Value, cancellationToken);
        }

        // Gone since the due list was read: nothing for this pass to do. Anything else — Media unreachable, say — is
        // a failure the pass logs and counts, not a quiet "nothing due" that would repeat for ever (ADR 0036).
        return gallery.Error!.Code == ErrorCodes.NotFound
            ? ScheduledPublishOutcome.Skipped
            : throw new InvalidOperationException(
                $"Reading shop item {id} for its scheduled publish failed ({gallery.Error.Code}).");
    }

    public async Task<OperationResult<CursorPage<SiteShopItemSummaryServiceModel>>> ListForSiteAsync(
        SiteShopListViewModel request, CancellationToken cancellationToken)
    {
        if (await siteListValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<SiteShopItemSummaryServiceModel>>(error);
        }

        // An artist is filtered by their page's slug, so only a published artist has work to show: a draft artist's
        // slug is an empty shop, the same as one that names nobody, and neither is cached.
        Guid? artistId = null;
        if (request.Artist is { } artistSlug)
        {
            var found = await artists.FindBySlugsAsync([artistSlug], cancellationToken);
            if (found is not [{ Status: PublishStatus.Published } artist])
            {
                return OperationResult.Success(new CursorPage<SiteShopItemSummaryServiceModel>([], null));
            }

            artistId = artist.Id;
        }

        // Only a first page is cached: a later page's key would be the visitor's cursor, which they can vary at will.
        if (request.Cursor is not null)
        {
            return await ReadSitePageAsync(request, artistId, cancellationToken);
        }

        var limit = new PageRequest(null, request.Limit).EffectiveLimit;
        var key = CacheKeys.List(
            ShopAudit.ResourceType,
            string.Create(
                CultureInfo.InvariantCulture,
                $"site|{request.Category ?? "*"}|{request.Artist ?? "*"}|{request.Sort ?? ShopPolicy.SiteSorts[0]}|{limit}"));

        return OperationResult.Success(await cache.GetOrCreateAsync(
            key,
            async token => (await ReadSitePageAsync(request, artistId, token)).Value!,
            SiteTags,
            SiteCachePolicy.Lifetime,
            cancellationToken));
    }

    public async Task<OperationResult<SiteShopItemServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken)
    {
        // A malformed slug can't name a piece, and must not become a cache key (CacheKeys refuses a ':').
        if (!Slugs.IsValid(slug))
        {
            return SiteNotFound();
        }

        var item = await cache.GetOrCreateFoundAsync(
            CacheKeys.Item(ShopAudit.ResourceType, slug),
            async token =>
            {
                if (await business.GetForSiteAsync(slug, token) is not { } row)
                {
                    return null;
                }

                var (credits, images) = await DescribeSiteAsync([row], token);
                return row.ToServiceModel(credits, images);
            },
            SiteTags,
            SiteCachePolicy.Lifetime,
            cancellationToken);

        return item is null ? SiteNotFound() : OperationResult.Success(item);
    }

    public async Task<OperationResult<PreviewRecord<SiteShopItemServiceModel>>> GetForPreviewAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var found = await business.GetForPreviewAsync(id, cancellationToken);
        if (!found.Succeeded)
        {
            return OperationResult.Failure<PreviewRecord<SiteShopItemServiceModel>>(found.Error!);
        }

        var row = found.Value!.Record;
        var (credits, images) = await DescribeSiteAsync([row], cancellationToken);
        return OperationResult.Success(
            new PreviewRecord<SiteShopItemServiceModel>(row.ToServiceModel(credits, images), found.Value.Status));
    }

    public async Task<CursorPage<SiteShopItemSummaryServiceModel>> ListWorksForSiteAsync(
        string artistSlug, CancellationToken cancellationToken)
    {
        // Another module's request with a slug it has already read from Artists, so anything malformed is its bug;
        // an artist with no work is an empty page, never a failure.
        var page = await ListForSiteAsync(new SiteShopListViewModel(Artist: artistSlug), cancellationToken);
        return page.Succeeded
            ? page.Value!
            : throw new ArgumentException($"Not a site list request ({page.Error!.Code}).", nameof(artistSlug));
    }

    public Task<IReadOnlyDictionary<Guid, int>> CountForSiteByArtistAsync(CancellationToken cancellationToken) =>
        business.CountForSiteByArtistAsync(cancellationToken);

    public Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) =>
        business.ListSitemapAsync(cancellationToken);

    private async Task<OperationResult<CursorPage<SiteShopItemSummaryServiceModel>>> ReadSitePageAsync(
        SiteShopListViewModel request, Guid? artistId, CancellationToken cancellationToken)
    {
        var page = await business.ListForSiteAsync(request, artistId, cancellationToken);
        if (!page.Succeeded)
        {
            return OperationResult.Failure<CursorPage<SiteShopItemSummaryServiceModel>>(page.Error!);
        }

        var (credits, images) = await DescribeSiteAsync(page.Value!.Items, cancellationToken);
        return OperationResult.Success(new CursorPage<SiteShopItemSummaryServiceModel>(
            [.. page.Value.Items.Select(row => row.ToSummary(credits, images))], page.Value.NextCursor));
    }

    /// <summary>
    /// One question each to Artists and Media for every row at once. The whole gallery is asked about even for a
    /// card, so a card leads with the same photograph as the piece's page: the first that has renditions.
    /// </summary>
    private async Task<(IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel> Credits, IReadOnlyDictionary<Guid, SiteImageServiceModel> Images)>
        DescribeSiteAsync(IReadOnlyList<ShopItemSiteRow> rows, CancellationToken cancellationToken)
    {
        var credits = await artists.DescribeCreditsAsync([.. rows.Select(row => row.ArtistId).OfType<Guid>()], cancellationToken);
        var images = await media.DescribeForSiteAsync([.. rows.SelectMany(row => row.ImageMediaIds)], cancellationToken);
        return (credits, images);
    }

    private static OperationResult<SiteShopItemServiceModel> SiteNotFound() =>
        OperationResult.Failure<SiteShopItemServiceModel>(OperationError.Of(ErrorCodes.NotFound, "There is no such piece."));

    /// <summary>A command that asks no other module. Without <c>If-Match</c> it is a 428 before anything is read.</summary>
    private async Task<OperationResult<ShopItemServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ShopItemServiceModel>(preconditionError);
        }

        return await CompleteAsync(
            await business.TransitionAsync(id, command, ifMatch, media: null, actorUserId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// What Media says about the item's current photographs, or <see cref="ShopItemMediaCheck.None"/> when it has
    /// none. Business checks the answer covers the gallery the item still has.
    /// </summary>
    private async Task<OperationResult<ShopItemMediaCheck>> GalleryOfAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await business.GetAsync(id, cancellationToken);
        if (!current.Succeeded)
        {
            return OperationResult.Failure<ShopItemMediaCheck>(current.Error!);
        }

        return OperationResult.Success(
            await AskMediaAsync(current.Value!.ImageMediaIds, description: null, cancellationToken));
    }

    /// <summary>
    /// What Media says about the photographs and the description's pictures: one question for both, because
    /// <c>CheckAttachableAsync</c> takes them together. The description is sanitized here only to learn which
    /// pictures it shows; Business sanitizes it again as the single writer of what is stored, and the sanitizer is
    /// idempotent.
    /// </summary>
    private async Task<ShopItemMediaCheck> AskMediaAsync(
        IReadOnlyList<Guid>? gallery, string? description, CancellationToken cancellationToken)
    {
        var photographs = (gallery ?? []).Distinct().ToHashSet();
        var pictures = sanitizer.Sanitize(description).MediaAssetIds.ToHashSet();
        var asked = photographs.Union(pictures).ToList();
        if (asked.Count == 0)
        {
            return ShopItemMediaCheck.None;
        }

        var answer = await media.CheckAttachableAsync(asked, cancellationToken);
        var missing = answer.Missing.ToHashSet();
        var withoutAltText = answer.MissingAltText.ToHashSet();

        return new ShopItemMediaCheck(
            AskedGallery: photographs,
            MissingGallery: photographs.Intersect(missing).ToHashSet(),
            GalleryWithoutAltText: photographs.Intersect(withoutAltText).ToHashSet(),
            AskedDescription: pictures,
            MissingDescription: pictures.Intersect(missing).ToHashSet());
    }

    /// <summary>
    /// The credit must be an artist on the roster that is not archived: archive is how an artist is retired, and a
    /// retired artist gains no new work. Draft artists may be credited; the site decides what it shows (Phase 3).
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
                "An archived artist can't be credited.",
                [("artistId", $"{found.Name} is archived, so new work can't be credited to them.")])
            : null;
    }

    /// <summary>Business's answer, finished with the artist as the Artists module describes them now.</summary>
    private async Task<OperationResult<ShopItemServiceModel>> CompleteAsync(
        OperationResult<ShopItemDetail> result, CancellationToken cancellationToken)
    {
        if (!result.Succeeded)
        {
            return OperationResult.Failure<ShopItemServiceModel>(result.Error!);
        }

        var detail = result.Value!;
        if (detail.ArtistId is not { } artistId)
        {
            return OperationResult.Success(detail.ToServiceModel(null));
        }

        var described = await artists.GetSummariesAsync([artistId], cancellationToken);
        return OperationResult.Success(detail.ToServiceModel(described.Count == 0 ? null : Credit(described[0])));
    }

    private static ShopItemArtistServiceModel Credit(Artists.Managers.ServiceModels.ArtistSummaryServiceModel artist) =>
        new(artist.Id, artist.Name, artist.Slug, artist.Status);
}
