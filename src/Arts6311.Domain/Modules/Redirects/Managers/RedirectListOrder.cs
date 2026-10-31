using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Redirects.Data.Entities;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Redirects.Managers;

/// <summary>
/// The redirects list's sortable columns (ADR 0062): every column the CMS table shows. By old address unless asked;
/// "type" is the status code (301 before 302) and "source" who made it.
/// </summary>
internal static class RedirectListOrder
{
    public static readonly ListOrder<Redirect, RedirectServiceModel> Columns =
        new ListOrder<Redirect, RedirectServiceModel>(redirect => redirect.Id, row => row.Id, ListSort.Ascending("from"))
            .By("from", redirect => redirect.FromPath, row => row.FromPath)
            .By("to", redirect => redirect.ToPath, row => row.ToPath)
            .By("type", redirect => redirect.StatusCode, row => row.StatusCode)
            .By("source", redirect => redirect.Source, row => row.Source);
}
