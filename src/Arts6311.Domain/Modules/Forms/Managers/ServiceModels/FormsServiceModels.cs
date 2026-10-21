using System.Text.Json.Serialization;
using Arts6311.Domain.Modules.Forms.Managers;

namespace Arts6311.Domain.Modules.Forms.Managers.ServiceModels;

/// <summary>One row of the submissions inbox.</summary>
/// <param name="Wants">What they are here for: <c>sell</c>, <c>exhibit</c>, <c>teach</c>, <c>hello</c>.</param>
public sealed record SubmissionSummaryServiceModel(
    Guid Id,
    string Name,
    string Email,
    IReadOnlyList<string> Wants,
    SubmissionStatus Status,
    int ImageCount,
    DateTimeOffset ReceivedAt);

/// <summary>One submission as staff read it: everything the artist answered, their pictures, and staff's notes.</summary>
public sealed record SubmissionServiceModel(
    Guid Id,
    string Name,
    string Email,
    IReadOnlyList<string> Wants,
    SubmissionAnswersServiceModel Answers,
    IReadOnlyList<SubmissionImageServiceModel> Images,
    SubmissionStatus Status,
    string? Notes,
    DateTimeOffset ReceivedAt,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>The rest of what the artist answered, as they answered it. A panel they didn't need is <c>null</c>.</summary>
public sealed record SubmissionAnswersServiceModel(
    string? Pronouns,
    string? Phone,
    string? Neighborhood,
    string? Zip,
    string? Website,
    string? Instagram,
    string? Medium,
    string? OtherMedia,
    int? Years,
    string? PriceBand,
    string? Bio,
    string? Statement,
    SubmissionSellServiceModel? Sell,
    SubmissionExhibitServiceModel? Exhibit,
    SubmissionTeachServiceModel? Teach,
    string? Portfolio,
    string? Heard,
    bool Newsletter);

public sealed record SubmissionSellServiceModel(int? PiecesReady, string? Restock);

public sealed record SubmissionExhibitServiceModel(string? ShowTitle, int? WorkCount, string? Largest, string? Window, string? Gallery);

public sealed record SubmissionTeachServiceModel(string? ClassTitle, int? MaxStudents, string? Format, string? Materials, bool? TaughtBefore);

/// <summary>
/// One picture the artist sent. Draft media: the URLs answer only a CMS session (media.md).
/// </summary>
/// <param name="ThumbnailUrl">A small rendition once one exists; <c>null</c> while the Worker is still producing them.</param>
public sealed record SubmissionImageServiceModel(Guid Id, string? FileName, int Width, int Height, string OriginalUrl, string? ThumbnailUrl);

/// <summary>One contact message.</summary>
public sealed record ContactMessageServiceModel(
    Guid Id,
    string Name,
    string Email,
    string Message,
    bool Handled,
    DateTimeOffset ReceivedAt,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>One address on the newsletter list, with what they agreed to and when.</summary>
public sealed record NewsletterSubscriberServiceModel(
    Guid Id,
    string Email,
    DateTimeOffset ConsentedAt,
    string ConsentVersion,
    NewsletterSource Source,
    DateTimeOffset CreatedAt);

/// <summary>The newsletter list as a CSV file, and how many addresses it holds.</summary>
public sealed record NewsletterExportServiceModel(string FileName, string Csv, int Count);
