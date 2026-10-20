namespace Arts6311.Domain.Managers.Catalog;

/// <summary>What every priced CMS record shares. The amounts themselves are Square's record (square.md).</summary>
public static class CatalogPolicy
{
    /// <summary>
    /// The currency every amount the API returns is in, as <c>api-contract.md</c> requires beside integer cents.
    /// The centre sells in one currency at one location; 5.1 confirms it against the Square location's own.
    /// </summary>
    public const string Currency = "USD";
}
