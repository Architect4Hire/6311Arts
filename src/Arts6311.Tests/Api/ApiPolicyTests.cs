using System.Security.Claims;
using Arts6311.Api.Authentication;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Api;

/// <summary>
/// The policy definitions themselves (auth.md "Authorization"), evaluated against principals rather than through
/// routes: <c>Member</c> and <c>InternalService</c> have no route yet, and the matrix is the part that must hold
/// before 1.9 starts issuing real roles.
/// </summary>
public sealed class ApiPolicyTests : IDisposable
{
    private readonly ApiFactory api = new();

    [Theory]
    // CmsEditor: Admin or Editor, and nobody else.
    [InlineData(ApiPolicies.CmsEditor, "Admin", true)]
    [InlineData(ApiPolicies.CmsEditor, "Editor", true)]
    [InlineData(ApiPolicies.CmsEditor, "Member", false)]
    [InlineData(ApiPolicies.CmsEditor, null, false)]

    // CmsAdmin: Admin only. An Editor must not reach a delete or a sync replay.
    [InlineData(ApiPolicies.CmsAdmin, "Admin", true)]
    [InlineData(ApiPolicies.CmsAdmin, "Editor", false)]
    [InlineData(ApiPolicies.CmsAdmin, "Member", false)]

    // Member: the member area. Staff roles are not members by implication.
    [InlineData(ApiPolicies.Member, "Member", true)]
    [InlineData(ApiPolicies.Member, "Admin", false)]
    [InlineData(ApiPolicies.Member, "Editor", false)]

    // The default policy needs no role, only a user token.
    [InlineData(ApiPolicies.AuthenticatedUser, null, true)]
    [InlineData(ApiPolicies.AuthenticatedUser, "Member", true)]
    public async Task AUserTokenIsJudgedByItsRole(string policy, string? role, bool allowed)
    {
        var principal = Principal(tokenUse: "user", role: role);

        Assert.Equal(allowed, await Succeeds(policy, principal));
    }

    [Theory]
    [InlineData(ApiPolicies.AuthenticatedUser)]
    [InlineData(ApiPolicies.CmsEditor)]
    [InlineData(ApiPolicies.CmsAdmin)]
    [InlineData(ApiPolicies.Member)]
    public async Task NoProductPolicyAcceptsAServiceToken(string policy)
    {
        // Every product policy is built on the same base, so the gateway's own token is refused by all of them and
        // not merely by the default (auth.md: "Product routes accept only token_use=user"). A service token with an
        // Admin role is the case worth proving: the role must not be enough on its own.
        var principal = Principal(tokenUse: "service", role: "Admin");

        Assert.False(await Succeeds(policy, principal));
    }

    [Fact]
    public async Task InternalServiceAcceptsOnlyAServiceToken()
    {
        Assert.True(await Succeeds(ApiPolicies.InternalService, Principal(tokenUse: "service", role: null)));
        Assert.False(await Succeeds(ApiPolicies.InternalService, Principal(tokenUse: "user", role: "Admin")));
    }

    [Fact]
    public async Task TheDefaultPolicyIsTheUserPolicy()
    {
        // A future controller written as a bare [Authorize], or [Authorize(Roles = "Admin")] with no policy name,
        // falls back to the framework default. Left alone that default is "any authenticated user", which the
        // gateway's service token satisfies — so a product route would accept one. MapAuthenticatedControllers
        // deliberately does not touch an endpoint that already declares authorization, which is why the default
        // itself has to be the user policy.
        using var scope = api.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var fallback = await provider.GetDefaultPolicyAsync();

        Assert.False(
            (await authorization.AuthorizeAsync(Principal("service", "Admin"), resource: null, fallback)).Succeeded);
        Assert.True(
            (await authorization.AuthorizeAsync(Principal("user", "Member"), resource: null, fallback)).Succeeded);
    }

    [Fact]
    public async Task AUserTokenWithNoSubjectSatisfiesNoProductPolicy()
    {
        // token_use=user without a sub would authenticate as a nameless principal, leaving every resource-level
        // check in Business ("a member reads only their own subscription") nothing to compare against.
        var nameless = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("token_use", "user"), new Claim("role", "Admin")], "InternalToken", "sub", "role"));

        Assert.False(await Succeeds(ApiPolicies.AuthenticatedUser, nameless));
        Assert.False(await Succeeds(ApiPolicies.CmsAdmin, nameless));
    }

    [Fact]
    public async Task AnUnauthenticatedPrincipalSatisfiesNothing()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        foreach (var policy in new[]
                 {
                     ApiPolicies.AuthenticatedUser, ApiPolicies.CmsEditor, ApiPolicies.CmsAdmin,
                     ApiPolicies.Member, ApiPolicies.InternalService,
                 })
        {
            Assert.False(await Succeeds(policy, anonymous), policy);
        }
    }

    /// <summary>
    /// A principal shaped the way the JWT handler produces one: the role claim type is <c>role</c>, not the SOAP-era
    /// URI, because <c>MapInboundClaims</c> is off.
    /// </summary>
    private static ClaimsPrincipal Principal(string tokenUse, string? role)
    {
        var claims = new List<Claim> { new("sub", "a-user"), new("token_use", tokenUse) };
        if (role is not null)
        {
            claims.Add(new Claim("role", role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "InternalToken", "sub", "role"));
    }

    private async Task<bool> Succeeds(string policy, ClaimsPrincipal principal)
    {
        using var scope = api.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(principal, resource: null, policy);
        return result.Succeeded;
    }

    public void Dispose() => api.Dispose();
}
