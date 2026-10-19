using System.Security.Cryptography;

namespace Arts6311.Gateway.Authentication;

/// <summary>
/// The private half of the ES256 pair, which exists only here (ADR 0002). The AppHost passes its generated
/// <c>internal-token-signing-key</c> parameter as <c>InternalToken__SigningKeyPem</c>; in Azure it is a Key Vault
/// reference.
/// </summary>
internal sealed class InternalTokenOptions
{
    public const string SectionName = "InternalToken";

    /// <summary>
    /// PKCS#8 PEM for the gateway's P-256 private key. A secret: it is never logged, never forwarded and never
    /// given to the API, which holds only the public half derived from it.
    /// </summary>
    public string SigningKeyPem { get; set; } = string.Empty;

    /// <summary>
    /// Imports <see cref="SigningKeyPem"/>, or throws with the reason. Called once at startup, so a bad key stops
    /// the host rather than failing every proxied request with an unsigned or unverifiable token.
    /// </summary>
    /// <remarks>
    /// The mirror of the API's check: it refuses a private key, this refuses a public one. Handing the gateway only
    /// the public half would leave it unable to sign anything, and the failure would otherwise look like an API
    /// problem.
    /// </remarks>
    public ECDsa ImportSigningKey()
    {
        if (string.IsNullOrWhiteSpace(SigningKeyPem))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(SigningKeyPem)} is required. The AppHost passes it as " +
                "InternalToken__SigningKeyPem, from the internal-token-signing-key parameter (ADR 0002).");
        }

        if (!SigningKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(SigningKeyPem)} is not a private key. The gateway signs, so it needs the " +
                "private half of the pair; the API is the side that holds only the public key (ADR 0002).");
        }

        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(SigningKeyPem);
        }
        catch (ArgumentException exception)
        {
            ecdsa.Dispose();
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(SigningKeyPem)} is not a readable PKCS#8 PEM.", exception);
        }
        catch (CryptographicException exception)
        {
            ecdsa.Dispose();
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(SigningKeyPem)} is not a readable PKCS#8 PEM.", exception);
        }

        // The curve by OID, not the key size: brainpoolP256r1 and secp256k1 are also 256-bit, and a token signed
        // with one would be rejected by the API on every request with nothing to point at the cause.
        var curve = ecdsa.ExportParameters(includePrivateParameters: false).Curve;
        if (!IsNistP256(curve))
        {
            var described = curve.Oid?.FriendlyName ?? curve.Oid?.Value ?? $"{ecdsa.KeySize}-bit";
            ecdsa.Dispose();
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(SigningKeyPem)} is on curve '{described}'; ES256 requires P-256 (ADR 0002).");
        }

        return ecdsa;
    }

    private static bool IsNistP256(ECCurve curve)
    {
        const string NistP256Oid = "1.2.840.10045.3.1.7";

        if (curve.IsNamed)
        {
            return curve.Oid.Value == NistP256Oid
                || string.Equals(curve.Oid.FriendlyName, "nistP256", StringComparison.OrdinalIgnoreCase)
                || string.Equals(curve.Oid.FriendlyName, "ECDSA_P256", StringComparison.OrdinalIgnoreCase)
                || string.Equals(curve.Oid.FriendlyName, "prime256v1", StringComparison.OrdinalIgnoreCase);
        }

        return curve.IsPrime && curve.Prime is { Length: 32 } && curve.Prime.SequenceEqual(NistP256Prime);
    }

    private static readonly byte[] NistP256Prime =
    [
        0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    ];
}
