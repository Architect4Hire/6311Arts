using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Identity.Facade;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Internal;

/// <summary>
/// What the gateway calls behind <c>/bff/forgot-password</c>, <c>/bff/reset-password</c> and
/// <c>/bff/confirm-email</c> (auth.md, ADR 0028).
/// </summary>
/// <remarks>
/// <para>
/// <c>InternalService</c> only, excluded from the OpenAPI document, and 404 from a browser at the gateway: the same
/// two locks as <see cref="InternalSessionsController"/>.
/// </para>
/// <para>
/// <strong>Tokens and passwords arrive in the body and nowhere else.</strong> The routes are fixed paths with no
/// query, so nothing secret can reach a URL, an access log or the <c>url.*</c> attributes of a span.
/// </para>
/// <para>
/// The distinct codes here (<c>auth.token_invalid</c>, <c>auth.password_rejected</c>) are safe to pass on: every
/// unusable link shares the first, and the second is only reachable with a valid token.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/internal/identity")]
[Authorize(Policy = ApiPolicies.InternalService)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalIdentityController(IIdentityFacade facade) : ControllerBase
{
    /// <summary>
    /// Stages a reset email if the address is registered. <c>202</c> either way; only a malformed body is refused.
    /// </summary>
    [HttpPost("password-reset")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RequestPasswordResetAsync(
        [FromBody] PasswordResetRequestViewModel request, CancellationToken cancellationToken) =>
        this.ToAcceptedResult(await facade.RequestPasswordResetAsync(request, cancellationToken));

    /// <summary>
    /// Sets a new password with the emailed link's user id and token. Rotates the security stamp, which ends every
    /// live session at its next revalidation.
    /// </summary>
    [HttpPost("password-reset/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CompletePasswordResetAsync(
        [FromBody] PasswordResetCompletionViewModel request, CancellationToken cancellationToken) =>
        this.ToNoContentResult(await facade.ResetPasswordAsync(request, cancellationToken));

    /// <summary>Confirms an address with the emailed link's user id and token. Works once.</summary>
    [HttpPost("email-confirmation/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CompleteEmailConfirmationAsync(
        [FromBody] EmailConfirmationCompletionViewModel request, CancellationToken cancellationToken) =>
        this.ToNoContentResult(await facade.ConfirmEmailAsync(request, cancellationToken));
}
