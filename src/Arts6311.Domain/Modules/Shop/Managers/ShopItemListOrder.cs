using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Shop.Data.Entities;

namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>
/// The artwork list's sortable columns (ADR 0062): every column the CMS table shows. By title unless asked.
/// </summary>
/// <remarks>
/// "artist" orders by the credited artist's name, read through the item's foreign key from the artists the query
/// passes (read-only, in the ORDER BY, its keyset and the cursor's value; the name shown still comes from the Artists
/// facade). A deleted artist reads as none, as the facade answers. "square" orders by the figure that column leads
/// with, Square's price; an item Square has not priced sorts last either way round.
/// </remarks>
internal static class ShopItemListOrder
{
    public static readonly ListOrder<ShopItem, ShopItemListRow> Columns =
        new ListOrder<ShopItem, ShopItemListRow>(item => item.Id, row => row.Id, ListSort.Ascending("title"))
            .By("title", item => item.Title, row => row.Title)
            .By<IQueryable<Artist>, string?>(
                "artist",
                (item, artists) => artists
                    .Where(artist => artist.Id == item.ArtistId)
                    .Select(artist => (string?)artist.Name)
                    .FirstOrDefault(),
                row => row.ArtistName)
            .By("status", item => item.Status, row => row.Status)
            .By("square", item => item.PriceCents, row => row.PriceCents)
            .By("updated", item => item.UpdatedAt, row => row.UpdatedAt);
}
