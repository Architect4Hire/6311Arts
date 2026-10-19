using System.Security.Cryptography;
using Aspire.Hosting.Publishing;

namespace Arts6311.AppHost.Parameters;

/// <summary>
/// Generates random key material, base64-encoded: the idempotency fingerprint HMAC key, which the API and
/// Worker require to be base64 of at least 32 bytes. Ported from creatorpantry.
/// </summary>
/// <remarks>
/// Not <see cref="GenerateParameterDefault"/>: that produces a 22-character password drawn from letters,
/// digits and punctuation, which is neither base64 nor 32 bytes.
/// </remarks>
internal sealed class Base64KeyDefault(int byteLength = 32) : ParameterDefault
{
    public override string GetDefaultValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength));

    public override void WriteToManifest(ManifestPublishingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Writer.WriteStartObject("generate");
        context.Writer.WriteString("type", "base64-key");
        context.Writer.WriteNumber("byteLength", byteLength);
        context.Writer.WriteEndObject();
    }
}
