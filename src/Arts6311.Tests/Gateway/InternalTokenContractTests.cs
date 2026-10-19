using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using Arts6311.Gateway.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ApiInternalToken = Arts6311.Api.Authentication.InternalTokenDefaults;
using ApiInternalTokenOptions = Arts6311.Api.Authentication.InternalTokenOptions;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// The obligation ADR 0021 left on this prompt: what the gateway signs is what the API accepts.
/// </summary>
/// <remarks>
/// The two <c>InternalTokenDefaults</c> are separate copies because the gateway holds no domain code and cannot
/// reference the API. This test project references both, so it is the only place the halves can be compared — and
/// a drift between them would otherwise surface as every internal request failing in production, with nothing in
/// either project looking wrong on its own.
/// </remarks>
public sealed class InternalTokenContractTests
{
    [Fact]
    public void TheTwoCopiesOfTheConstantsAgree()
    {
        Assert.Equal(ApiInternalToken.Issuer, InternalTokenDefaults.Issuer);
        Assert.Equal(ApiInternalToken.Audience, InternalTokenDefaults.Audience);
        Assert.Equal(ApiInternalToken.TokenUseClaim, InternalTokenDefaults.TokenUseClaim);
        Assert.Equal(ApiInternalToken.UserTokenUse, InternalTokenDefaults.UserTokenUse);
        Assert.Equal(ApiInternalToken.ServiceTokenUse, InternalTokenDefaults.ServiceTokenUse);
        Assert.Equal(ApiInternalToken.RoleClaim, InternalTokenDefaults.RoleClaim);
        Assert.Equal(ApiInternalToken.SubjectClaim, InternalTokenDefaults.SubjectClaim);
        Assert.Equal(ApiInternalToken.SessionIdClaim, InternalTokenDefaults.SessionIdClaim);
    }

    [Fact]
    public void EveryConstantTheGatewayUsesMatchesTheApis()
    {
        // The equality above only covers names someone thought to list; this catches a claim added to one side.
        // Compared in one direction on purpose: the gateway's string constants must all exist on the API with the
        // same value, while the API keeps two of its own (the scheme name, and MaxLifetime as a *limit* rather
        // than the single lifetime the gateway signs with).
        var api = StringConstants(typeof(ApiInternalToken));

        foreach (var (name, value) in StringConstants(typeof(InternalTokenDefaults)))
        {
            Assert.True(api.ContainsKey(name), $"The API has no constant named {name}.");
            Assert.Equal(api[name], value);
        }
    }

    [Fact]
    public void TheSignedLifetimeIsWithinWhatTheApiAccepts()
    {
        // The gateway signs for exactly this long; the API refuses anything longer. Equal is intentional — a
        // gateway lifetime above the API's maximum would make every token it issued unusable.
        Assert.True(InternalTokenDefaults.Lifetime <= ApiInternalToken.MaxLifetime);
    }

    [Fact]
    public async Task AUserTokenFromTheGatewayIsAcceptedByTheApi()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var token = Signer(key).SignForUser(Principal("user-42", "Admin", "session-7"));

        Assert.NotNull(token);

        var result = await ValidateAsApi(token, key);

        Assert.True(result.IsValid, result.Exception?.ToString());
        Assert.Equal("user-42", result.ClaimsIdentity.FindFirst(ApiInternalToken.SubjectClaim)?.Value);
        Assert.Equal("session-7", result.ClaimsIdentity.FindFirst(ApiInternalToken.SessionIdClaim)?.Value);
        Assert.Equal(ApiInternalToken.UserTokenUse, result.ClaimsIdentity.FindFirst(ApiInternalToken.TokenUseClaim)?.Value);
        Assert.Equal("Admin", result.ClaimsIdentity.FindFirst(ApiInternalToken.RoleClaim)?.Value);
    }

    [Fact]
    public async Task AServiceTokenFromTheGatewayIsAcceptedByTheApi()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var result = await ValidateAsApi(Signer(key).SignForService(), key);

        Assert.True(result.IsValid, result.Exception?.ToString());
        Assert.Equal(
            ApiInternalToken.ServiceTokenUse,
            result.ClaimsIdentity.FindFirst(ApiInternalToken.TokenUseClaim)?.Value);
    }

    [Fact]
    public async Task SeveralRolesSurviveTheRoundTrip()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var token = Signer(key).SignForUser(Principal("user-42", "Admin", "session-7", "Editor"));

        var result = await ValidateAsApi(token!, key);

        Assert.True(result.IsValid, result.Exception?.ToString());
        Assert.Equal(
            ["Admin", "Editor"],
            result.ClaimsIdentity.FindAll(ApiInternalToken.RoleClaim).Select(claim => claim.Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ATokenSignedByADifferentKeyIsRefused()
    {
        // Proves the round trip above is a real signature check and not an accident of shared configuration.
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var result = await ValidateAsApi(Signer(signingKey).SignForService(), otherKey);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void TheGatewaySignsNothingForAnAnonymousCaller()
    {
        // An anonymous request must reach the API with no credential at all (ADR 0021).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Null(Signer(key).SignForUser(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void AnAuthenticatedPrincipalWithNoSubjectIsARefusal()
    {
        // The API rejects a user token with no sub, so signing one would produce a 403 the caller cannot act on.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var nameless = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "Admin")], "test"));

        Assert.Throws<InvalidOperationException>(() => Signer(key).SignForUser(nameless));
    }

    /// <summary>
    /// Validates with the parameters the API actually uses, read from its own options type, so this test cannot
    /// drift from the API's configuration.
    /// </summary>
    private static Task<TokenValidationResult> ValidateAsApi(string token, ECDsa publicHalf)
    {
        var apiOptions = new ApiInternalTokenOptions
        {
            PublicKeyPem = publicHalf.ExportSubjectPublicKeyInfoPem(),
        };

        return new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = apiOptions.Issuer,
            ValidAudience = apiOptions.Audience,
            IssuerSigningKey = new ECDsaSecurityKey(apiOptions.ImportPublicKey()),
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = ApiInternalToken.SubjectClaim,
            RoleClaimType = ApiInternalToken.RoleClaim,
        });
    }

    /// <summary>
    /// Signs on the real clock. These tests validate with the API's parameters, which check the lifetime against
    /// now, so a fixed instant would make every token look expired — the question here is the signature and the
    /// claims, not the clock. <see cref="InternalTokenSignerTests"/> pins the lifetime against a fake one.
    /// </summary>
    private static InternalTokenSigner Signer(ECDsa key) =>
        new(new InternalTokenOptions { SigningKeyPem = key.ExportPkcs8PrivateKeyPem() }, TimeProvider.System);

    private static ClaimsPrincipal Principal(string subject, string role, string sessionId, string? secondRole = null)
    {
        var claims = new List<Claim>
        {
            new(InternalTokenDefaults.SubjectClaim, subject),
            new(InternalTokenDefaults.SessionIdClaim, sessionId),
            new(InternalTokenDefaults.RoleClaim, role),
        };

        if (secondRole is not null)
        {
            claims.Add(new Claim(InternalTokenDefaults.RoleClaim, secondRole));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", InternalTokenDefaults.SubjectClaim, InternalTokenDefaults.RoleClaim));
    }

    private static Dictionary<string, string> StringConstants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
}
