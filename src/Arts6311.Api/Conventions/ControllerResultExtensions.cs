using Arts6311.Domain.Managers.Results;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Conventions;

/// <summary>
/// The controller's whole job on the way out: map one facade result to HTTP (<c>backend.md</c> "Controller").
/// </summary>
/// <remarks>
/// A controller never chooses a status for a failure. The status follows the error's stable code through
/// <see cref="ApiProblemDetails.StatusFor"/>, so one code cannot mean 409 on one route and 422 on the next.
/// </remarks>
internal static class ControllerResultExtensions
{
    /// <summary>Success → <c>200</c> with the ServiceModel. Failure → the mapped <c>ProblemDetails</c>.</summary>
    public static IActionResult ToActionResult<T>(this ControllerBase controller, OperationResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);

        return result.Succeeded
            ? controller.Ok(result.Value)
            : controller.ProblemFor(result.Error!);
    }

    /// <summary>Success → <c>202</c> with the ServiceModel: accepted work the Worker will finish.</summary>
    public static IActionResult ToAcceptedResult<T>(this ControllerBase controller, OperationResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);

        return result.Succeeded
            ? controller.Accepted(result.Value)
            : controller.ProblemFor(result.Error!);
    }

    /// <summary>
    /// Success → <c>202</c> with no body: accepted work with nothing to point at, such as a reset request whose
    /// whole contract is that the caller learns nothing more.
    /// </summary>
    public static IActionResult ToAcceptedResult(this ControllerBase controller, OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);

        return result.Succeeded
            ? controller.StatusCode(StatusCodes.Status202Accepted)
            : controller.ProblemFor(result.Error!);
    }

    /// <summary>Success → <c>204</c>. Failure → the mapped <c>ProblemDetails</c>.</summary>
    public static IActionResult ToNoContentResult(this ControllerBase controller, OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);

        return result.Succeeded
            ? controller.NoContent()
            : controller.ProblemFor(result.Error!);
    }

    /// <summary>
    /// Named <c>ProblemFor</c>, not <c>Problem</c>: <see cref="ControllerBase.Problem"/> already exists and takes a
    /// status, and a same-named extension beside it would read as that method at the call site.
    /// </summary>
    private static ObjectResult ProblemFor(this ControllerBase controller, OperationError error)
    {
        var problem = ApiProblemDetails.From(error, controller.CorrelationId());
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
