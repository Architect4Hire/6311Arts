using Arts6311.Api.Authentication;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Api;

/// <summary>
/// The two conventions that have to hold for every route ever added, checked against the endpoints the host
/// actually built rather than against source text: versioning (api-contract.md, "All routes are versioned from the
/// first endpoint") and secure-by-default (auth.md).
/// </summary>
/// <remarks>
/// This is what makes the literal <c>api/v1/...</c> route templates safe without a versioning library (ADR 0021):
/// a route that forgets the prefix, or an action that is neither policy-gated nor explicitly anonymous, fails here.
/// </remarks>
public sealed class ApiRouteConventionTests : IDisposable
{
    private readonly ApiFactory api = new();

    /// <summary>
    /// Webhooks are signature-verified and unversioned by design (api-contract.md "Route families"), and
    /// <c>media/</c> is image delivery: file URLs written into the site's markup, not the JSON API (ADR 0030).
    /// </summary>
    private static readonly string[] PermittedPrefixes = ["api/v1/", "webhooks/", "media/"];

    [Fact]
    public void EveryControllerRouteIsVersioned()
    {
        var offenders = Endpoints()
            .Where(endpoint => !PermittedPrefixes.Any(prefix =>
                endpoint.Route.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(endpoint => $"{endpoint.Action}: {endpoint.Route}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryActionIsEitherPolicyGatedOrExplicitlyAnonymous()
    {
        // "Secure by default" only means something if nothing can stay silent. An action with neither is the
        // dangerous case: it reads as finished and is open.
        //
        // This is also the proof that MapAuthenticatedControllers works: an action that declares no [Authorize] has
        // a policy here *only* because the convention added one, so a broken convention fails this test.
        var offenders = Endpoints()
            .Where(endpoint => !endpoint.Anonymous && endpoint.Policies.Count == 0)
            .Select(endpoint => endpoint.Action)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheInternalSessionRoutesRequireTheInternalServicePolicy()
    {
        // auth.md: /api/v1/internal/** accepts only token_use=service. The gateway's 404 is the other lock.
        var internalRoutes = Endpoints()
            .Where(endpoint => endpoint.Route.StartsWith("api/v1/internal/", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(internalRoutes);
        Assert.All(internalRoutes, endpoint =>
        {
            Assert.Contains(ApiPolicies.InternalService, endpoint.Policies);
            Assert.False(endpoint.Anonymous);
        });
    }

    [Fact]
    public void TheSiteHealthInfoRouteIsExplicitlyAnonymous()
    {
        var healthInfo = Endpoints().Single(endpoint => endpoint.Route == "api/v1/site/health-info");

        Assert.True(healthInfo.Anonymous);
    }

    [Fact]
    public void TheScanActuallySeesTheControllers()
    {
        // Without this, every assertion above would pass vacuously the day the endpoint shape changes.
        var routes = Endpoints().Select(endpoint => endpoint.Route).ToList();

        Assert.Contains("api/v1/site/health-info", routes);
        Assert.Contains("api/v1/internal/sessions/credentials", routes);
    }

    [Fact]
    public void NoEndpointOutsideTheControllersIsLeftUnprotected()
    {
        // MapAuthenticatedControllers only reaches controller endpoints. Anything else mapped on the host escapes
        // it, so the non-controller set is pinned to the three that must stay open. A future minimal-API endpoint
        // added without authorization fails here rather than shipping public.
        string[] expected = ["/health", "/alive", "/openapi/{documentName}.json"];

        var others = api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is null)
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected.Order(StringComparer.Ordinal), others.Order(StringComparer.Ordinal));
    }

    private List<ControllerEndpoint> Endpoints() =>
    [
        .. api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .Select(endpoint =>
            {
                var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!;
                var authorizeData = endpoint.Metadata.OfType<IAuthorizeData>().ToList();

                return new ControllerEndpoint(
                    Route: endpoint.RoutePattern.RawText ?? string.Empty,
                    Action: $"{action.ControllerName}.{action.ActionName}",
                    Anonymous: endpoint.Metadata.OfType<IAllowAnonymous>().Any(),
                    Policies: [.. authorizeData.Select(data => data.Policy).OfType<string>()]);
            }),
    ];

    private sealed record ControllerEndpoint(
        string Route,
        string Action,
        bool Anonymous,
        IReadOnlyList<string> Policies);

    public void Dispose() => api.Dispose();
}
