namespace Arts6311.Domain.Modules.Identity.Managers.ServiceModels;

/// <summary>
/// What a composer needs to build a confirmation or reset email: who it goes to, and the Identity token the link
/// carries.
/// </summary>
/// <remarks>
/// Minted at send time and never stored or put in an outbox payload (add-outbox-message forbids tokens there).
/// It stays in-process: the composer puts it straight into the link and nothing else sees it.
/// </remarks>
public sealed record EmailTokenServiceModel(string Email, string DisplayName, string Token);
