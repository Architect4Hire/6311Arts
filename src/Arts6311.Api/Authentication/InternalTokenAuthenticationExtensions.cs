using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Arts6311.Api.Authentication;

internal static class InternalTokenAuthenticationExtensions
{
    /// <summary>
    /// Registers the API's one and only authentication scheme: the gateway's ES256 internal token (ADR 0002).
    /// There is no cookie scheme, no OAuth/OIDC handler and nothing to fall back to — a request either carries a
    /// token this gateway signed, or it is anonymous.
    /// </summary>
    /// <remarks>
    /// The public key is imported once, here, so a missing or malformed <c>InternalToken:PublicKeyPem</c> stops the
    /// host at startup instead of turning into a 401 on every request with no explanation in the logs.
    /// </remarks>
    public static IServiceCollection AddInternalTokenAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = new InternalTokenOptions();
        configuration.GetSection(InternalTokenOptions.SectionName).Bind(options);

        // Deliberately not disposed: the key is held for the application's lifetime, as the signing key's verifier.
        var publicKey = options.ImportPublicKey();
        var signingKey = new ECDsaSecurityKey(publicKey);
        var maxLifetime = options.MaxLifetime;

        services.AddAuthentication(InternalTokenDefaults.Scheme)
            .AddJwtBearer(InternalTokenDefaults.Scheme, jwt =>
            {
                // No authority and no metadata address: there is no discovery document and nothing to fetch. The
                // one key we trust is the one the AppHost handed us.
                jwt.MapInboundClaims = false;

                // The token is a credential, not something the API hands on: nothing stores it, nothing forwards it.
                jwt.SaveToken = false;

                // A 401 never explains which check failed. The reason is logged server-side instead.
                jwt.IncludeErrorDetails = false;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,

                    // ES256 only. Without this, a token could present any algorithm the key happens to support;
                    // with it, an HS256 token or `alg: none` is rejected on its header, before any key is consulted.
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],

                    ValidateLifetime = true,

                    // Tokens live two minutes. Borrowing five more from a default skew would nearly triple that.
                    ClockSkew = TimeSpan.Zero,

                    NameClaimType = InternalTokenDefaults.SubjectClaim,
                    RoleClaimType = InternalTokenDefaults.RoleClaim,
                };

                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        // "≤ 2 minutes" is a property of the token, not of when it is presented: a signature-valid
                        // token claiming an hour must be refused. Default lifetime validation only compares against
                        // now, so it would accept one happily. `nbf` is required because without it there is no
                        // start instant to measure the lifetime from (the gateway always sets it; see ADR 0021).
                        var token = context.SecurityToken;
                        if (token.ValidFrom == default || token.ValidTo == default)
                        {
                            context.Fail("The internal token must carry both nbf and exp.");
                            return Task.CompletedTask;
                        }

                        if (token.ValidTo - token.ValidFrom > maxLifetime)
                        {
                            context.Fail($"The internal token's lifetime exceeds {maxLifetime}.");
                        }

                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        // The exception *type* only. It already says which check failed
                        // (SecurityTokenExpiredException, ...InvalidAudienceException, ...SignatureKeyNotFound),
                        // while IdentityModel's messages quote claim values and sometimes the token itself. A
                        // rejected token is still a credential, and logs are not the place for one.
                        context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<InternalTokenScheme>>()
                            .InternalTokenRejected(context.Exception.GetType().Name);
                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }
}

/// <summary>The log category for internal-token authentication. Never instantiated.</summary>
internal sealed class InternalTokenScheme
{
    private InternalTokenScheme()
    {
    }
}

internal static partial class InternalTokenLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "The internal token was rejected ({ExceptionType}).")]
    public static partial void InternalTokenRejected(this ILogger<InternalTokenScheme> logger, string exceptionType);
}
