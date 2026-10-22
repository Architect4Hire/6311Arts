using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Business;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Shop.Facade;

/// <summary>
/// Validates the request's shape, reads the file into rows, asks Artists who the <c>artist</c> column names, then
/// calls Business — once for a preview, and once inside the idempotent command for the import (ADR 0053).
/// </summary>
/// <remarks>
/// Artists is asked before the command starts, never inside it: the executor owns the request's DbContext from then
/// on, and the answer is a read that a retried attempt can reuse. Business re-reads everything else it checks —
/// slugs, SKUs, item numbers — inside the command, so a retry checks against the database as it is then.
/// </remarks>
internal sealed class ShopImportFacade(
    IShopBusiness business,
    IArtistsFacade artists,
    IValidator<ShopImportViewModel> validator,
    IIdempotentCommandExecutor executor) : IShopImportFacade
{
    /// <summary>The idempotent command's stable name (<see cref="IdempotentCommand.Operation"/>).</summary>
    internal const string Operation = "shop.import";

    /// <summary>
    /// How many references one question to the Artists facade carries: comfortably under the most it takes at once
    /// (<c>ArtistPolicy.LookupMaxCount</c>), which is that module's to change.
    /// </summary>
    private const int ArtistLookupChunk = 100;

    public async Task<OperationResult<ShopImportPreviewServiceModel>> PreviewAsync(
        ShopImportViewModel import, CancellationToken cancellationToken)
    {
        var read = await ReadAsync(import, cancellationToken);
        if (!read.Succeeded)
        {
            return OperationResult.Failure<ShopImportPreviewServiceModel>(read.Error!);
        }

        var (sheet, check) = read.Value!;
        return OperationResult.Success(await business.PreviewImportAsync(sheet, check, cancellationToken));
    }

    public async Task<IdempotentOutcome<ShopImportResultServiceModel>> ImportAsync(
        ShopImportViewModel import, string? idempotencyKey, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorUserId);

        var read = await ReadAsync(import, cancellationToken);
        if (!read.Succeeded)
        {
            return new IdempotentOutcome<ShopImportResultServiceModel>(
                OperationResult.Failure<ShopImportResultServiceModel>(read.Error!), Replayed: false);
        }

        var (sheet, check) = read.Value!;

        // The file is the request: the same key with a different file is refused (idempotency.key_reused), not
        // replayed. The file name is only for the audit line, so it is not part of what makes two requests the same.
        return await executor.ExecuteAsync(
            new IdempotentCommand($"user:{actorUserId}", Operation, idempotencyKey, new { import.Csv }),
            token => business.ImportAsync(sheet, check, import.FileName, actorUserId, token),
            cancellationToken);
    }

    /// <summary>The request's shape checked, the file read into rows, and Artists' answer about the artist column.</summary>
    private async Task<OperationResult<(ShopImportSheet Sheet, ShopImportArtistCheck Artists)>> ReadAsync(
        ShopImportViewModel import, CancellationToken cancellationToken)
    {
        if (await validator.ValidateOrErrorAsync(import, cancellationToken) is { } error)
        {
            return OperationResult.Failure<(ShopImportSheet, ShopImportArtistCheck)>(error);
        }

        var sheet = ShopImportSheet.Read(import.Csv);
        if (!sheet.Succeeded)
        {
            return OperationResult.Failure<(ShopImportSheet, ShopImportArtistCheck)>(sheet.Error!);
        }

        var check = await AskArtistsAsync(
            [.. sheet.Value!.Lines.Select(line => line[ShopImportColumns.Artist]).Where(reference => reference.Length > 0)],
            cancellationToken);
        return OperationResult.Success((sheet.Value, check));
    }

    /// <summary>
    /// Who each reference could mean: the artist whose slug it is, or failing that every artist with that name, case
    /// aside. Asked a chunk at a time, as the Artists facade requires.
    /// </summary>
    private async Task<ShopImportArtistCheck> AskArtistsAsync(
        IReadOnlyList<string> references, CancellationToken cancellationToken)
    {
        var distinct = references.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0)
        {
            return ShopImportArtistCheck.None;
        }

        var slugs = distinct.Select(reference => reference.ToLowerInvariant()).Where(Slugs.IsValid).Distinct(StringComparer.Ordinal).ToList();
        var bySlug = new Dictionary<string, ArtistSummaryServiceModel>(StringComparer.Ordinal);
        foreach (var chunk in slugs.Chunk(ArtistLookupChunk))
        {
            foreach (var artist in await artists.FindBySlugsAsync(chunk, cancellationToken))
            {
                bySlug[artist.Slug] = artist;
            }
        }

        var byName = new List<ArtistSummaryServiceModel>();
        var names = distinct.Where(reference => !bySlug.ContainsKey(reference.ToLowerInvariant())).ToList();
        foreach (var chunk in names.Chunk(ArtistLookupChunk))
        {
            byName.AddRange(await artists.FindByNamesAsync(chunk, cancellationToken));
        }

        var matches = new Dictionary<string, IReadOnlyList<ShopItemArtistServiceModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in distinct)
        {
            matches[reference] = bySlug.TryGetValue(reference.ToLowerInvariant(), out var artist)
                ? [Credit(artist)]
                : [.. byName
                    .Where(candidate => string.Equals(candidate.Name.Trim(), reference, StringComparison.OrdinalIgnoreCase))
                    .Select(Credit)];
        }

        return new ShopImportArtistCheck(matches);
    }

    private static ShopItemArtistServiceModel Credit(ArtistSummaryServiceModel artist) =>
        new(artist.Id, artist.Name, artist.Slug, artist.Status);
}
