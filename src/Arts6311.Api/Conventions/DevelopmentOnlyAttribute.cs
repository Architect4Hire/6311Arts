using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Arts6311.Api.Conventions;

/// <summary>
/// Marks a controller that exists only in Development. Outside it the controller is never registered, so its
/// routes genuinely do not exist (api-contract.md: "Development only (404 elsewhere)").
/// </summary>
/// <remarks>
/// The gate belongs in the host, not in the action. An <c>IHostEnvironment</c> check inside a method runs *after*
/// authorization, so a route gated that way answers 401 to an anonymous caller outside Development — which both
/// tells an attacker the route is there and contradicts the contract. Removing the controller is also what lets
/// such a controller inject facades only, per backend.md.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class DevelopmentOnlyAttribute : Attribute;

/// <summary>
/// Drops <see cref="DevelopmentOnlyAttribute"/> controllers outside Development, before routes are built from them.
/// </summary>
internal sealed class DevelopmentOnlyControllerFeatureProvider(bool isDevelopment)
    : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        if (isDevelopment)
        {
            return;
        }

        // Runs after the built-in provider has filled the list, so this removes from a populated feature.
        foreach (var controller in feature.Controllers
                     .Where(controller => controller.IsDefined(typeof(DevelopmentOnlyAttribute), inherit: true))
                     .ToList())
        {
            feature.Controllers.Remove(controller);
        }
    }
}

internal static class DevelopmentOnlyExtensions
{
    /// <summary>Applies the <see cref="DevelopmentOnlyAttribute"/> gate for this environment.</summary>
    public static IMvcBuilder AddDevelopmentOnlyControllerGate(this IMvcBuilder builder, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ConfigureApplicationPartManager(manager =>
            manager.FeatureProviders.Add(new DevelopmentOnlyControllerFeatureProvider(isDevelopment)));
    }
}
