using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Modules.Shop.Facade;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Cms;

/// <summary>
/// The shop's CSV import (ADR 0053): a spreadsheet of new pieces, previewed and then created as Drafts, all of them
/// or none. Nothing here reaches Square or the site: a Draft is pushed and shown only once someone publishes it.
/// </summary>
/// <remarks>
/// The file travels as text in a JSON body, up to <see cref="ShopPolicy.ImportMaxRequestBytes"/> — more than the
/// 1 MiB every other route allows, so the gateway's <c>cms-shop-import</c> route raises its own limit to match.
/// </remarks>
[ApiController]
[Route("api/v1/cms/shop/import")]
[Authorize(Policy = ApiPolicies.CmsEditor)]
public sealed class CmsShopImportController(IShopImportFacade facade) : ControllerBase
{
    /// <summary>
    /// What the file would create, row by row, with every row's problems. Writes nothing. A file the import can't
    /// read at all — an unknown column, no rows, more than a thousand — is a 400 under <c>csv</c>.
    /// </summary>
    [HttpPost("preview")]
    [RequestSizeLimit(ShopPolicy.ImportMaxRequestBytes)]
    [ProducesResponseType<ShopImportPreviewServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> PreviewAsync(
        [FromBody] ShopImportViewModel import, CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.PreviewAsync(import, cancellationToken));

    /// <summary>
    /// A Draft for every row, in one save, or nothing. Requires an <c>Idempotency-Key</c> header: sent again with
    /// the same key and file, it answers what the first request created (<c>Idempotent-Replayed: true</c>) rather
    /// than creating it twice. A row with a problem is a 400 keyed <c>rows[n].column</c>, n the spreadsheet row.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(ShopPolicy.ImportMaxRequestBytes)]
    [ProducesResponseType<ShopImportResultServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ImportAsync(
        [FromBody] ShopImportViewModel import,
        [FromHeader(Name = IdempotencyPolicy.KeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Bound so the contract declares the header; read through IdempotencyKey(), where several values count as
        // none rather than the comma-joined string binding would make of them (api-contract.md).
        _ = idempotencyKey;
        return this.ToActionResult(await facade.ImportAsync(import, this.IdempotencyKey(), User.UserId(), cancellationToken));
    }
}
