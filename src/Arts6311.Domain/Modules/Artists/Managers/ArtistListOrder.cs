using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Artists.Managers;

/// <summary>The roster's sortable columns (ADR 0062): every column the CMS table shows. By name unless asked.</summary>
internal static class ArtistListOrder
{
    public static readonly ListOrder<Artist, ArtistSummaryServiceModel> Columns =
        new ListOrder<Artist, ArtistSummaryServiceModel>(artist => artist.Id, row => row.Id, ListSort.Ascending("name"))
            .By("name", artist => artist.Name, row => row.Name)
            .By("medium", artist => artist.Medium, row => row.Medium)
            .By("status", artist => artist.Status, row => row.Status)
            .By("showing", artist => artist.Active, row => row.Active)
            .By("updated", artist => artist.UpdatedAt, row => row.UpdatedAt);
}
