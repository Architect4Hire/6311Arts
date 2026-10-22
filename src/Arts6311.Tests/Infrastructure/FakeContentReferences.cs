using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Tests.Infrastructure;

/// <summary>
/// What the modules say still names a record (ADR 0052), as a facade asks before a delete. Nothing names it
/// unless a test says otherwise, which is the usual case.
/// </summary>
internal sealed class FakeContentReferences : IContentReferences
{
    /// <summary>What to answer for a type, keyed by the <c>SiteContentTypes</c> value the facade asks about.</summary>
    public Dictionary<string, List<ContentReference>> Naming { get; } = new(StringComparer.Ordinal);

    /// <summary>What was asked, so a test can hold that the facade asked about the right record.</summary>
    public List<(string EntityType, Guid EntityId)> Asked { get; } = [];

    public Task<IReadOnlyList<ContentReference>> ToAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        Asked.Add((entityType, entityId));
        return Task.FromResult<IReadOnlyList<ContentReference>>(
            Naming.TryGetValue(entityType, out var naming) ? naming : []);
    }
}
