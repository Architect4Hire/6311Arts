namespace Arts6311.Domain.Managers.Email;

/// <summary>Where email comes from and where its links point (add-notification).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>The From address. Configuration, never a literal: it differs between a laptop and production.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "6311arts";

    /// <summary>
    /// The public site origin links are built from — the gateway's, since it is the only public endpoint
    /// (ADR 0001). A link never points at the API or at a container hostname.
    /// </summary>
    public string SiteOrigin { get; set; } = string.Empty;

    /// <summary>Throws if a value an email cannot be built without is missing, at startup rather than mid-send.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(FromAddress))
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(FromAddress)} is required.");
        }

        if (!Uri.TryCreate(SiteOrigin, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(SiteOrigin)} must be an absolute URL; links in email are built from it.");
        }
    }
}
