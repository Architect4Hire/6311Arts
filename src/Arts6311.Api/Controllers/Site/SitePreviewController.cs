using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Preview.Facade;
using Arts6311.Domain.Modules.Preview.Managers.ServiceModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /api/v1/site/preview/{token}</c>: the draft a preview link names, for the site to render with
/// <c>noindex</c> (ADR 0036).
/// </summary>
/// <remarks>
/// <para>
/// Anonymous, because the token is the credential: the site's server fetches this while rendering, and the person
/// following the link has no session. The token grants this one read — nothing accepts it as authentication for a
/// write.
/// </para>
/// <para>
/// Never cached, by this host or anything in front of it: a draft changes while it is being previewed, and a
/// shared cache must not keep an unpublished page. <c>X-Robots-Tag</c> says the same thing as the body's
/// <c>noIndex</c>, for anything that fetches this directly.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/site/preview")]
[AllowAnonymous]
public sealed class SitePreviewController(IPreviewFacade facade) : ControllerBase
{
    [HttpGet("{token}")]
    [ProducesResponseType<ContentPreviewServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        [FromRoute(Name = "token")] string token, CancellationToken cancellationToken)
    {
        // Length, shape and expiry are all the facade's: anything unusable is one 404, so a holder cannot tell a
        // too-long string from an expired link (PreviewPolicy.TokenMaxLength, ADR 0036).
        var result = await facade.ReadAsync(token, cancellationToken);
        if (result.Succeeded)
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        }

        return this.ToActionResult(result);
    }
}
