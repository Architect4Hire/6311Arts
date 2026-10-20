using Arts6311.Domain.Managers.Catalog;
using Arts6311.Tests.Artists.Infrastructure;

namespace Arts6311.Tests.Infrastructure;

/// <summary>
/// The shared SKU registry over a dictionary, recording what a write staged. <see cref="Owners"/> is what is
/// <em>committed</em>: a staged reservation is not readable from it, as it would not be from the database after the
/// save that staged it rolled back.
/// </summary>
internal sealed class FakeSkuRegistry(CallLog log) : ISkuRegistry
{
    public Dictionary<string, SkuOwner> Owners { get; } = new(StringComparer.Ordinal);

    public List<string> Reserved { get; } = [];

    public List<string> Released { get; } = [];

    public Task<IReadOnlyDictionary<string, SkuOwner>> OwnersAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, SkuOwner>>(
            skus.Where(Owners.ContainsKey).Distinct(StringComparer.Ordinal)
                .ToDictionary(sku => sku, sku => Owners[sku], StringComparer.Ordinal));

    public Task<bool> IsTakenAsync(string sku, SkuOwner owner, CancellationToken cancellationToken) =>
        Task.FromResult(Owners.TryGetValue(sku, out var current) && current != owner);

    public void Reserve(string sku, string ownerType, Guid ownerId)
    {
        log.Add($"sku-reserve:{sku}");
        Reserved.Add(sku);
    }

    public Task ReleaseAsync(string sku, SkuOwner owner, CancellationToken cancellationToken)
    {
        log.Add($"sku-release:{sku}");
        Released.Add(sku);
        if (Owners.TryGetValue(sku, out var current) && current == owner)
        {
            Owners.Remove(sku);
        }

        return Task.CompletedTask;
    }
}
