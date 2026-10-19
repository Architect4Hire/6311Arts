using System.Security.Cryptography;

namespace Arts6311.Api.Authentication;

/// <summary>
/// The public half of the gateway's ES256 signing pair, plus the issuer, audience and maximum lifetime the API
/// enforces (ADR 0002). The AppHost derives the public key from the generated
/// <c>internal-token-signing-key</c> parameter and passes it as <c>InternalToken__PublicKeyPem</c>; the private
/// half never leaves the gateway.
/// </summary>
internal sealed class InternalTokenOptions
{
    public const string SectionName = "InternalToken";

    /// <summary>
    /// SubjectPublicKeyInfo PEM for the gateway's P-256 public key. Not a secret, but required: without it the API
    /// could verify nothing, so a missing value stops the host instead of failing every request.
    /// </summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    public string Issuer { get; set; } = InternalTokenDefaults.Issuer;

    public string Audience { get; set; } = InternalTokenDefaults.Audience;

    public TimeSpan MaxLifetime { get; set; } = InternalTokenDefaults.MaxLifetime;

    /// <summary>
    /// Imports <see cref="PublicKeyPem"/>, or throws with the reason. Called once at startup, so a bad value is a
    /// startup failure rather than a 401 on every request.
    /// </summary>
    /// <remarks>
    /// A PKCS#8 <c>PRIVATE KEY</c> PEM is refused rather than imported. <see cref="ECDsa.ImportFromPem"/> accepts
    /// one happily, and the API would then hold signing material it has no business holding — the exact mistake
    /// this check exists to catch, since the gateway's parameter *is* a private key PEM.
    /// </remarks>
    public ECDsa ImportPublicKey()
    {
        if (string.IsNullOrWhiteSpace(PublicKeyPem))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(PublicKeyPem)} is required. The AppHost passes it as " +
                "InternalToken__PublicKeyPem, derived from the internal-token-signing-key parameter (ADR 0002).");
        }

        if (PublicKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(PublicKeyPem)} is a private key. The API verifies and must only ever hold " +
                "the public half; the private key belongs to the gateway alone (ADR 0002).");
        }

        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(PublicKeyPem);
        }
        catch (ArgumentException exception)
        {
            ecdsa.Dispose();
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(PublicKeyPem)} is not a readable SubjectPublicKeyInfo PEM.", exception);
        }
        catch (CryptographicException exception)
        {
            ecdsa.Dispose();
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(PublicKeyPem)} is not a readable SubjectPublicKeyInfo PEM.", exception);
        }

        // The curve, not just the size: brainpoolP256r1 and secp256k1 are also 256-bit, and neither is ES256's
        // curve. A key on one of those would pass a size check and then fail every signature verification.
        var curve = ecdsa.ExportParameters(includePrivateParameters: false).Curve;
        if (!IsNistP256(curve))
        {
            var described = curve.Oid?.FriendlyName ?? curve.Oid?.Value ?? $"{ecdsa.KeySize}-bit";
            ecdsa.Dispose();
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(PublicKeyPem)} is on curve '{described}'; ES256 requires P-256 (ADR 0002).");
        }

        return ecdsa;
    }

    /// <summary>
    /// P-256, by OID. A named curve imported from a PEM comes back either named or explicit depending on the
    /// platform, so both forms are checked rather than assuming one.
    /// </summary>
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

        // Explicit parameters: compare the prime field's order, which is P-256's and nothing else's.
        return curve.IsPrime
            && curve.Prime is { Length: 32 }
            && curve.Prime.SequenceEqual(NistP256Prime);
    }

    /// <summary>P-256's field prime, for the case where a platform hands back explicit curve parameters.</summary>
    private static readonly byte[] NistP256Prime =
    [
        0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    ];
}
