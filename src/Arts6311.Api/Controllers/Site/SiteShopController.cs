using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Shop.Facade;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /api/v1/site/shop</c> and <c>/{slug}</c>: published artwork, cursor-paged, by category, artist and order
/// (ADR 0042). A public read, so it opts out of authentication explicitly.
/// </summary>
/// <remarks>
/// A first page and a piece's page are cached in the API (HybridCache, cleared by <c>site.revalidate-requested</c>
/// for a shop item or an artist); every page is cached for a minute by anything in front of it
/// (<c>Cache-Control: public</c>). Price and sold-out are Square's record, read-only here; the amount a visitor pays
/// is never taken from this answer (square.md).
/// </remarks>
[ApiController]
[Route("api/v1/site/shop")]
[AllowAnonymous]
public sealed class SiteShopController(IShopFacade facade) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CursorPage<SiteShopItemSummaryServiceModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery(Name = "category")] string? category,
        [FromQuery(Name = "artist")] string? artist,
        [FromQuery(Name = "sort")] string? sort,
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.ListForSiteAsync(
            new SiteShopListViewModel(category, artist, sort, cursor, limit), cancellationToken));

    [HttpGet("{slug}")]
    [ProducesResponseType<SiteShopItemServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync([FromRoute(Name = "slug")] string slug, CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.GetForSiteAsync(slug, cancellationToken));
}
