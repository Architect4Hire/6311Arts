using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Site.Facade;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /api/v1/site/sitemap</c>: every published record's address and last change, for the site's server to
/// write <c>/sitemap.xml</c> from (ADR 0042). A public read, so it opts out of authentication explicitly. Drafts,
/// previews and redirects never appear.
/// </summary>
/// <remarks>
/// Cached in the API (HybridCache, cleared by any content type's <c>site.revalidate-requested</c>) and for a minute by
/// anything in front of it (<c>Cache-Control: public</c>).
/// </remarks>
[ApiController]
[Route("api/v1/site/sitemap")]
[AllowAnonymous]
public sealed class SiteSitemapController(ISiteFacade facade) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SitemapEntryServiceModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.ListSitemapAsync(cancellationToken));
}
