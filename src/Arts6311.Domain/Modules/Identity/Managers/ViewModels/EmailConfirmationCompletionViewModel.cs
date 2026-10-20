namespace Arts6311.Domain.Modules.Identity.Managers.ViewModels;

/// <summary>
/// The user id and token from an emailed confirmation link
/// (<c>POST /api/v1/internal/identity/email-confirmation/complete</c>, behind <c>/bff/confirm-email</c>).
/// </summary>
/// <remarks>The token arrives in a request body and is never logged, audited or traced (ADR 0028).</remarks>
public sealed record EmailConfirmationCompletionViewModel(string UserId, string Token)
{
    /// <summary>The user id only; the generated <c>ToString</c> would print the token.</summary>
    public override string ToString() => $"{nameof(EmailConfirmationCompletionViewModel)} {{ UserId = {UserId} }}";
}
