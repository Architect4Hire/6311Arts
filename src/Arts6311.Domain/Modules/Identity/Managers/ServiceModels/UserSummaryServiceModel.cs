namespace Arts6311.Domain.Modules.Identity.Managers.ServiceModels;

/// <summary>
/// What the gateway needs to build a session after a successful credential check.
/// </summary>
/// <remarks>
/// No password hash, no email address and no token. The gateway stores the ticket in Redis and puts
/// <see cref="UserId"/>, <see cref="Roles"/> and the session id into the internal token it signs per request
/// (ADR 0002); <c>/bff/session</c> returns only a display name and roles.
/// </remarks>
/// <param name="SecurityStamp">
/// Identity's stamp as it stands now. The gateway keeps it on the ticket and revalidates it, so a password reset
/// or "log out everywhere" ends the session (auth.md "Revalidation").
/// </param>
public sealed record UserSummaryServiceModel(
    string UserId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string SecurityStamp);
