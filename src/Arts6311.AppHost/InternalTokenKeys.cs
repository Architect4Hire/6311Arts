using System.Security.Cryptography;

namespace Arts6311.AppHost;

/// <summary>Key handling for the gateway-signed internal token (ADR 0002).</summary>
internal static class InternalTokenKeys
{
    /// <summary>
    /// Derives the SubjectPublicKeyInfo PEM from the PKCS#8 private key, so the API only ever receives the
    /// public half of the pair the gateway signs with.
    /// </summary>
    public static string DerivePublicKeyPem(string privateKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        return ecdsa.ExportSubjectPublicKeyInfoPem();
    }
}
