using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.Imaging;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Media.Data;

/// <summary>What delivery needs to answer one <c>/media/**</c> request: no more than one indexed row read.</summary>
internal sealed record MediaDeliveryRecord(
    MediaVisibility Visibility, string BlobKey, string MimeType, long Bytes, string Checksum);

/// <summary>Where a library page resumes: after this row in (UploadedAt desc, Id desc) order.</summary>
internal sealed record MediaListPosition(DateTimeOffset UploadedAt, Guid Id);

/// <summary>
/// The two contenders for a master, in temp files: the compressed re-encode, and — when the plan did not resize —
/// the upload itself with its metadata scrubbed. Business chooses (<see cref="MasterPlan.KeepsReencode"/>); the
/// caller disposes whichever it does not keep, or both through <see cref="DisposeAsync"/>.
/// </summary>
/// <param name="Width">The re-encode's size, as displayed.</param>
internal sealed record MasterCandidates(MediaTempFile Reencoded, int Width, int Height, MediaTempFile? Scrubbed) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Reencoded.DisposeAsync();
        if (Scrubbed is not null)
        {
            await Scrubbed.DisposeAsync();
        }
    }
}

/// <summary>One rendition to produce, as Business decided it.</summary>
/// <param name="ConvertToSrgb">Renditions are sRGB for the web.</param>
internal sealed record RenditionTarget(
    MediaRenditionName Name, int Quality, bool Chroma444, bool ConvertToSrgb, string Profile, string BlobKey);

/// <summary>Everything a rendition run produces, and the smallest decode that serves all of it.</summary>
/// <param name="DecodeLongSide">The longest side to decode the master at: enough for the widest target, no more.</param>
internal sealed record RenditionPlan(IReadOnlyList<RenditionTarget> Targets, int DecodeLongSide);

/// <summary>What a rendition run did.</summary>
internal enum RenditionRunOutcome
{
    /// <summary>Renditions were encoded, or every one was already in place.</summary>
    Completed = 0,

    /// <summary>The master does not decode: retrying will not change that.</summary>
    Undecodable = 10,
}

internal enum MediaWriteOutcome
{
    Saved = 0,

    /// <summary>Someone else saved first: the row version moved under this write.</summary>
    Conflict = 10,

    /// <summary>The delete lost a race with something that started referencing the asset.</summary>
    InUse = 20,
}

internal interface IMediaDataLayer
{
    /// <summary>The upload in a self-deleting temp file, or <c>null</c> once it passes <paramref name="maxBytes"/>.</summary>
    Task<MediaTempFile?> SpoolUploadAsync(Stream content, long maxBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Carries out a <see cref="MasterPlan"/>: decodes, re-encodes, and scrubs. <c>null</c> when the upload does not
    /// decode.
    /// </summary>
    Task<MasterCandidates?> PrepareMasterAsync(
        Stream upload, ImageProbeResult probe, MasterPlan plan, CancellationToken cancellationToken);

    /// <summary>SHA-256 of the stream, lower-case hex; the stream is left at its start.</summary>
    Task<string> ChecksumAsync(Stream content, CancellationToken cancellationToken);

    Task<Guid?> FindDuplicateAsync(string checksum, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the master's bytes, then the row, its audit entry and <c>media.rendition-requested</c> in one save. A
    /// failed save removes the bytes again.
    /// </summary>
    Task CreateOriginalAsync(MediaAsset original, Stream master, CancellationToken cancellationToken);

    Task<MediaAsset?> FindOriginalAsync(Guid id, CancellationToken cancellationToken);

    Task<List<MediaAsset>> GetRenditionsAsync(Guid originalId, CancellationToken cancellationToken);

    Task<List<MediaAssetSummaryServiceModel>> ListOriginalsAsync(
        MediaListPosition? after, int take, bool missingAltText, MediaProcessingStatus? status, CancellationToken cancellationToken);

    Task<List<(Guid Id, bool HasAltText)>> FindAttachabilityAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The originals among <paramref name="ids"/> with every rendition of each, untracked.</summary>
    Task<List<MediaAsset>> FindWithRenditionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<MediaDeliveryRecord?> FindDeliveryAsync(Guid id, string? rendition, CancellationToken cancellationToken);

    Task<Stream?> OpenBlobAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a staff edit already applied to <paramref name="original"/>, with its audit entry, and stages
    /// <c>media.rendition-requested</c> when <paramref name="requestRenditions"/> — in one save.
    /// </summary>
    Task<MediaWriteOutcome> UpdateAsync(
        MediaAsset original, bool requestRenditions, string actorUserId, string summary, CancellationToken cancellationToken);

    Task<bool> IsReferencedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Removes the original and its renditions, audits it and stages <c>media.purge-requested</c>, in one save.</summary>
    Task<MediaWriteOutcome> DeleteAsync(MediaAsset original, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Produces whichever targets are not already in place, in one save that also stages a purge for any files the
    /// run replaced; then marks the original Ready.
    /// </summary>
    Task<RenditionRunOutcome> ProduceRenditionsAsync(
        MediaAsset original, RenditionPlan plan, CancellationToken cancellationToken);

    /// <summary>Records that renditions will not be produced: status Failed plus an audit row.</summary>
    Task MarkRenditionsFailedAsync(Guid originalId, int attempt, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes every blob under the asset's prefix that no row references and that is not in
    /// <paramref name="keep"/> (the keys a rendition run may be writing right now). Returns how many went.
    /// </summary>
    Task<int> PurgeAsync(Guid assetId, IReadOnlySet<string> keep, CancellationToken cancellationToken);

    Task RecordPurgeFailedAsync(Guid assetId, int attempt, CancellationToken cancellationToken);
}
