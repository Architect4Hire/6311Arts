using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Operations.Facade;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Operations;

/// <summary>
/// Stages a <c>system.ping-requested</c> message so one trace can be followed API → SQL → Worker → handler.
/// </summary>
/// <remarks>
/// <para>
/// Development-only and <c>CmsAdmin</c> (api-contract.md: "Development only (404 elsewhere); CmsAdmin from 1.7").
/// <see cref="DevelopmentOnlyAttribute"/> removes the controller outside Development, so the route genuinely does
/// not exist there — for an anonymous caller as much as for an admin. Prompt 1.5 did this with an
/// <c>IHostEnvironment</c> check inside the action, which ran *after* authorization and so answered 401 outside
/// Development: it both contradicted the contract and confirmed the route's existence to anyone who asked.
/// </para>
/// <para>
/// Kept out of the OpenAPI document with <see cref="ApiExplorerSettingsAttribute"/>: it is scaffolding, and
/// publishing it as contract would make its deletion in 1.12 read as a breaking path removal.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/cms/ops/ping")]
[Authorize(Policy = ApiPolicies.CmsAdmin)]
[DevelopmentOnly]
[ApiExplorerSettings(IgnoreApi = true)]
[Obsolete("Development scaffolding from prompt 1.5. Delete in 1.12.")]
public sealed class OpsPingController(IOperationsFacade facade) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<PingRequestedServiceModel>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RequestPingAsync(CancellationToken cancellationToken) =>

        // The operation has no expected failure, so the mapping never actually produces a problem. It goes through
        // the shared mapper anyway: a hand-written status here is the thing that drifts from the contract.
        this.ToAcceptedResult(await facade.RequestPingAsync(cancellationToken));
}
