namespace Arts6311.Domain.Managers.Catalog;

/// <summary>
/// The error codes the catalog's shared rules raise. Part of the API contract; each has its status in
/// <c>ApiProblemDetails</c> (<c>api-contract.md</c>).
/// </summary>
public static class CatalogErrorCodes
{
    /// <summary>
    /// Another record in the CMS — of this type or another — already uses this SKU (ADR 0032, 0037). 409: it is a
    /// state the editor resolves by choosing a different one.
    /// </summary>
    public const string SkuTaken = "cms.sku_taken";
}
