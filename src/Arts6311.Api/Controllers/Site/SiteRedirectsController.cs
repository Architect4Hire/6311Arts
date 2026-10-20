using System.Globalization;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Redirects.Facade;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /api/v1/site/redirects</c>: every redirect, for the site's server to apply before it renders (frontend.md
/// "Legacy URL redirects"). A public read, so it opts out of authentication explicitly; a small fixed list, so it
/// returns everything (api-contract.md "Paging").
/// </summary>
/// <remarks>
/// Cached for <see cref="RedirectPolicy.SiteCacheLifetime"/> in the API (HybridCache) and by anyone in front of it
/// (<c>Cache-Control: public</c>). Until 2.8's revalidation, a change shows within that long (ADR 0035).
/// </remarks>
[ApiController]
[Route("api/v1/site/redirects")]
[AllowAnonymous]
public sealed class SiteRedirectsController(IRedirectsFacade facade) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SiteRedirectServiceModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        var result = await facade.ListForSiteAsync(cancellationToken);
        if (result.Succeeded)
        {
            Response.Headers.CacheControl = string.Create(
                CultureInfo.InvariantCulture, $"public, max-age={(int)RedirectPolicy.SiteCacheLifetime.TotalSeconds}");
        }

        return this.ToActionResult(result);
    }
}
