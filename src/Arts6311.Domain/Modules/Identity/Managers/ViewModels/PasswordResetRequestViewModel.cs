namespace Arts6311.Domain.Modules.Identity.Managers.ViewModels;

/// <summary>
/// What the gateway sends when someone asks for a reset link (<c>POST /api/v1/internal/identity/password-reset</c>,
/// behind <c>/bff/forgot-password</c>).
/// </summary>
/// <remarks>
/// The address is here and in no payload, log line or audit row: the outbox message carries the user id only, and
/// the composer looks the address up again at send time (ADR 0023).
/// </remarks>
public sealed record PasswordResetRequestViewModel(string Email)
{
    /// <summary>Nothing: the generated <c>ToString</c> would print the address.</summary>
    public override string ToString() => nameof(PasswordResetRequestViewModel);
}
