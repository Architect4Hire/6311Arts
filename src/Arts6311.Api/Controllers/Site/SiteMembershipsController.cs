using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Memberships.Facade;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /api/v1/site/memberships</c>: every published tier, in display order, for <c>/memberships</c> (ADR 0040,
/// 0042). A public read, so it opts out of authentication explicitly; a small fixed list, so not paged.
/// </summary>
/// <remarks>
/// Cached in the API (HybridCache, cleared by <c>site.revalidate-requested</c>) and for a minute by anything in front
/// of it (<c>Cache-Control: public</c>). Price and cadence are Square's record, read-only here.
/// </remarks>
[ApiController]
[Route("api/v1/site/memberships")]
[AllowAnonymous]
public sealed class SiteMembershipsController(IMembershipsFacade facade) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SiteMembershipPlanServiceModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.ListForSiteAsync(cancellationToken));
}
