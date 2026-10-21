using Arts6311.Domain.Modules.Forms.Managers;

namespace Arts6311.Domain.Modules.Forms.Data.Entities;

/// <summary>An address on the newsletter list, with the consent that put it there (ADR 0047).</summary>
public sealed class NewsletterSubscriber
{
    public Guid Id { get; set; }

    /// <summary>Trimmed and lower-cased, so one person is one row. Unique.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The latest time they agreed: signing up again refreshes it.</summary>
    public DateTimeOffset ConsentedAt { get; set; }

    /// <summary>Which wording they agreed to (<see cref="FormsPolicy.NewsletterConsentVersion"/>).</summary>
    public string ConsentVersion { get; set; } = string.Empty;

    public NewsletterSource Source { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
