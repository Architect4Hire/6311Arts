using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Site.Facade;
using Arts6311.Domain.Modules.Site.Managers.ServiceModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /api/v1/site/artists</c> and <c>/{slug}</c>: the published roster with work counts, and one artist with the
/// first page of their work (ADR 0042). A public read, so it opts out of authentication explicitly; a roster, so not
/// paged — an artist's further work continues at <c>/site/shop?artist={slug}</c>.
/// </summary>
/// <remarks>
/// Cached in the API (HybridCache, cleared by <c>site.revalidate-requested</c> for an artist or a shop item) and for a
/// minute by anything in front of it (<c>Cache-Control: public</c>).
/// </remarks>
[ApiController]
[Route("api/v1/site/artists")]
[AllowAnonymous]
public sealed class SiteArtistsController(ISiteFacade facade) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SiteArtistSummaryServiceModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.ListArtistsAsync(cancellationToken));

    [HttpGet("{slug}")]
    [ProducesResponseType<SiteArtistPageServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync([FromRoute(Name = "slug")] string slug, CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.GetArtistAsync(slug, cancellationToken));
}
