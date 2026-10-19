namespace Arts6311.Domain.Modules.Identity.Managers.ServiceModels;

/// <summary>
/// The answer to "is this session still good?", asked periodically by the gateway.
/// </summary>
/// <param name="Valid">
/// False when the stamp has moved, the account is locked, or the user is gone. The gateway deletes the ticket.
/// </param>
/// <param name="Roles">
/// The roles as they stand now, so a role change takes effect at the next revalidation rather than at the next
/// sign-in. Empty when <paramref name="Valid"/> is false.
/// </param>
public sealed record SessionValidationServiceModel(bool Valid, IReadOnlyList<string> Roles);
