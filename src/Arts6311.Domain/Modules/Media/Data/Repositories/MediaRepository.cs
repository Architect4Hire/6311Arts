using Arts6311.Domain.Modules.Media.Data;
using System.Linq.Expressions;
using System.Reflection;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Media.Data.Repositories;

internal sealed class MediaRepository(ArtsDbContext context) : IMediaRepository
{
    private static readonly MethodInfo AnyReferencingMethod = typeof(MediaRepository)
        .GetMethod(nameof(AnyReferencingAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly string ThumbnailRendition = new MediaRenditionName(400, MediaImageFormat.Webp).ToString();

    private DbSet<MediaAsset> Assets => context.Set<MediaAsset>();

    public void Add(MediaAsset asset) => Assets.Add(asset);

    public void Remove(MediaAsset asset) => Assets.Remove(asset);

    public Task<MediaAsset?> FindOriginalAsync(Guid id, CancellationToken cancellationToken) =>
        Assets.FirstOrDefaultAsync(asset => asset.Id == id && asset.Kind != MediaAssetKind.Derived, cancellationToken);

    public Task<List<MediaAsset>> GetRenditionsAsync(Guid originalId, CancellationToken cancellationToken) =>
        Assets.Where(asset => asset.ParentAssetId == originalId).ToListAsync(cancellationToken);

    // Two pools: a staff upload is checked against the library, a submitted picture against other submitted ones, so a
    // visitor can never link (and so pin against deletion) a library picture by sending its bytes (ADR 0047).
    public async Task<Guid?> FindOriginalByChecksumAsync(string checksum, bool includeSubmissions, CancellationToken cancellationToken) =>
        await Assets.AsNoTracking()
            .Where(asset => asset.Checksum == checksum && asset.Kind != MediaAssetKind.Derived
                && (includeSubmissions ? asset.Kind == MediaAssetKind.Submission : asset.Kind != MediaAssetKind.Submission))
            .Select(asset => (Guid?)asset.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<List<MediaAssetSummaryServiceModel>> ListOriginalsAsync(
        MediaListPosition? after, int take, bool missingAltText, MediaProcessingStatus? status, CancellationToken cancellationToken)
    {
        // Submitted pictures are an applicant's, seen from their submission, not the library's (ADR 0047).
        var query = Assets.AsNoTracking().Where(asset => asset.Kind != MediaAssetKind.Derived && asset.Kind != MediaAssetKind.Submission);

        if (missingAltText)
        {
            query = query.Where(asset => asset.AltText == null);
        }

        if (status is { } wanted)
        {
            query = query.Where(asset => asset.ProcessingStatus == wanted);
        }

        if (after is not null)
        {
            query = query.Where(asset => asset.UploadedAt < after.UploadedAt
                || (asset.UploadedAt == after.UploadedAt && asset.Id.CompareTo(after.Id) < 0));
        }

        var rows = await query
            .OrderByDescending(asset => asset.UploadedAt)
            .ThenByDescending(asset => asset.Id)
            .Take(take)
            .Select(asset => new
            {
                asset.Id,
                asset.FileName,
                asset.MimeType,
                asset.Bytes,
                asset.UploadedBytes,
                asset.Width,
                asset.Height,
                asset.ProcessingStatus,
                asset.Visibility,
                asset.AltText,
                asset.Credit,
                asset.UploadedAt,
                Thumbnail = Assets
                    .Where(rendition => rendition.ParentAssetId == asset.Id && rendition.RenditionName == ThumbnailRendition)
                    .Select(rendition => rendition.Checksum)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new MediaAssetSummaryServiceModel(
                row.Id,
                row.FileName,
                row.MimeType,
                row.Bytes,
                row.UploadedBytes,
                row.Width,
                row.Height,
                row.ProcessingStatus,
                row.Visibility,
                row.AltText,
                row.Credit,
                row.UploadedAt,
                row.Thumbnail is null ? null : MediaUrls.For(row.Id, ThumbnailRendition, row.Thumbnail))),
        ];
    }

    public async Task<List<(Guid Id, bool HasAltText)>> FindAttachabilityAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = await Assets.AsNoTracking()
            .Where(asset => ids.Contains(asset.Id) && asset.Kind != MediaAssetKind.Derived)
            .Select(asset => new { asset.Id, HasAltText = asset.AltText != null })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => (row.Id, row.HasAltText))];
    }

