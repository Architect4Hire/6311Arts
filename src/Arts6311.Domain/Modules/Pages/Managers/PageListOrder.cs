using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Pages.Managers;

/// <summary>The pages list's sortable columns (ADR 0062): every column the CMS table shows. By title unless asked.</summary>
internal static class PageListOrder
{
    public static readonly ListOrder<Page, PageSummaryServiceModel> Columns =
        new ListOrder<Page, PageSummaryServiceModel>(page => page.Id, row => row.Id, ListSort.Ascending("title"))
            .By("title", page => page.Title, row => row.Title)
            .By("status", page => page.Status, row => row.Status)
            .By("updated", page => page.UpdatedAt, row => row.UpdatedAt);
}
