using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Exhibitions.Managers;

/// <summary>
/// One exhibition as Business hands it to the facade for the site: everything the site's models carry except who
/// the credited artists are and what the hero image is, which only Artists and Media can say (backend.md:
/// cross-module reads go facade to facade).
/// </summary>
internal sealed record ExhibitionSiteRow(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string? Body,
    DateOnly StartDate,
    DateOnly EndDate,
    ExhibitionReceptionServiceModel? Reception,
    Guid? HeroMediaId,
    IReadOnlyList<Guid> ArtistIds,
    DateTimeOffset UpdatedAt)
{
    public SiteExhibitionSummaryServiceModel ToSummary(
        IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel> credits, IReadOnlyDictionary<Guid, SiteImageServiceModel> images) =>
        new(Id, Slug, Title, Summary, StartDate, EndDate, Reception, GalleryTimeZone.Id, Credits(credits), Hero(images));

    public SiteExhibitionServiceModel ToServiceModel(
        IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel> credits, IReadOnlyDictionary<Guid, SiteImageServiceModel> images) =>
        new(Id, Slug, Title, Summary, Body, StartDate, EndDate, Reception, GalleryTimeZone.Id, Credits(credits), Hero(images));

    /// <summary>In the order staff set. A credit Artists did not describe is left out; the <c>Restrict</c> key means that cannot happen today.</summary>
    private List<SiteArtistCreditServiceModel> Credits(IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel> credits) =>
        [.. ArtistIds.Where(credits.ContainsKey).Select(id => credits[id])];

    private SiteImageServiceModel? Hero(IReadOnlyDictionary<Guid, SiteImageServiceModel> images) =>
        HeroMediaId is { } id && images.TryGetValue(id, out var image) ? image : null;
}
