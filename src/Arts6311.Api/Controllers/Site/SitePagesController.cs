using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Pages.Facade;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /api/v1/site/pages/{slug}</c>: one published page — privacy, terms and the like (ADR 0034, 0042). A public
/// read, so it opts out of authentication explicitly.
/// </summary>
/// <remarks>
/// Cached in the API (HybridCache, cleared by <c>site.revalidate-requested</c>) and for a minute by anything in front
/// of it (<c>Cache-Control: public</c>).
/// </remarks>
[ApiController]
[Route("api/v1/site/pages")]
[AllowAnonymous]
public sealed class SitePagesController(IPagesFacade facade) : ControllerBase
{
    [HttpGet("{slug}")]
    [ProducesResponseType<SitePageServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync([FromRoute(Name = "slug")] string slug, CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.GetForSiteAsync(slug, cancellationToken));
}
