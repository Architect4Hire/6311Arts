using System.Text.Json.Serialization;

namespace Arts6311.Domain.Modules.Forms.Managers;

/// <summary>Where an artist submission stands in the inbox. Stored as its number, so the values never change.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubmissionStatus>))]
public enum SubmissionStatus
{
    /// <summary>Arrived; nobody has looked yet.</summary>
    New = 0,

    Reviewed = 10,

    Accepted = 20,

    Declined = 30,
}

/// <summary>How an address joined the newsletter list.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NewsletterSource>))]
public enum NewsletterSource
{
    /// <summary>The sign-up on the site.</summary>
    Signup = 0,

    /// <summary>The box on the artist submission form.</summary>
    Submission = 10,
}
