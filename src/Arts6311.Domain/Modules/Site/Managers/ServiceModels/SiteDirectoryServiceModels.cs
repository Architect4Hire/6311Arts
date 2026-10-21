using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Site.Managers.ServiceModels;

/// <summary>One published artist in the site's directory (ADR 0042), with how much of their work is in the shop.</summary>
/// <param name="Active">Whether they currently show at the gallery, so the directory can group them.</param>
/// <param name="Portrait">The portrait's renditions, or <c>null</c> when there is none yet.</param>
/// <param name="WorkCount">How many published pieces in the shop credit them; the spike's <c>productCount</c>.</param>
public sealed record SiteArtistSummaryServiceModel(
    Guid Id,
    string Slug,
    string Name,
    string? Medium,
    bool Active,
    SiteImageServiceModel? Portrait,
    int WorkCount);

/// <summary>
/// One published artist's page (ADR 0042): the artist, and the first page of their work in the shop. The rest of it
/// continues at <c>/api/v1/site/shop?artist={slug}&amp;cursor={works.nextCursor}</c>, which is the same list.
/// </summary>
public sealed record SiteArtistPageServiceModel(
    SiteArtistServiceModel Artist,
    CursorPage<SiteShopItemSummaryServiceModel> Works);
