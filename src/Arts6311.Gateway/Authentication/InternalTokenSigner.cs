using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Arts6311.Gateway.Authentication;

internal interface IInternalTokenSigner
{
    /// <summary>
    /// Signs a short-lived token for the signed-in person behind <paramref name="user"/>, or returns
    /// <c>null</c> when there is nobody to sign for — an anonymous request must carry no token at all, so the
    /// API's <c>[AllowAnonymous]</c> routes decide for themselves.
    /// </summary>
    string? SignForUser(ClaimsPrincipal user);

    /// <summary>
    /// Signs a token for the gateway acting as itself (<c>token_use=service</c>), which only
    /// <c>/api/v1/internal/**</c> accepts. Used by the gateway's own outbound calls from 1.10, never by the proxy:
    /// internal paths are refused at the edge (<c>gateway.md</c>).
    /// </summary>
    string SignForService();
}

/// <summary>
/// Mints the internal token the API verifies (ADR 0002, ADR 0021). A singleton holding the one
/// <see cref="ECDsa"/> for the process: the key is imported once at startup, not per request.
/// </summary>
internal sealed class InternalTokenSigner : IInternalTokenSigner, IDisposable
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly ECDsa key;
    private readonly SigningCredentials credentials;
    private readonly TimeProvider time;

    public InternalTokenSigner(InternalTokenOptions options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.time = time;
        key = options.ImportSigningKey();
        credentials = new SigningCredentials(new ECDsaSecurityKey(key), SecurityAlgorithms.EcdsaSha256);
    }

    public string? SignForUser(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var subject = user.FindFirst(InternalTokenDefaults.SubjectClaim)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(subject))
        {
            // The API refuses a user token with no sub, so signing one would produce a 403 the caller cannot act
            // on. An authenticated principal without a subject is a bug in whatever established the session.
            throw new InvalidOperationException(
                "An authenticated principal carried no subject claim, so no internal token can be signed for it.");
        }

        var roles = user.FindAll(InternalTokenDefaults.RoleClaim)
            .Concat(user.FindAll(ClaimTypes.Role))
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [InternalTokenDefaults.SubjectClaim] = subject,
            [InternalTokenDefaults.TokenUseClaim] = InternalTokenDefaults.UserTokenUse,
        };

        if (user.FindFirst(InternalTokenDefaults.SessionIdClaim)?.Value is { Length: > 0 } sessionId)
        {
            claims[InternalTokenDefaults.SessionIdClaim] = sessionId;
        }

        if (roles.Length > 0)
        {
            // One role stays a string; several become an array. Both are read back as multiple role claims.
            claims[InternalTokenDefaults.RoleClaim] = roles.Length == 1 ? roles[0] : roles;
        }

        return Sign(claims);
    }

    public string SignForService() => Sign(new Dictionary<string, object>(StringComparer.Ordinal)
    {
        [InternalTokenDefaults.SubjectClaim] = InternalTokenDefaults.Issuer,
        [InternalTokenDefaults.TokenUseClaim] = InternalTokenDefaults.ServiceTokenUse,
    });

    private string Sign(Dictionary<string, object> claims)
    {
        var issuedAt = time.GetUtcNow().UtcDateTime;

        return Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = InternalTokenDefaults.Issuer,
            Audience = InternalTokenDefaults.Audience,
            Claims = claims,

            // nbf and exp both set, and exp - nbf is exactly the lifetime: the API bounds the lifetime as a
            // property of the token and refuses one with no start instant (ADR 0021).
            NotBefore = issuedAt,
            IssuedAt = issuedAt,
            Expires = issuedAt + InternalTokenDefaults.Lifetime,
            SigningCredentials = credentials,
        });
    }

    public void Dispose() => key.Dispose();
}
