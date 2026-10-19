using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Operations.Facade;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /api/v1/site/health-info</c>: the public site's check that the API answers.
/// </summary>
/// <remarks>
/// A public site read (<c>api-contract.md</c>), so it is one of the few routes that opts out of the API's
/// secure-by-default authentication. The opt-out is the explicit <c>[AllowAnonymous]</c> below and nothing else:
/// <c>MapAuthenticatedControllers</c> would otherwise require a user token here, as it does for every route that
/// stays silent.
/// </remarks>
[ApiController]
[Route("api/v1/site/health-info")]
[AllowAnonymous]
public sealed class SiteHealthInfoController(IOperationsFacade facade) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthInfoServiceModel>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.GetHealthInfoAsync(cancellationToken));
}
