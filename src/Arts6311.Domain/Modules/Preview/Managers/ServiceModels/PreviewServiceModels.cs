using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Preview.Managers.ServiceModels;

/// <summary>A link the CMS hands a staff member so they can see an unpublished record as the site will show it.</summary>
/// <param name="Token">The signed, expiring token. It is the whole credential, so it is never logged or put in a sitemap.</param>
/// <param name="Path">Where to open it on the site, <c>/preview/{token}</c>.</param>
/// <param name="ExpiresAt">When it stops working (UTC).</param>
public sealed record PreviewLinkServiceModel(string Token, string Path, DateTimeOffset ExpiresAt);

/// <summary>
/// One record as a preview shows it: which type it is, where it would live, and the record itself.
/// </summary>
/// <remarks>
/// Exactly one of <paramref name="Artist"/>, <paramref name="Exhibition"/>, <paramref name="Page"/>,
/// <paramref name="ClassOffering"/> and <paramref name="ShopItem"/> is set, named rather than polymorphic so the
/// contract (and its generated TypeScript) stays explicit. These are the CMS ServiceModels: the site's own read models arrive in 3.1 and this
/// shape takes them then (ADR 0036).
/// </remarks>
/// <param name="EntityType"><c>artist</c>, <c>exhibition</c>, <c>page</c>, <c>class-offering</c> or <c>shop-item</c>.</param>
/// <param name="Path">The address the record would have on the site, for the preview's own canonical and breadcrumbs.</param>
/// <param name="Status">Where the record is in its lifecycle, so the preview can say "Draft" on the page.</param>
/// <param name="NoIndex">Always <c>true</c>: a preview is never indexed and never enters the sitemap.</param>
public sealed record ContentPreviewServiceModel(
    string EntityType,
    Guid EntityId,
    string Path,
    PublishStatus Status,
    bool NoIndex,
    ArtistServiceModel? Artist = null,
    ExhibitionServiceModel? Exhibition = null,
    PageServiceModel? Page = null,
    ClassServiceModel? ClassOffering = null,
    ShopItemServiceModel? ShopItem = null);
