using System.Security.Cryptography;
using System.Text;
using Arts6311.AppHost;
using Arts6311.AppHost.Parameters;

namespace Arts6311.Tests.AppHost;

/// <summary>The generated, persisted AppHost secrets have the shapes their consumers require.</summary>
public sealed class GeneratedSecretTests
{
    [Fact]
    public void SigningKeyIsAnEcdsaP256Pkcs8PrivateKey()
    {
        var pem = new EcdsaP256PrivateKeyDefault().GetDefaultValue();

        Assert.Equal("PRIVATE KEY", PemLabel(pem));
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        Assert.Equal(256, ecdsa.KeySize);
        Assert.Equal(
            ECCurve.NamedCurves.nistP256.Oid.Value,
            ecdsa.ExportParameters(includePrivateParameters: false).Curve.Oid.Value);
    }

    [Fact]
    public void DerivedPublicKeyVerifiesWhatThePrivateKeySigned()
    {
        var privateKeyPem = new EcdsaP256PrivateKeyDefault().GetDefaultValue();
        var publicKeyPem = InternalTokenKeys.DerivePublicKeyPem(privateKeyPem);
        var payload = Encoding.UTF8.GetBytes("internal-token");

        using var signer = ECDsa.Create();
        signer.ImportFromPem(privateKeyPem);
        var signature = signer.SignData(payload, HashAlgorithmName.SHA256);

        using var verifier = ECDsa.Create();
        verifier.ImportFromPem(publicKeyPem);
        Assert.Equal("PUBLIC KEY", PemLabel(publicKeyPem));
        Assert.DoesNotContain("PRIVATE", publicKeyPem, StringComparison.Ordinal);
        Assert.True(verifier.VerifyData(payload, signature, HashAlgorithmName.SHA256));
    }

    [Fact]
    public void SigningKeysAreUniquePerGeneration()
    {
        var generator = new EcdsaP256PrivateKeyDefault();

        Assert.NotEqual(generator.GetDefaultValue(), generator.GetDefaultValue());
    }

    [Fact]
    public void FingerprintKeyIsBase64OfThirtyTwoBytes()
    {
        var generator = new Base64KeyDefault();

        var key = generator.GetDefaultValue();

        Assert.Equal(32, Convert.FromBase64String(key).Length);
        Assert.NotEqual(key, generator.GetDefaultValue());
    }

    private static string PemLabel(string pem) => pem[PemEncoding.Find(pem).Label];
}
