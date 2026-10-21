using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Exhibitions.Facade;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /api/v1/site/exhibitions</c> and <c>/{slug}</c>: published exhibitions, newest opening first, with their
/// credited artists (ADR 0042). A public read, so it opts out of authentication explicitly; a small list, so not paged.
/// </summary>
/// <remarks>
/// Cached in the API (HybridCache, cleared by <c>site.revalidate-requested</c> for an exhibition or an artist) and for
/// a minute by anything in front of it (<c>Cache-Control: public</c>).
/// </remarks>
[ApiController]
[Route("api/v1/site/exhibitions")]
[AllowAnonymous]
public sealed class SiteExhibitionsController(IExhibitionsFacade facade) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SiteExhibitionSummaryServiceModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.ListForSiteAsync(cancellationToken));

    [HttpGet("{slug}")]
    [ProducesResponseType<SiteExhibitionServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync([FromRoute(Name = "slug")] string slug, CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.GetForSiteAsync(slug, cancellationToken));
}
