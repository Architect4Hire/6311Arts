using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Domain.Managers.Validation;

public static class ValidationServiceCollectionExtensions
{
    /// <summary>
    /// Discovers every <see cref="IValidator{T}"/> in the Domain by scanning this assembly once, so a module adds a
    /// validator beside its ViewModel and nothing else has to be registered (the <c>add-endpoint</c> skill's
    /// "validators are discovered by assembly scan already").
    /// </summary>
    /// <remarks>
    /// Scoped, matching the facades that resolve them. Internal types are included: a validator is an
    /// implementation detail of its module, not part of its boundary, so most are <c>internal sealed</c>.
    /// </remarks>
    public static IServiceCollection AddArtsValidation(this IServiceCollection services) =>
        services.AddValidatorsFromAssemblyContaining<ValidationMarker>(
            lifetime: ServiceLifetime.Scoped, includeInternalTypes: true);
}

/// <summary>Anchors the validator scan to the Domain assembly. Never instantiated.</summary>
internal sealed class ValidationMarker
{
    private ValidationMarker()
    {
    }
}
