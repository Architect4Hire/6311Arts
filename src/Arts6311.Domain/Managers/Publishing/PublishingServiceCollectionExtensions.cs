using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Publishing;

public static class PublishingServiceCollectionExtensions
{
    /// <summary>
    /// The content lifecycle's shared services: who still names a record, asked before it is deleted (ADR 0052).
    /// Scoped, because each <see cref="IContentReferenceSource"/> reads the request's <c>ArtsDbContext</c>. The
    /// sources themselves are registered by the modules that hold the references.
    /// </summary>
    public static IServiceCollection AddArtsPublishing(this IServiceCollection services)
    {
        services.TryAddScoped<IContentReferences, ContentReferences>();
        return services;
    }
}
