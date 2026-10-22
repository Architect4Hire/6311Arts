using Arts6311.Domain.Modules.Shop.Data.Entities;

namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>One item a CSV import built, and the audit line that says which file and row it came from (ADR 0053).</summary>
internal sealed record ShopImportedItem(ShopItem Item, string Summary);
