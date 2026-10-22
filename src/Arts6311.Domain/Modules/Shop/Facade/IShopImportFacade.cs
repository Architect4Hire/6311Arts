using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Shop.Facade;

/// <summary>
/// The shop's CSV import (ADR 0053): a spreadsheet of new pieces, previewed row by row, then created as Drafts all
/// together or not at all. The CMS's <c>/cms/shop/import</c> routes are its only callers.
/// </summary>
/// <remarks>
/// A facade of its own rather than more of <see cref="IShopFacade"/>, because the import is an idempotent command
/// and <see cref="IIdempotentCommandExecutor"/> exists only in the API. The Worker and MigrationService compose the
/// Shop module too, and must not need it.
/// </remarks>
public interface IShopImportFacade
{
    /// <summary>
    /// What the file would create, every row with its problems. Writes nothing. Refusal: <c>validation.failed</c>
    /// for the file itself — a header the import doesn't know, no rows, too many — under <c>csv</c>.
    /// </summary>
    Task<OperationResult<ShopImportPreviewServiceModel>> PreviewAsync(
        ShopImportViewModel import, CancellationToken cancellationToken);

    /// <summary>
    /// A Draft for every row, in one save, or none. Requires an <c>Idempotency-Key</c>: a retried request returns
    /// what the first one created instead of creating it twice. Refusals: <c>idempotency.*</c>,
    /// <c>validation.failed</c> (the file under <c>csv</c>, a row under <c>rows[n].column</c>),
    /// <c>cms.slug_taken</c>, <c>cms.sku_taken</c>.
    /// </summary>
    Task<IdempotentOutcome<ShopImportResultServiceModel>> ImportAsync(
        ShopImportViewModel import, string? idempotencyKey, string actorUserId, CancellationToken cancellationToken);
}
