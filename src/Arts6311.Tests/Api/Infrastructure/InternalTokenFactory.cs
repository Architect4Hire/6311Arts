using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Arts6311.Tests.Api.Infrastructure;

/// <summary>
/// Mints the gateway's ES256 internal token (ADR 0002). It stands in for <c>Arts6311.Gateway</c>, which only
/// learns to sign in prompt 1.8, and it is also how the awkward tokens are produced: wrong audience, expired,
/// over-long lifetime, a different key, a different algorithm.
/// </summary>
/// <remarks>
/// It holds a private key, so it exists only in the test project. The API never sees one
/// (<c>InternalTokenOptions.ImportPublicKey</c> refuses a private-key PEM outright).
/// </remarks>
internal sealed class InternalTokenFactory : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The public half, in the form the AppHost passes to the API.</summary>
    public string PublicKeyPem => key.ExportSubjectPublicKeyInfoPem();

    /// <summary>A token for a signed-in person, with the roles given. This is what a product route expects.</summary>
    public string UserToken(params string[] roles) => Token(tokenUse: "user", roles: roles);

    /// <summary>A token for the gateway acting as itself. Only <c>/api/v1/internal/**</c> accepts one.</summary>
    public string ServiceToken() => Token(tokenUse: "service");

    /// <summary>
    /// The one place a token is built, so every awkward case is the happy path with one thing changed and no test
    /// can accidentally differ in a second way.
    /// </summary>
    /// <param name="issuedAt">
    /// When the token was issued. Shifting it into the past is how an expired token is made; the API's zero clock
    /// skew means "past" needs no slack.
    /// </param>
    /// <param name="lifetime">How long it claims to live. The API refuses more than two minutes.</param>
    public string Token(
        string tokenUse = "user",
        string[]? roles = null,
        string issuer = "6311-gateway",
        string audience = "6311-api",
        DateTimeOffset? issuedAt = null,
        TimeSpan? lifetime = null,
        SigningCredentials? credentials = null)
    {
        var from = issuedAt ?? DateTimeOffset.UtcNow;
        var to = from + (lifetime ?? TimeSpan.FromMinutes(2));

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = "11111111-1111-1111-1111-111111111111",
            ["sid"] = "22222222-2222-2222-2222-222222222222",
            ["token_use"] = tokenUse,
        };

        if (roles is { Length: > 0 })
        {
            // One value stays a string and several become an array, which is how the gateway will write it and
            // how JsonWebToken reads multiple roles back out.
            claims["role"] = roles.Length == 1 ? roles[0] : roles;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            NotBefore = from.UtcDateTime,
            IssuedAt = from.UtcDateTime,
            Expires = to.UtcDateTime,
            SigningCredentials = credentials ?? Credentials(key),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>A token signed by some other P-256 key: a valid ES256 token the API must still refuse.</summary>
    public string TokenFromAnotherKey()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Token(credentials: Credentials(other));
    }

    /// <summary>
    /// A token signed with HMAC instead of ECDSA. <c>ValidAlgorithms</c> must reject it on its header, before any
    /// key is consulted — the classic algorithm-confusion attempt.
    /// </summary>
    public string TokenSignedWithHmac()
    {
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        return Token(credentials: new SigningCredentials(
            new SymmetricSecurityKey(secret), SecurityAlgorithms.HmacSha256));
    }

    /// <summary>
    /// A correctly signed ES256 token carrying <c>exp</c> but neither <c>nbf</c> nor <c>iat</c>, so its lifetime
    /// has no start instant and cannot be bounded. Built by hand: <c>SecurityTokenDescriptor</c> fills <c>nbf</c>
    /// in for itself, which would quietly make this case untestable.
    /// </summary>
    public string TokenWithNoStartInstant()
    {
        var expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var header = Base64Url(@"{""alg"":""ES256"",""typ"":""JWT""}");
        var payload = Base64Url(
            @"{""iss"":""6311-gateway"",""aud"":""6311-api"",""sub"":""1"",""token_use"":""user"",""role"":""Admin"",""exp"":"
            + expires + "}");

        var signature = key.SignData(
            Encoding.UTF8.GetBytes(header + "." + payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    private static string Base64Url(string value) => Base64Url(Encoding.UTF8.GetBytes(value));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static SigningCredentials Credentials(ECDsa signingKey) =>
        new(new ECDsaSecurityKey(signingKey), SecurityAlgorithms.EcdsaSha256);

    public void Dispose() => key.Dispose();
}
