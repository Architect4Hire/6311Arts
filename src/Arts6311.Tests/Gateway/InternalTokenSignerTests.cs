using System.Security.Claims;
using System.Security.Cryptography;
using Arts6311.Gateway.Authentication;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// What the gateway puts in a token, against a fixed clock (ADR 0002's claim table).
/// </summary>
/// <remarks>
/// <see cref="InternalTokenContractTests"/> proves the API accepts these tokens; this pins the instants, which a
/// round trip against the real clock cannot assert.
/// </remarks>
public sealed class InternalTokenSignerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);

    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    [Fact]
    public void AUserTokenLivesExactlyTwoMinutesFromNow()
    {
        // The API refuses a longer lifetime *and* refuses a token with no nbf, so both instants have to be here
        // and their difference has to be within the limit (ADR 0021).
        var token = Read(Signer().SignForUser(Principal())!);

        Assert.Equal(Now.UtcDateTime, token.ValidFrom);
        Assert.Equal(Now.UtcDateTime.AddMinutes(2), token.ValidTo);
        Assert.Equal(TimeSpan.FromMinutes(2), token.ValidTo - token.ValidFrom);
    }

    [Fact]
    public void AUserTokenCarriesTheClaimTableFromAdr0002()
    {
        var token = Read(Signer().SignForUser(Principal())!);

        Assert.Equal("6311-gateway", token.Issuer);
        Assert.Equal("6311-api", Assert.Single(token.Audiences));
        Assert.Equal("user-42", token.GetClaim("sub").Value);
        Assert.Equal("session-7", token.GetClaim("sid").Value);
        Assert.Equal("user", token.GetClaim("token_use").Value);
        Assert.Equal("Admin", token.GetClaim("role").Value);
    }

    [Fact]
    public void ItIsSignedWithEs256()
    {
        // Not just "a signature": the API restricts ValidAlgorithms to ES256 and rejects anything else on the
        // header, so the algorithm is part of the contract.
        Assert.Equal("ES256", Read(Signer().SignForService()).Alg);
    }

    [Fact]
    public void AServiceTokenNamesTheGatewayAndNoRole()
    {
        // The gateway acting as itself has no user and no role; only /api/v1/internal/** accepts it.
        var token = Read(Signer().SignForService());

        Assert.Equal("service", token.GetClaim("token_use").Value);
        Assert.Equal("6311-gateway", token.GetClaim("sub").Value);
        Assert.False(token.TryGetClaim("role", out _));
    }

    [Fact]
    public void AUserWithNoSessionGetsNoSidClaim()
    {
        // Rather than an empty one: a blank sid would look like a session that exists.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "user-42"), new Claim("role", "Member")], "test", "sub", "role"));

        Assert.False(Read(Signer().SignForUser(principal)!).TryGetClaim("sid", out _));
    }

    [Fact]
    public void TwoTokensForTheSameUserDifferByTime()
    {
        // Each request gets its own token; nothing is cached across the two-minute window, so a captured token
        // cannot be replayed beyond its own lifetime.
        var time = new FakeTimeProvider(Now);
        var signer = new InternalTokenSigner(
            new InternalTokenOptions { SigningKeyPem = key.ExportPkcs8PrivateKeyPem() }, time);

        var first = Read(signer.SignForUser(Principal())!);
        time.Advance(TimeSpan.FromSeconds(30));
        var second = Read(signer.SignForUser(Principal())!);

        Assert.Equal(first.ValidTo.AddSeconds(30), second.ValidTo);
    }

    [Fact]
    public void TheSignerRefusesAPublicKey()
    {
        // The gateway signs, so it needs the private half. Being handed the API's public key would otherwise fail
        // at the first request rather than at startup.
        var options = new InternalTokenOptions { SigningKeyPem = key.ExportSubjectPublicKeyInfoPem() };

        var exception = Assert.Throws<InvalidOperationException>(options.ImportSigningKey);

        Assert.Contains("not a private key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSignerRefusesAMissingKey()
    {
        var exception = Assert.Throws<InvalidOperationException>(new InternalTokenOptions().ImportSigningKey);

        Assert.Contains("InternalToken__SigningKeyPem", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSignerRefusesTheWrongCurve()
    {
        // ES256 means P-256. A P-384 key would sign happily and be rejected by the API on every request.
        using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var options = new InternalTokenOptions { SigningKeyPem = wrongCurve.ExportPkcs8PrivateKeyPem() };

        var exception = Assert.Throws<InvalidOperationException>(options.ImportSigningKey);

        Assert.Contains("P-256", exception.Message, StringComparison.Ordinal);
    }

    private InternalTokenSigner Signer() => new(
        new InternalTokenOptions { SigningKeyPem = key.ExportPkcs8PrivateKeyPem() }, new FakeTimeProvider(Now));

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(
        [new Claim("sub", "user-42"), new Claim("sid", "session-7"), new Claim("role", "Admin")],
        "test",
        "sub",
        "role"));

    private static JsonWebToken Read(string token) => new(token);

    public void Dispose() => key.Dispose();
}
