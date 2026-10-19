using Arts6311.Domain.Modules.Operations.Facade;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Operations;

/// <summary>
/// Stages a <c>system.ping-requested</c> message so one trace can be followed API → SQL → Worker → handler.
/// </summary>
/// <remarks>
/// Development-only until it is deleted: there is no authentication before 1.7, so outside Development the route
/// is a 404. The API has no external endpoint, so in Development only this machine reaches it.
/// Injecting <see cref="IHostEnvironment"/> is a deliberate exception to "controllers inject facades only"
/// (backend.md): the gate is edge policy for scaffolding that is deleted in 1.12, not a pattern to copy.
/// </remarks>
[ApiController]
[Route("api/v1/cms/ops/ping")]
[Obsolete("Development scaffolding from prompt 1.5. Delete in 1.12. Until then, 1.7 must add [Authorize(Policy = CmsAdmin)].")]
public sealed class OpsPingController(IOperationsFacade facade, IHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<PingRequestedServiceModel>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RequestPingAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        // The operation has no expected failure, so a failed result is a bug: a 500, never a 202 with no body.
        // ProblemDetails with stable codes and the correlation id arrive with the API conventions in 1.7.
        var result = await facade.RequestPingAsync(cancellationToken);
        return result.Succeeded
            ? Accepted(result.Value)
            : StatusCode(
                StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "The ping could not be requested.",
                    Status = StatusCodes.Status500InternalServerError,
                });
    }
}
