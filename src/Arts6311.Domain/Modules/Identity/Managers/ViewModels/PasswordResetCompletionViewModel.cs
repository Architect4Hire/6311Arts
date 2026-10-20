namespace Arts6311.Domain.Modules.Identity.Managers.ViewModels;

/// <summary>
/// A new password, with the user id and token from the emailed link
/// (<c>POST /api/v1/internal/identity/password-reset/complete</c>, behind <c>/bff/reset-password</c>).
/// </summary>
/// <remarks>
/// The token and the password exist on this record and nowhere else. Both arrive in a request body — never a URL —
/// and neither is logged, audited, traced or echoed in a ProblemDetails (auth.md, ADR 0028).
/// </remarks>
public sealed record PasswordResetCompletionViewModel(string UserId, string Token, string NewPassword)
{
    /// <summary>
    /// The user id only. A record's generated <c>ToString</c> prints every property, so anything that formats this
    /// object — a log template, an exception message — would otherwise write the token and the password.
    /// </summary>
    public override string ToString() => $"{nameof(PasswordResetCompletionViewModel)} {{ UserId = {UserId} }}";
}
