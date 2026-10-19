using System.Security.Cryptography;
using Arts6311.Api.Authentication;
using Arts6311.Tests.Api.Infrastructure;

namespace Arts6311.Tests.Api;

/// <summary>
/// The verification key is checked when the host starts, not when a request arrives. A key problem that surfaced
/// as a 401 on every request would look exactly like a signing problem in the gateway, and the one thing it must
/// never be is quiet.
/// </summary>
public sealed class InternalTokenOptionsTests
{
    [Fact]
    public void AMissingKeyIsRefused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new InternalTokenOptions().ImportPublicKey());

        // The message names the AppHost variable, so whoever hits it knows where the value comes from.
        Assert.Contains("InternalToken__PublicKeyPem", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a pem at all")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nnot base64\n-----END PUBLIC KEY-----")]
    public void AnUnreadableKeyIsRefused(string pem)
    {
        var options = new InternalTokenOptions { PublicKeyPem = pem };

        Assert.Throws<InvalidOperationException>(options.ImportPublicKey);
    }

    [Fact]
    public void APrivateKeyIsRefusedEvenThoughItWouldImport()
    {
        // The gateway's generated parameter *is* a PKCS#8 private key, so handing the API the wrong half of the
        // pair is the plausible mistake. ImportFromPem would accept it without complaint, and the API would then
        // hold signing material it has no business holding (ADR 0002).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = new InternalTokenOptions { PublicKeyPem = key.ExportPkcs8PrivateKeyPem() };

        var exception = Assert.Throws<InvalidOperationException>(options.ImportPublicKey);

        Assert.Contains("private key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyOnTheWrongCurveIsRefused()
    {
        // ES256 means P-256. A P-384 key would make every signature check fail on an algorithm mismatch instead.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var options = new InternalTokenOptions { PublicKeyPem = key.ExportSubjectPublicKeyInfoPem() };

        var exception = Assert.Throws<InvalidOperationException>(options.ImportPublicKey);

        Assert.Contains("P-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGatewaysDerivedPublicKeyIsAccepted()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = new InternalTokenOptions { PublicKeyPem = key.ExportSubjectPublicKeyInfoPem() };

        using var imported = options.ImportPublicKey();

        Assert.Equal(256, imported.KeySize);
    }

    [Fact]
    public void TheHostDoesNotStartWithoutAKey()
    {
        // Not just the options type: the API itself refuses to come up, so a deployment that forgot the variable
        // fails loudly at startup rather than serving 401s.
        using var api = new ApiFactory(publicKeyPem: string.Empty);

        Assert.Throws<InvalidOperationException>(() => api.CreateClient());
    }

    [Fact]
    public void TheDefaultsAreTheOnesTheGatewayWillSign()
    {
        // ADR 0002's table. These are not configuration: both halves must agree, and prompt 1.8 mirrors them.
        var options = new InternalTokenOptions();

        Assert.Equal("6311-gateway", options.Issuer);
        Assert.Equal("6311-api", options.Audience);
        Assert.Equal(TimeSpan.FromMinutes(2), options.MaxLifetime);
    }
}
