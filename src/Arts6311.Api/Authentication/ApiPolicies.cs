using Microsoft.AspNetCore.Authorization;

namespace Arts6311.Api.Authentication;

/// <summary>
/// The API's authorization policies (<c>.claude/rules/auth.md</c> "Authorization"). Roles are <c>Admin</c>,
/// <c>Editor</c> and <c>Member</c>; 1.9 fills them from Identity.
/// </summary>
internal static class ApiPolicies
{
    /// <summary>
    /// The policy every route gets unless it declares another one, or opts out with <c>[AllowAnonymous]</c>.
    /// </summary>
    public const string AuthenticatedUser = "AuthenticatedUser";

    /// <summary>Admin or Editor: CMS content.</summary>
    public const string CmsEditor = "CmsEditor";

    /// <summary>Admin only: deletes, sync replay, user management.</summary>
    public const string CmsAdmin = "CmsAdmin";

    /// <summary>The member area on the public site.</summary>
    public const string Member = "Member";

    /// <summary>
    /// <c>/api/v1/internal/**</c>: the gateway calling the API on its own behalf, not a user's. The gateway also
    /// returns 404 for these paths from browsers (<c>gateway.md</c>), so this is the second of two locks.
    /// </summary>
    public const string InternalService = "InternalService";

    public const string AdminRole = "Admin";

    public const string EditorRole = "Editor";

    public const string MemberRole = "Member";
}

internal static class ApiAuthorizationExtensions
{
    /// <summary>
    /// Registers the route policies. Every product policy is built on the same base — authenticated *and*
    /// <c>token_use=user</c> — so a service token is refused by all of them, not only by the default.
    /// </summary>
    public static IServiceCollection AddArtsAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()

            // The framework's own default is "any authenticated user", which the gateway's *service* token
            // satisfies. So a future controller written as a bare [Authorize], or [Authorize(Roles = "Admin")]
            // with no policy, would accept a service token on a product route — MapAuthenticatedControllers
            // deliberately leaves such an endpoint alone, because it already declares authorization. Replacing the
            // default closes that door for every form of [Authorize] at once.
            .SetDefaultPolicy(new AuthorizationPolicyBuilder().RequireUserToken().Build())
            .AddPolicy(ApiPolicies.AuthenticatedUser, policy => policy.RequireUserToken())
            .AddPolicy(ApiPolicies.CmsEditor, policy => policy
                .RequireUserToken()
                .RequireRole(ApiPolicies.AdminRole, ApiPolicies.EditorRole))
            .AddPolicy(ApiPolicies.CmsAdmin, policy => policy
                .RequireUserToken()
                .RequireRole(ApiPolicies.AdminRole))
            .AddPolicy(ApiPolicies.Member, policy => policy
                .RequireUserToken()
                .RequireRole(ApiPolicies.MemberRole))

            // No role requirement: the caller is the gateway itself, which has no role to carry.
            .AddPolicy(ApiPolicies.InternalService, policy => policy
                .AddAuthenticationSchemes(InternalTokenDefaults.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(InternalTokenDefaults.TokenUseClaim, InternalTokenDefaults.ServiceTokenUse));

        return services;
    }

    /// <summary>
    /// A token issued for a signed-in person, as opposed to the gateway acting as a service. Product routes accept
    /// only this (<c>auth.md</c> "Token use"); the claim is absent from nothing we issue, so a token without it is
    /// refused rather than treated as a user.
    /// </summary>
    private static AuthorizationPolicyBuilder RequireUserToken(this AuthorizationPolicyBuilder policy) => policy
        .AddAuthenticationSchemes(InternalTokenDefaults.Scheme)
        .RequireAuthenticatedUser()
        .RequireClaim(InternalTokenDefaults.TokenUseClaim, InternalTokenDefaults.UserTokenUse)

        // A user token names a user. Without this, a token_use=user token carrying no sub would authenticate as a
        // nameless principal, and every resource-level check in Business ("a member reads only their own
        // subscription") would have nothing to compare against.
        .RequireClaim(InternalTokenDefaults.SubjectClaim);
}
