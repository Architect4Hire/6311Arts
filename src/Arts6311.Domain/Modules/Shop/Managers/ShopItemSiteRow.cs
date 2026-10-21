using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>
/// One shop item as Business hands it to the facade for the site: everything the site's models carry except who
/// the credited artist is and what the photographs are, which only Artists and Media can say (backend.md:
/// cross-module reads go facade to facade).
/// </summary>
/// <param name="ImageMediaIds">The gallery in order; the first leads.</param>
/// <param name="PublishedAt">The first publish, which "newest" orders by.</param>
internal sealed record ShopItemSiteRow(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    Guid? ArtistId,
    IReadOnlyList<string> Categories,
    IReadOnlyList<Guid> ImageMediaIds,
    long? PriceCents,
    bool OneOfAKind,
    bool SoldOut,
    DateTimeOffset? PublishedAt,
    DateTimeOffset UpdatedAt)
{
    public SiteShopItemSummaryServiceModel ToSummary(
        IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel> credits, IReadOnlyDictionary<Guid, SiteImageServiceModel> images) =>
        new(Id, Slug, Title, Credit(credits), Categories, Gallery(images).FirstOrDefault(), PriceCents, CatalogPolicy.Currency,
            OneOfAKind, SoldOut);

    public SiteShopItemServiceModel ToServiceModel(
        IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel> credits, IReadOnlyDictionary<Guid, SiteImageServiceModel> images)
    {
        var gallery = Gallery(images);
        return new(Id, Slug, Title, Description, Credit(credits), Categories, gallery.FirstOrDefault(), gallery, PriceCents,
            CatalogPolicy.Currency, OneOfAKind, SoldOut);
    }

    private SiteArtistCreditServiceModel? Credit(IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel> credits) =>
        ArtistId is { } id && credits.TryGetValue(id, out var credit) ? credit : null;

    private List<SiteImageServiceModel> Gallery(IReadOnlyDictionary<Guid, SiteImageServiceModel> images) =>
        [.. ImageMediaIds.Where(images.ContainsKey).Select(id => images[id])];
}
