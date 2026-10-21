using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Instructors.Managers;

/// <summary>
/// One instructor as Business hands it to the facade for the site: everything in
/// <see cref="SiteInstructorServiceModel"/> except the portrait, which only Media can describe, and the artist page,
/// which only Artists can (backend.md: cross-module reads go facade to facade), plus <see cref="UpdatedAt"/> for the
/// sitemap.
/// </summary>
internal sealed record InstructorSiteRow(
    Guid Id,
    string Slug,
    string Name,
    string? Medium,
    bool Active,
    Guid? PortraitMediaId,
    string? Bio,
    string? Website,
    string? Instagram,
    Guid? ArtistId,
    DateTimeOffset UpdatedAt)
{
    /// <param name="portrait">The portrait's renditions, or <c>null</c>.</param>
    /// <param name="artist">How Artists credits the linked artist; its slug is <c>null</c> unless they are published.</param>
    public SiteInstructorServiceModel ToServiceModel(SiteImageServiceModel? portrait, SiteArtistCreditServiceModel? artist) =>
        new(Id, Slug, Name, Medium, Active, portrait, Bio, Website, Instagram, artist?.Slug);
}
