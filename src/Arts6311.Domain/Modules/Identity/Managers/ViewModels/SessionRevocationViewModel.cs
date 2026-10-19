namespace Arts6311.Domain.Modules.Identity.Managers.ViewModels;

/// <summary>
/// What the gateway sends to end every session a person has
/// (<c>POST /api/v1/internal/sessions/revoke</c>, behind <c>/bff/logout-everywhere</c>).
/// </summary>
/// <remarks>
/// The user id and nothing else. The gateway already holds a session for this person, so it is not asking the
/// API to decide who the caller is; the session that asked is deleted by the gateway itself, and the rest end at
/// their next revalidation (auth.md "Revalidation").
/// </remarks>
public sealed record SessionRevocationViewModel(string UserId);
