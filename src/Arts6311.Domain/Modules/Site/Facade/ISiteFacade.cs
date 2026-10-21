using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Site.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Site.Facade;

/// <summary>
/// The site reads that show more than one module's records (ADR 0042): the artist directory and an artist's page,
/// which need Shop's work beside Artists' roster, and the sitemap, which needs every content type.
/// </summary>
public interface ISiteFacade
{
    /// <summary>
    /// <c>GET /site/artists</c>: every published artist, by name, with their work count, cached under the
    /// <c>artist</c> and <c>shop-item</c> type tags. A roster, so not paged.
    /// </summary>
    Task<OperationResult<IReadOnlyList<SiteArtistSummaryServiceModel>>> ListArtistsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /site/artists/{slug}</c>: the published artist there and the first page of their work, cached as the
    /// directory is. Anything else is <c>resource.not_found</c>, and is not cached.
    /// </summary>
    Task<OperationResult<SiteArtistPageServiceModel>> GetArtistAsync(string slug, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /site/sitemap</c>: every published record's address, cached under every content type's tag. Previews,
    /// drafts and redirects never appear.
    /// </summary>
    Task<OperationResult<IReadOnlyList<SitemapEntryServiceModel>>> ListSitemapAsync(CancellationToken cancellationToken);
}
