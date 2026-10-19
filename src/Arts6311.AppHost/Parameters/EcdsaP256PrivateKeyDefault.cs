using System.Security.Cryptography;
using Aspire.Hosting.Publishing;

namespace Arts6311.AppHost.Parameters;

/// <summary>
/// Generates a random ECDSA P-256 private key, PKCS#8 PEM-encoded: the gateway's internal-token signing key
/// (ADR 0002, creatorpantry B-13). Ported from creatorpantry.
/// </summary>
/// <remarks>
/// Only the private half is generated. The public half the API verifies with is derived from it at AppHost
/// startup (<see cref="InternalTokenKeys.DerivePublicKeyPem"/>), never generated as a second parameter: two
/// independently generated halves could drift into a mismatched pair and fail every internal request's
/// signature check with no obvious cause.
/// </remarks>
internal sealed class EcdsaP256PrivateKeyDefault : ParameterDefault
{
    public override string GetDefaultValue()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportPkcs8PrivateKeyPem();
    }

    public override void WriteToManifest(ManifestPublishingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Writer.WriteStartObject("generate");
        context.Writer.WriteString("type", "ecdsa-p256-pkcs8-pem");
        context.Writer.WriteEndObject();
    }
}
