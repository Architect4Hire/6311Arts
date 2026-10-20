using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Exhibitions.Managers;

/// <summary>
/// One exhibition as Business hands it to the facade: everything in <see cref="ExhibitionServiceModel"/> except who
/// the credited artists are, which only the Artists module can say. The facade asks it and finishes the model with
/// <see cref="ToServiceModel"/> (backend.md: cross-module reads go facade to facade).
/// </summary>
/// <param name="ArtistIds">The credits, in order.</param>
internal sealed record ExhibitionDetail(
    Guid Id,
    string Title,
    string Slug,
    string? Summary,
    string? Body,
    DateOnly StartDate,
    DateOnly EndDate,
    ExhibitionReceptionServiceModel? Reception,
    Guid? HeroMediaId,
    IReadOnlyList<Guid> ArtistIds,
    PublishStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledFor,
    long? LegacyWordPressId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    string ETag)
{
    /// <summary>
    /// The finished model, with each credit described by <paramref name="artists"/> in this exhibition's order. A credit
    /// Artists did not describe is left out; the <c>Restrict</c> foreign key means that cannot happen today.
    /// </summary>
    public ExhibitionServiceModel ToServiceModel(IReadOnlyDictionary<Guid, ExhibitionArtistServiceModel> artists)
    {
        ArgumentNullException.ThrowIfNull(artists);

        return new ExhibitionServiceModel(
            Id,
            Title,
            Slug,
            Summary,
            Body,
            StartDate,
            EndDate,
            Reception,
            GalleryTimeZone.Id,
            HeroMediaId,
            [.. ArtistIds.Where(artists.ContainsKey).Select(id => artists[id])],
            Status,
            PublishedAt,
            ScheduledFor,
            SlugLocked: PublishedAt is not null,
            LegacyWordPressId,
            CreatedAt,
            UpdatedAt,
            UpdatedBy,
            ETag);
    }
}
