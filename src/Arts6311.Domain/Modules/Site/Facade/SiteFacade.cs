using Arts6311.Domain.Managers.Caching;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Exhibitions.Facade;
using Arts6311.Domain.Modules.Instructors.Facade;
using Arts6311.Domain.Modules.Memberships.Facade;
using Arts6311.Domain.Modules.Pages.Facade;
using Arts6311.Domain.Modules.Shop.Facade;
using Arts6311.Domain.Modules.Site.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Site.Facade;

/// <summary>
/// Composes the site reads no one content module can answer alone, asking each module's facade and caching what it
/// composes (ADR 0042).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a module of its own.</strong> Shop already asks Artists who its credits are, so Artists asking Shop
/// for a work count would make the two depend on each other; and the sitemap needs every type, which only a module
/// that knows them all may ask (as <c>Modules/Preview</c> does, ADR 0036). A module reaches another only through its
/// facade and ServiceModels (backend.md), which is all this does.
/// </para>
/// <para>
/// <strong>It has no Business or Data layer</strong>, like Preview and Operations: there is no rule to own and nothing
/// to persist. Whether a record is published is each module's own rule, already applied by the facade it asks.
/// </para>
/// </remarks>
internal sealed class SiteFacade(
    IArtistsFacade artists,
    IInstructorsFacade instructors,
    IShopFacade shop,
    IExhibitionsFacade exhibitions,
    IClassesFacade classes,
    IMembershipsFacade memberships,
    IPagesFacade pages,
    IAppCache cache) : ISiteFacade
{
    internal static readonly string DirectoryKey = CacheKeys.List(SiteContentTypes.Artist, "site");

    internal static readonly string SitemapKey = CacheKeys.List("sitemap", "site");

    /// <summary>The directory and an artist's page show artists and their work, so a change to either clears them.</summary>
    internal static readonly string[] ArtistTags =
        [CacheTags.Type(SiteContentTypes.Artist), CacheTags.Type(SiteContentTypes.ShopItem)];

    /// <summary>The sitemap names every type's records, so any content change clears it.</summary>
    internal static readonly string[] SitemapTags = [.. SiteContentTypes.All.Select(CacheTags.Type)];

    public async Task<OperationResult<IReadOnlyList<SiteArtistSummaryServiceModel>>> ListArtistsAsync(
        CancellationToken cancellationToken) =>
        OperationResult.Success(await cache.GetOrCreateAsync<IReadOnlyList<SiteArtistSummaryServiceModel>>(
            DirectoryKey,
            async token =>
            {
                var roster = await artists.ListForSiteAsync(token);
                var counts = await shop.CountForSiteByArtistAsync(token);
                return
                [
                    .. roster.Select(artist => new SiteArtistSummaryServiceModel(
                        artist.Id,
                        artist.Slug,
                        artist.Name,
                        artist.Medium,
                        artist.Active,
                        artist.Portrait,
                        counts.GetValueOrDefault(artist.Id))),
                ];
            },
            ArtistTags,
            SiteCachePolicy.Lifetime,
            cancellationToken));

    public async Task<OperationResult<SiteArtistPageServiceModel>> GetArtistAsync(string slug, CancellationToken cancellationToken)
    {
        // A malformed slug can't name an artist, and must not become a cache key (CacheKeys refuses a ':').
        if (!Slugs.IsValid(slug))
        {
            return ArtistNotFound();
        }

        var page = await cache.GetOrCreateFoundAsync(
            CacheKeys.Item(SiteContentTypes.Artist, slug),
            async token =>
            {
                if (await artists.GetForSiteAsync(slug, token) is not { } artist)
                {
                    return null;
                }

                // The shop's own list, filtered to them: its next cursor continues at /site/shop?artist={slug}.
                return new SiteArtistPageServiceModel(artist, await shop.ListWorksForSiteAsync(slug, token));
            },
            ArtistTags,
            SiteCachePolicy.Lifetime,
            cancellationToken);

        return page is null ? ArtistNotFound() : OperationResult.Success(page);
    }

    public async Task<OperationResult<IReadOnlyList<SitemapEntryServiceModel>>> ListSitemapAsync(
        CancellationToken cancellationToken) =>
        OperationResult.Success(await cache.GetOrCreateAsync<IReadOnlyList<SitemapEntryServiceModel>>(
            SitemapKey,
            async token =>
            [
                .. await pages.ListSitemapAsync(token),
                .. await classes.ListSitemapAsync(token),
                .. await exhibitions.ListSitemapAsync(token),
                .. await artists.ListSitemapAsync(token),
                .. await instructors.ListSitemapAsync(token),
                .. await shop.ListSitemapAsync(token),
                .. await memberships.ListSitemapAsync(token),
            ],
            SitemapTags,
            SiteCachePolicy.Lifetime,
            cancellationToken));

    private static OperationResult<SiteArtistPageServiceModel> ArtistNotFound() =>
        OperationResult.Failure<SiteArtistPageServiceModel>(OperationError.Of(ErrorCodes.NotFound, "There is no such artist."));
}
