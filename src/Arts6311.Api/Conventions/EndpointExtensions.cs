using Arts6311.Api.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Arts6311.Api.Conventions;

internal static class EndpointExtensions
{
    /// <summary>
    /// Maps the controllers, requiring an authenticated user on every action that has not said otherwise:
    /// "secure by default", so a new route is protected because nobody did anything, and public reads opt out with
    /// an explicit <c>[AllowAnonymous]</c> (<c>auth.md</c> "Secure by default").
    /// </summary>
    /// <remarks>
    /// The requirement is added only to endpoints carrying neither <see cref="IAuthorizeData"/> nor
    /// <see cref="IAllowAnonymous"/>. That condition is the whole point: authorization requirements *compose*, so a
    /// blanket <c>MapControllers().RequireAuthorization()</c> would make an internal route satisfy both
    /// <see cref="ApiPolicies.AuthenticatedUser"/> (<c>token_use=user</c>) and
    /// <see cref="ApiPolicies.InternalService"/> (<c>token_use=service</c>) at once, which no token can do.
    /// Skipping endpoints that already declare a policy gives the same effect as a fallback policy while staying
    /// visible in one place, and testable (<c>ApiRouteConventionTests</c>).
    /// </remarks>
    public static IEndpointConventionBuilder MapAuthenticatedControllers(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var controllers = app.MapControllers();

        controllers.Add(endpoint =>
        {
            if (endpoint.Metadata.OfType<IAuthorizeData>().Any()
                || endpoint.Metadata.OfType<IAllowAnonymous>().Any())
            {
                return;
            }

            endpoint.Metadata.Add(new AuthorizeAttribute(ApiPolicies.AuthenticatedUser));
        });

        return controllers;
    }
}