    public Task<List<MediaAsset>> FindWithRenditionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Assets.AsNoTracking()
            .Where(asset => (ids.Contains(asset.Id) && asset.Kind != MediaAssetKind.Derived)
                || (asset.ParentAssetId != null && ids.Contains(asset.ParentAssetId.Value)))
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// Read from the model rather than a list of modules, so Media never names the content types that use it: a
    /// module that adds a foreign key to <see cref="MediaAsset"/> is covered by declaring it (ADR 0030). The
    /// foreign key itself is <c>Restrict</c>, so a reference added between this check and the delete still fails
    /// the delete's save.
    /// </remarks>
    public async Task<bool> IsReferencedAsync(Guid id, CancellationToken cancellationToken)
    {
        var media = context.Model.FindEntityType(typeof(MediaAsset))
            ?? throw new InvalidOperationException("MediaAsset is not in the model.");

        foreach (var foreignKey in media.GetReferencingForeignKeys())
        {
            // A rendition's link to its original is ownership, not use: renditions are deleted with it.
            if (foreignKey.DeclaringEntityType.ClrType == typeof(MediaAsset))
            {
                continue;
            }

            // A reference this check cannot follow would be a silent hole in the delete guard, so it is a fault that
            // shows up on the first delete in any test, not a reference quietly skipped (media.md).
            if (foreignKey.Properties.Count != 1
                || !foreignKey.PrincipalKey.IsPrimaryKey()
                || foreignKey.DeleteBehavior != DeleteBehavior.Restrict)
            {
                throw new InvalidOperationException(
                    $"{foreignKey.DeclaringEntityType.DisplayName()} references MediaAsset in a way the delete guard cannot "
                    + "check: use one column, the asset's Id, and DeleteBehavior.Restrict.");
            }

            var property = foreignKey.Properties[0];
            var check = AnyReferencingMethod.MakeGenericMethod(foreignKey.DeclaringEntityType.ClrType, property.ClrType);
            var task = (Task<bool>)check.Invoke(this, [property.Name, id, cancellationToken])!;
            if (await task)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<MediaDeliveryRecord?> FindDeliveryAsync(Guid id, string? rendition, CancellationToken cancellationToken)
    {
        if (rendition is null)
        {
            return await Assets.AsNoTracking()
                .Where(asset => asset.Id == id && asset.Kind != MediaAssetKind.Derived)
                .Select(asset => new MediaDeliveryRecord(asset.Visibility, asset.BlobKey, asset.MimeType, asset.Bytes, asset.Checksum))
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await (
                from derived in Assets.AsNoTracking()
                join original in Assets.AsNoTracking() on derived.ParentAssetId equals original.Id
                where derived.ParentAssetId == id && derived.RenditionName == rendition
                select new MediaDeliveryRecord(original.Visibility, derived.BlobKey, derived.MimeType, derived.Bytes, derived.Checksum))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<HashSet<string>> GetBlobKeysAsync(Guid assetId, CancellationToken cancellationToken) =>
    [
        .. await Assets.AsNoTracking()
            .Where(asset => asset.Id == assetId || asset.ParentAssetId == assetId)
            .Select(asset => asset.BlobKey)
            .ToListAsync(cancellationToken),
    ];

    public Task<int> SetProcessingStatusAsync(Guid originalId, MediaProcessingStatus status, CancellationToken cancellationToken) =>
        Assets
            .Where(asset => asset.Id == originalId && asset.Kind != MediaAssetKind.Derived)
            .ExecuteUpdateAsync(set => set.SetProperty(asset => asset.ProcessingStatus, status), cancellationToken);

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private Task<bool> AnyReferencingAsync<TDependent, TKey>(string propertyName, Guid id, CancellationToken cancellationToken)
        where TDependent : class
    {
        // Guid and Guid? both arrive here, so the comparison is built as an expression for the property's own type:
        // `entity => EF.Property<TKey>(entity, name) == id`, which EF translates to one EXISTS query.
        var entity = Expression.Parameter(typeof(TDependent), "entity");
        var property = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(TKey)], entity, Expression.Constant(propertyName));
        var predicate = Expression.Lambda<Func<TDependent, bool>>(
            Expression.Equal(property, Expression.Constant((TKey)(object)id, typeof(TKey))), entity);

        return context.Set<TDependent>().AnyAsync(predicate, cancellationToken);
    }
}
