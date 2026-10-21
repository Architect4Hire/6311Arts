using System.Text.Json;
using Arts6311.Domain.Modules.Forms.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Forms.Managers;

/// <summary>
/// What an artist answered beyond their name, address and goals, as it is stored (<c>ArtistSubmission.AnswersJson</c>).
/// Read whole and never queried, so one column rather than thirty; the inbox filters on the columns beside it.
/// </summary>
internal sealed record SubmissionAnswers(
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
    bool Newsletter)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Web);

    /// <summary>The stored answers. A row this code did not write is a fault, so it throws rather than guessing.</summary>
    public static SubmissionAnswers Parse(string json) =>
        JsonSerializer.Deserialize<SubmissionAnswers>(json, Web)
        ?? throw new InvalidOperationException("A submission's stored answers are empty.");

    public SubmissionAnswersServiceModel ToServiceModel() => new(
        Pronouns, Phone, Neighborhood, Zip, Website, Instagram, Medium, OtherMedia, Years, PriceBand, Bio, Statement,
        Sell, Exhibit, Teach, Portfolio, Heard, Newsletter);
}
