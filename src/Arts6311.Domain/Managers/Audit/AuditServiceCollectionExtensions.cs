using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Audit;

public static class AuditServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IAuditWriter"/>, scoped to the request's <c>ArtsDbContext</c>.</summary>
    public static IServiceCollection AddArtsAudit(this IServiceCollection services)
    {
        services.TryAddScoped<IAuditWriter, AuditWriter>();
        return services;
    }
}
