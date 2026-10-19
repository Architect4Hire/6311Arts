using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Identity.Facade;
using Arts6311.Domain.Modules.Identity.Managers.ServiceModels;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Internal;

/// <summary>
/// What the gateway calls to establish and keep a browser session (auth.md "Gateway → API trust").
/// </summary>
/// <remarks>
/// <para>
/// <c>InternalService</c> only: these routes accept a <c>token_use=service</c> token and nothing else, so a user
/// token — even an Admin's — is refused (ADR 0021). The gateway also answers 404 for <c>/api/*/internal/**</c>
/// from a browser (ADR 0022), so a browser cannot reach them even with a session. Two locks, deliberately.
/// </para>
/// <para>
/// These are the only auth routes in the API, and they are not browser-facing: there is no login form, no cookie
/// and no redirect here. The browser-facing <c>/bff/**</c> endpoints are the gateway's, in 1.10.
/// </para>
/// <para>
/// <strong>The failure codes below distinguish a lockout from a bad password, and 1.10 must not pass that
/// distinction to a browser.</strong> The gateway needs it to word its message and to audit; a browser given it
/// can enumerate accounts, which auth.md forbids (ADR 0023).
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/internal/sessions")]
[Authorize(Policy = ApiPolicies.InternalService)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalSessionsController(IIdentityFacade facade) : ControllerBase
{
    /// <summary>Checks an email and password, and returns what the gateway needs to build a session ticket.</summary>
    [HttpPost("credentials")]
    [ProducesResponseType<UserSummaryServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CheckCredentialsAsync(
        [FromBody] CredentialCheckViewModel request, CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.CheckCredentialsAsync(request, cancellationToken));

    /// <summary>
    /// Whether a session ticket is still good. The gateway calls this periodically, which is how a password
    /// reset or "log out everywhere" ends a session that is already signed in (auth.md "Revalidation").
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType<SessionValidationServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ValidateSessionAsync(
        [FromBody] SessionValidationViewModel request, CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.ValidateSessionAsync(request, cancellationToken));
}
