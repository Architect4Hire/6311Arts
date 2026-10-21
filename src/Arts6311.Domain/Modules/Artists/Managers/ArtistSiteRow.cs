using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Artists.Managers;

/// <summary>
/// One artist as Business hands it to the facade for the site: everything in <see cref="SiteArtistServiceModel"/>
/// except the portrait, which only Media can describe (backend.md: cross-module reads go facade to facade), plus
/// <see cref="UpdatedAt"/> for the sitemap.
/// </summary>
internal sealed record ArtistSiteRow(
    Guid Id,
    string Slug,
    string Name,
    string? Medium,
    bool Active,
    Guid? PortraitMediaId,
    string? Bio,
    DateTimeOffset UpdatedAt)
{
    public SiteArtistServiceModel ToServiceModel(SiteImageServiceModel? portrait) =>
        new(Id, Slug, Name, Medium, Active, portrait, Bio);
}
