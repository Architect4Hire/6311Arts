namespace Arts6311.Gateway.Session;

/// <summary>
/// The gateway's Data Protection key ring, which exists for one thing: antiforgery tokens.
/// </summary>
/// <remarks>
/// <para>
/// The default ring is per process and, in a container, in memory. That would make every antiforgery token die
/// with the process and disagree between replicas — a 400 on roughly half the writes behind two instances. The
/// ring therefore lives in Redis, which the gateway already depends on for sessions (ADR 0009, ADR 0024).
/// </para>
/// <para>
/// The application name matches the one the API and Worker use for their blob ring
/// (<c>DataProtectionApplicationName.Value</c>), mirrored here because the gateway cannot reference the Domain.
/// The two rings stay separate — different stores — and nothing is protected by one and unprotected by the other:
/// Identity's email tokens never reach the gateway, and antiforgery never reaches the API.
/// </para>
/// </remarks>
internal static class GatewayDataProtection
{
    public const string ApplicationName = "Arts6311";

    /// <summary>Namespaced like the tickets: Redis is shared with the API's HybridCache L2.</summary>
    public const string KeyRingKey = "6311:dataprotection:gateway-keys";
}
