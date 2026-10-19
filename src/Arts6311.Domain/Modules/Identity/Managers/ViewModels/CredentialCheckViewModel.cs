namespace Arts6311.Domain.Modules.Identity.Managers.ViewModels;

/// <summary>
/// What the gateway sends to check a password (<c>POST /api/v1/internal/sessions/credentials</c>).
/// </summary>
/// <remarks>
/// The password is here and nowhere else: it is never logged, never put in an audit row, never echoed in a
/// ProblemDetails, and the API has no browser-facing route that could receive it (auth.md).
/// </remarks>
public sealed record CredentialCheckViewModel(string Email, string Password);
