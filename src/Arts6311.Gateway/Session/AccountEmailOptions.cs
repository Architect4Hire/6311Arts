namespace Arts6311.Gateway.Session;

/// <summary>
/// The cooldown and hand-off queue behind <c>/bff/forgot-password</c> and <c>/bff/resend-confirmation</c>, from
/// configuration (ADR 0028, 0029).
/// </summary>
internal sealed class AccountEmailOptions
{
    public const string SectionName = "Gateway:AccountEmail";

    /// <summary>
    /// How long after a request for an address the next one of the same kind for it is quietly ignored. This is
    /// what stops one mailbox being flooded; the per-IP rate limit stops one caller flooding many. Behaves
    /// identically for known and unknown addresses, because the gateway does not know which an address is.
    /// </summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Requests waiting for the internal API, both kinds together. Bounded, so a flood the rate limit lets through
    /// cannot grow memory; once full, further requests are dropped and logged, and their senders ask again.
    /// </summary>
    public int QueueCapacity { get; set; } = 256;

    /// <summary>Throws unless every value is usable, at startup, like <c>BrowserSessionOptions.Validate</c>.</summary>
    public void Validate()
    {
        if (Cooldown <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{SectionName}:Cooldown must be greater than zero.");
        }

        if (QueueCapacity <= 0)
        {
            throw new InvalidOperationException($"{SectionName}:QueueCapacity must be greater than zero.");
        }
    }
}
