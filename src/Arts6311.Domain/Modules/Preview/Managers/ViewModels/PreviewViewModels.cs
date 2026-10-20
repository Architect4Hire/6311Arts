namespace Arts6311.Domain.Modules.Preview.Managers.ViewModels;

/// <summary>Asks for a preview link for one record.</summary>
/// <param name="EntityType"><c>artist</c>, <c>exhibition</c> or <c>page</c>.</param>
/// <param name="EntityId">Which record.</param>
public sealed record PreviewLinkRequestViewModel(string EntityType, Guid EntityId);
