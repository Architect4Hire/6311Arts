using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
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
/// <paramref name="ClassOffering"/>, <paramref name="ShopItem"/>, <paramref name="MembershipPlan"/> and
/// <paramref name="Instructor"/> is set,
/// named rather than polymorphic so the contract (and its generated TypeScript) stays explicit. Each is the site's
/// own read model, the one its public page renders (ADR 0036, 0042), so a preview carries no staff bookkeeping.
/// </remarks>
/// <param name="EntityType"><c>artist</c>, <c>instructor</c>, <c>exhibition</c>, <c>page</c>, <c>class-offering</c>, <c>shop-item</c> or <c>membership-plan</c>.</param>
/// <param name="Path">The address the record would have on the site, for the preview's own canonical and breadcrumbs.</param>
/// <param name="Status">Where the record is in its lifecycle, so the preview can say "Draft" on the page.</param>
/// <param name="NoIndex">Always <c>true</c>: a preview is never indexed and never enters the sitemap.</param>
public sealed record ContentPreviewServiceModel(
    string EntityType,
    Guid EntityId,
    string Path,
    PublishStatus Status,
    bool NoIndex,
    SiteArtistServiceModel? Artist = null,
    SiteExhibitionServiceModel? Exhibition = null,
    SitePageServiceModel? Page = null,
    SiteClassServiceModel? ClassOffering = null,
    SiteShopItemServiceModel? ShopItem = null,
    SiteMembershipPlanServiceModel? MembershipPlan = null,
    SiteInstructorServiceModel? Instructor = null);
