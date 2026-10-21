using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Shop.Business;

/// <summary>
/// The shop's rules: slugs unique, a description that is only ever the sanitizer's output, a SKU no other record in
/// the CMS holds (ADR 0037), a one-of-a-kind piece that is always a count of one, and Square's record of money and
/// stock never written from here (square.md, ADR 0032).
/// </summary>
/// <remarks>
/// It returns <see cref="ShopItemDetail"/> rather than the finished ServiceModel: who the credited artist is only
/// the Artists module can say, and only the facade may ask it (ADR 0033).
/// </remarks>
internal interface IShopBusiness
{
    Task<OperationResult<CursorPage<ShopItemListRow>>> ListAsync(
        ShopItemListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<ShopItemDetail>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <param name="media">What Media said about the gallery and about the description's images.</param>
    Task<OperationResult<ShopItemDetail>> CreateAsync(
        ShopItemCreateViewModel create, ShopItemMediaCheck media, string actorUserId, CancellationToken cancellationToken);

    /// <param name="media">What Media said about a gallery or a description sent with this edit.</param>
    Task<OperationResult<ShopItemDetail>> UpdateAsync(
        Guid id,
        ShopItemUpdateViewModel update,
        string? ifMatch,
        ShopItemMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <param name="media">Required for <see cref="PublishCommand.Publish"/>: what Media said about the gallery.</param>
    Task<OperationResult<ShopItemDetail>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ShopItemMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled for <paramref name="scheduledFor"/>. Refusals: <c>concurrency.*</c>,
    /// <c>cms.status_transition_invalid</c>, and <c>validation.failed</c> for a time already past or anything
    /// publishing would refuse (ADR 0036).
    /// </summary>
    Task<OperationResult<ShopItemDetail>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        ShopItemMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<ShopItemDetail>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>The items due to be published at <paramref name="asOf"/>, for the Worker's scheduled publisher.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the site's shop (ADR 0042): published items only, filtered and ordered as asked. Refusal:
    /// <c>paging.cursor.invalid</c> for a cursor from another list or order.
    /// </summary>
    /// <param name="artistId">The artist the facade resolved <c>request.Artist</c> to, when it names one.</param>
    Task<OperationResult<CursorPage<ShopItemSiteRow>>> ListForSiteAsync(
        SiteShopListViewModel request, Guid? artistId, CancellationToken cancellationToken);

    /// <summary>The published item at <paramref name="slug"/>, or <c>null</c>: a draft is not there to the site.</summary>
    Task<ShopItemSiteRow?> GetForSiteAsync(string slug, CancellationToken cancellationToken);

    /// <summary>One item as the site would show it, whatever the status, for a preview link.</summary>
    Task<OperationResult<PreviewRecord<ShopItemSiteRow>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>How many published items each credited artist has, for the site's artist directory.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountForSiteByArtistAsync(CancellationToken cancellationToken);

    /// <summary>Every published item's address and last change, for the sitemap.</summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Publishes one due item on the scheduled publisher's behalf, re-reading it first: the row version is the
    /// claim, so two Workers acting on one record produce one publish and one
    /// <see cref="ScheduledPublishOutcome.Lost"/>.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, ShopItemMediaCheck? media, CancellationToken cancellationToken);

    /// <summary>
    /// The development shop: adds, as Published and without photographs, each item whose slug is not in use yet,
    /// with a suggested SKU. Returns how many were added. Never touches an existing row, so a staff edit survives
    /// a rerun.
    /// </summary>
    Task<int> SeedAsync(IReadOnlyList<ShopItemSeed> seeds, string actor, CancellationToken cancellationToken);
}
