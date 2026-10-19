namespace Arts6311.Domain.Modules.Identity.Managers.ViewModels;

/// <summary>
/// What the gateway sends to revalidate a session ticket
/// (<c>POST /api/v1/internal/sessions/validate</c>).
/// </summary>
public sealed record SessionValidationViewModel(string UserId, string SecurityStamp);
