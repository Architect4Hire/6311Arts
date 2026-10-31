using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Exhibitions.Managers;

/// <summary>
/// The exhibitions list's sortable columns (ADR 0062): every column the CMS table shows. Newest opening first unless
/// asked; "dates" sorts by the opening day. Ties break on the id descending, as this list always has.
/// </summary>
internal static class ExhibitionListOrder
{
    public static readonly ListOrder<Exhibition, ExhibitionSummaryServiceModel> Columns =
        new ListOrder<Exhibition, ExhibitionSummaryServiceModel>(exhibition => exhibition.Id, row => row.Id, ListSort.Descending("dates"), SortDirection.Descending)
            .By("title", exhibition => exhibition.Title, row => row.Title)
            .By("dates", exhibition => exhibition.StartDate, row => row.StartDate)
            .By("status", exhibition => exhibition.Status, row => row.Status)
            .By("artists", exhibition => exhibition.Artists.Count, row => row.ArtistCount)
            .By("updated", exhibition => exhibition.UpdatedAt, row => row.UpdatedAt);
}
