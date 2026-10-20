namespace Arts6311.Domain.Modules.Identity.Managers.ViewModels;

/// <summary>
/// What the gateway sends when someone asks for a new confirmation link
/// (<c>POST /api/v1/internal/identity/email-confirmation</c>, behind <c>/bff/resend-confirmation</c>).
/// </summary>
/// <remarks>
/// An address, not a user id: the person asking holds only their address once a link has failed, and the user id
/// never appears anywhere but an emailed link. The address is here and in no payload, log line or audit row, as for
/// <see cref="PasswordResetRequestViewModel"/> (ADR 0023).
/// </remarks>
public sealed record EmailConfirmationRequestViewModel(string Email)
{
    /// <summary>Nothing: the generated <c>ToString</c> would print the address.</summary>
    public override string ToString() => nameof(EmailConfirmationRequestViewModel);
}
