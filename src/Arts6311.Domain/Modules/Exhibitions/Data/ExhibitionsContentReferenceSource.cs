using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Exhibitions.Data;

/// <summary>
/// What this module says about the exhibitions that credit an artist (ADR 0033), so that artist cannot be deleted while one still names it
/// (ADR 0052). Registered with <c>TryAddEnumerable</c> in this module's own
/// <c>ServiceCollectionExtensions</c>: this module already knows about artists, so answering here keeps
/// the dependency pointing the way it already does, and the Artists module never names its
/// callers.
/// </summary>
/// <remarks>
/// It asks through this module's DataLayer, like everything else that reads from it (backend.md: each layer calls
/// only the next one), and adds no rule of its own — the question is one read, and the kernel's
/// <see cref="IContentReferences"/> is what asks it.
/// </remarks>
internal sealed class ExhibitionsContentReferenceSource(IExhibitionsDataLayer dataLayer) : IContentReferenceSource
{
    public async Task<IReadOnlyList<ContentReference>> ReferencesToAsync(
        string entityType, Guid entityId, int take, CancellationToken cancellationToken)
    {
        if (entityType != SiteContentTypes.Artist || take <= 0)
        {
            return [];
        }

        var naming = await dataLayer.ListNamingArtistAsync(entityId, take, cancellationToken);
        return [.. naming.Select(label => new ContentReference(SiteContentTypes.Exhibition, label))];
    }
}
