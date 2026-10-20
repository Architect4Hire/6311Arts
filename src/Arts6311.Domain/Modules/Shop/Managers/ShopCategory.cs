namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>
/// One category the shop files artwork under: the slug the site filters on and the Square push maps, and the words
/// staff and visitors read (ADR 0038).
/// </summary>
public sealed record ShopCategory(string Slug, string Label);
