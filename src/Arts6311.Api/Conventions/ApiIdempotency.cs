using Arts6311.Domain.Managers.Idempotency;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Conventions;

/// <summary>
/// The HTTP half of idempotent commands, which ADR 0020 leaves to the API: read the <c>Idempotency-Key</c> header,
/// and mark a replayed response.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately thin. The key's own rules (required, length, printable ASCII) are already enforced in
/// <c>IdempotencyBusiness</c>, which answers with <c>idempotency.key_required</c>,
/// <c>idempotency.key_invalid</c> and <c>idempotency.key_reused</c>; those codes reach the right statuses through
/// <see cref="ApiProblemDetails"/> (the last one a 422, per api-contract.md). Re-checking them here would mean two
/// places to keep in step.
/// </para>
/// <para>
/// The first idempotent route is the shop's CSV import (ADR 0053); checkout pay follows in 5.14. A route binds the
/// header with <c>[FromHeader]</c> only so OpenAPI declares it, and reads it through <see cref="IdempotencyKey"/>,
/// which treats several values as none where model binding would join them.
/// </para>
/// </remarks>
internal static class ApiIdempotency
{
    /// <summary>
    /// The client's <c>Idempotency-Key</c>, or <c>null</c> when it sent none. Passed straight to
    /// <see cref="IdempotentCommand.Key"/>, which decides whether its absence is a failure.
    /// </summary>
    public static string? IdempotencyKey(this ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var header = controller.Request.Headers[IdempotencyPolicy.KeyHeader];

        // Several values means an ambiguous key. Treat it as absent rather than picking one, and let the command's
        // KeyRequired decide: guessing which one the client meant could replay the wrong result.
        return header.Count == 1 && !string.IsNullOrEmpty(header[0]) ? header[0] : null;
    }

    /// <summary>
    /// Maps an idempotent outcome, setting <c>Idempotent-Replayed</c> when the body is a committed earlier
    /// result rather than work done now, so a client can tell a replay from a fresh run.
    /// </summary>
    public static IActionResult ToActionResult<T>(this ControllerBase controller, IdempotentOutcome<T> outcome)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.Replayed)
        {
            controller.Response.Headers[IdempotencyPolicy.ReplayedHeader] = "true";
        }

        return controller.ToActionResult(outcome.Result);
    }
}
