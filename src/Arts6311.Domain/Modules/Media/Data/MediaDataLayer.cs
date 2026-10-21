using System.Security.Cryptography;
using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Data.Repositories;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.Imaging;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Media.Data;

/// <summary>
/// The Media module's DataLayer: every write is one <c>SaveChangesAsync</c>, with its audit row and outbox message
/// staged before it (outbox.md). It carries out plans Business made; it decides nothing about images itself.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The upload writes its blob before the save, not after.</strong> A blob written ahead of a commit is not a
/// side effect of that commit — nothing has committed yet — so it needs no outbox message. A save that fails removes
/// the blob again, best effort; the alternative (a row pointing at bytes that may never arrive) is worse (ADR 0030).
/// </para>
/// <para>
/// <strong>Nothing calls storage after a save.</strong> Files a rendition run replaces are deleted by the
/// <c>media.purge-requested</c> message it stages in the same save, and the Worker's status writes are targeted
/// updates that never take part in a staff editor's row-version check.
/// </para>
/// </remarks>
internal sealed partial class MediaDataLayer(
    IMediaRepository repository,
    IMediaBlobGateway blobs,
    IImageCodecGateway codec,
    IOutboxWriter outbox,
    IAuditWriter audit,
    ICorrelationContext correlation,
    IClock clock,
    ILogger<MediaDataLayer> logger) : IMediaDataLayer
{
    public Task<MediaTempFile?> SpoolUploadAsync(Stream content, long maxBytes, CancellationToken cancellationToken) =>
        MediaTempFile.SpoolAsync(content, maxBytes, cancellationToken);

    public async Task<MasterCandidates?> PrepareMasterAsync(
        Stream upload, ImageProbeResult probe, MasterPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(plan);

        var reencoded = MediaTempFile.Create();
        MediaTempFile? scrubbed = null;
        try
        {
            int width, height;

            // Always decoded, even when the upload itself may be kept: a file that will not decode is refused now,
            // while the person is still looking at the upload, rather than failing later in the Worker.
            using (var image = await codec.DecodeAsync(upload, probe.Exif.Orientation, plan.DecodeLongSide, cancellationToken))
            {
                await using var encoded = MediaTempFile.Create();
                (width, height) = image.Encode(
                    new ImageEncodeSpec(plan.Format, plan.TargetWidth, plan.Quality, plan.Lossless, plan.Chroma444, MasterPlan.ConvertToSrgb),
                    encoded.Stream);

                // The encoder writes no EXIF at all; the kept tags go back in. The orientation is now in the pixels.
                ImageMetadataRewriter.Rewrite(encoded.Stream, plan.Format, plan.Keep.WithoutOrientation(), reencoded.Stream);
            }

            if (!plan.Resize)
            {
                // Same size, so the upload itself is a contender: scrubbed of everything but the kept tags, its
                // orientation tag included, so it still displays the right way up.
                scrubbed = MediaTempFile.Create();
                ImageMetadataRewriter.Rewrite(upload, plan.Format, plan.Keep, scrubbed.Stream);
                scrubbed.Stream.Position = 0;
            }

            reencoded.Stream.Position = 0;
            return new MasterCandidates(reencoded, width, height, scrubbed);
        }
        catch (Exception exception) when (exception is ImageUndecodableException or InvalidDataException)
        {
            await DisposeAsync(reencoded, scrubbed);
            var reason = exception.GetType().Name;
            LogUndecodable(probe.Format, reason);
            return null;
        }
        catch
        {
            await DisposeAsync(reencoded, scrubbed);
            throw;
        }
    }

    public async Task<string> ChecksumAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        content.Position = 0;
        var checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, cancellationToken));
        content.Position = 0;
        return checksum;
    }

    public Task<Guid?> FindDuplicateAsync(string checksum, bool includeSubmissions, CancellationToken cancellationToken) =>
        repository.FindOriginalByChecksumAsync(checksum, includeSubmissions, cancellationToken);

    public async Task CreateOriginalAsync(MediaAsset original, Stream master, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(master);

        try
        {
            await blobs.UploadAsync(original.BlobKey, master, original.MimeType, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the type: an Azure SDK message names the account and the request.
            throw new InvalidOperationException($"Storing the master failed ({exception.GetType().Name}).");
        }

        repository.Add(original);
        var correlationId = correlation.Current;
        audit.Record(new AuditEntry(
            original.UploadedBy,
            MediaAudit.Uploaded,
            MediaAudit.AssetResourceType,
            original.Id.ToString(),
            correlationId,
            $"Uploaded {original.MimeType}, {original.Width}×{original.Height}, {original.UploadedBytes} → {original.Bytes} bytes."));

        // Ids only: the handler reloads the asset, so it renders whatever the asset is when it runs.
        outbox.Enqueue(
            MediaRenditionRequested.MessageType,
            new MediaRenditionRequested(original.Id).Serialize(),
            correlationId);

        // One save: the row, the audit entry and the message commit together, or none of them does.
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            repository.DiscardChanges();
            await DeleteQuietlyAsync(original.Id, original.BlobKey);
            throw;
        }
    }

    public Task<MediaAsset?> FindOriginalAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindOriginalAsync(id, cancellationToken);

    public Task<List<MediaAsset>> GetRenditionsAsync(Guid originalId, CancellationToken cancellationToken) =>
        repository.GetRenditionsAsync(originalId, cancellationToken);

    public Task<List<MediaAssetSummaryServiceModel>> ListOriginalsAsync(
        MediaListPosition? after, int take, bool missingAltText, MediaProcessingStatus? status, CancellationToken cancellationToken) =>
        repository.ListOriginalsAsync(after, take, missingAltText, status, cancellationToken);

    public Task<List<(Guid Id, bool HasAltText)>> FindAttachabilityAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        repository.FindAttachabilityAsync(ids, cancellationToken);

    public Task<List<MediaAsset>> FindWithRenditionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        repository.FindWithRenditionsAsync(ids, cancellationToken);

    public Task<MediaDeliveryRecord?> FindDeliveryAsync(Guid id, string? rendition, CancellationToken cancellationToken) =>
        repository.FindDeliveryAsync(id, rendition, cancellationToken);

    public Task<Stream?> OpenBlobAsync(string key, CancellationToken cancellationToken) =>
        blobs.OpenReadAsync(key, cancellationToken);

    public async Task<MediaWriteOutcome> UpdateAsync(
        MediaAsset original, bool requestRenditions, string actorUserId, string summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);

        // Bookkeeping: who and when, and a new row version so every other open form's If-Match goes stale. EF
        // still compares against the version this row was read with.
        original.UpdatedBy = actorUserId;
        original.UpdatedAt = clock.UtcNow;
        original.RowVersion = Guid.NewGuid();

        var correlationId = correlation.Current;
        audit.Record(new AuditEntry(
            actorUserId, MediaAudit.Updated, MediaAudit.AssetResourceType, original.Id.ToString(), correlationId, summary));

        if (requestRenditions)
        {
            outbox.Enqueue(
                MediaRenditionRequested.MessageType,
                new MediaRenditionRequested(original.Id).Serialize(),
                correlationId);
        }

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return MediaWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.DiscardChanges();
            return MediaWriteOutcome.Conflict;
        }
    }

    public Task<bool> IsReferencedAsync(Guid id, CancellationToken cancellationToken) =>
        repository.IsReferencedAsync(id, cancellationToken);

    public async Task<MediaWriteOutcome> DeleteAsync(MediaAsset original, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);

        // Renditions first and explicitly: the self-reference cannot cascade on SQL Server.
        foreach (var rendition in await repository.GetRenditionsAsync(original.Id, cancellationToken))
        {
            repository.Remove(rendition);
        }

        repository.Remove(original);

        var correlationId = correlation.Current;
        audit.Record(new AuditEntry(
            actorUserId,
            MediaAudit.Deleted,
            MediaAudit.AssetResourceType,
            original.Id.ToString(),
            correlationId,
            $"Deleted {original.MimeType}, {original.Width}×{original.Height}.",
            BeforeReference: $"checksum:{original.Checksum}"));

        outbox.Enqueue(MediaPurgeRequested.MessageType, new MediaPurgeRequested(original.Id).Serialize(), correlationId);

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return MediaWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.DiscardChanges();
            return MediaWriteOutcome.Conflict;
        }
        catch (DbUpdateException)
        {
            // Most likely a Restrict foreign key: something started using the asset after Business checked. Asked
            // again rather than assumed, so a genuine fault is not reported as "in use".
            repository.DiscardChanges();
            if (await repository.IsReferencedAsync(original.Id, cancellationToken))
            {
                return MediaWriteOutcome.InUse;
            }

            throw;
        }
    }

    public async Task<RenditionRunOutcome> ProduceRenditionsAsync(
        MediaAsset original, RenditionPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(plan);

        var existing = (await repository.GetRenditionsAsync(original.Id, cancellationToken))
            .ToDictionary(rendition => rendition.RenditionName!, StringComparer.Ordinal);

        // Content-addressed: a rendition already stored under its key is done. A redelivered message lands here
        // with nothing to do and encodes nothing.
        var todo = plan.Targets
            .Where(target => !(existing.TryGetValue(target.Name.ToString(), out var row) && row.BlobKey == target.BlobKey))
            .ToList();

        if (todo.Count > 0)
        {
            await using var master = MediaTempFile.Create();
            await using (var blob = await blobs.OpenReadAsync(original.BlobKey, cancellationToken)
                ?? throw new InvalidOperationException("The original's master blob is missing."))
            {
                await blob.CopyToAsync(master.Stream, cancellationToken);
            }

            var probe = ImageProbe.Probe(master.Stream);
            if (probe is null || !probe.HasDimensions)
            {
                LogUndecodable(probe?.Format ?? MediaImageFormat.Jpeg, "header");
                return RenditionRunOutcome.Undecodable;
            }

            var now = clock.UtcNow;
            var replaced = false;
            try
            {
                using var image = await codec.DecodeAsync(master.Stream, probe.Exif.Orientation, plan.DecodeLongSide, cancellationToken);
                foreach (var target in todo)
                {
                    await using var output = MediaTempFile.Create();
                    var (width, height) = image.Encode(
                        new ImageEncodeSpec(
                            target.Name.Format, target.Name.Width, target.Quality, Lossless: false, target.Chroma444, target.ConvertToSrgb),
                        output.Stream);

                    var checksum = await ChecksumAsync(output.Stream, cancellationToken);
                    var mimeType = target.Name.Format.MimeType();
                    await blobs.UploadAsync(target.BlobKey, output.Stream, mimeType, cancellationToken);

                    if (!existing.TryGetValue(target.Name.ToString(), out var row))
                    {
                        row = new MediaAsset
                        {
                            Id = Guid.NewGuid(),
                            Kind = MediaAssetKind.Derived,
                            ParentAssetId = original.Id,
                            RenditionName = target.Name.ToString(),
                            UploadedBy = original.UploadedBy,
                            UploadedAt = now,
                        };
                        repository.Add(row);
                    }
                    else
                    {
                        replaced = true;
                    }

                    row.MimeType = mimeType;
                    row.Bytes = output.Length;
                    row.Width = width;
                    row.Height = height;
                    row.Checksum = checksum;
                    row.BlobKey = target.BlobKey;
                    row.ProfileHash = target.Profile;
                    row.CompressionPreset = original.CompressionPreset;
                    row.ProcessingStatus = MediaProcessingStatus.Ready;
                    row.UpdatedAt = now;

                    // A new version on every write, so a slower run rendering an older preset collides here instead of
                    // overwriting this run's rows with keys that the purge below will delete.
                    row.RowVersion = Guid.NewGuid();
                }
            }
            catch (Exception exception) when (exception is ImageUndecodableException or InvalidDataException)
            {
                repository.DiscardChanges();
                var reason = exception.GetType().Name;
                LogUndecodable(probe.Format, reason);
                return RenditionRunOutcome.Undecodable;
            }

            if (replaced)
            {
                // The files the replaced rows pointed at are now unreferenced; their purge commits with the rows.
                outbox.Enqueue(MediaPurgeRequested.MessageType, new MediaPurgeRequested(original.Id).Serialize(), correlation.Current);
            }

            await repository.SaveChangesAsync(cancellationToken);
        }

        // The Worker's own status, not a staff edit: a targeted update, outside the row-version check, so an editor
        // saving alt text while this ran neither blocks it nor is blocked by it. Idempotent if it runs twice.
        if (original.ProcessingStatus != MediaProcessingStatus.Ready || todo.Count > 0)
        {
            await repository.SetProcessingStatusAsync(original.Id, MediaProcessingStatus.Ready, cancellationToken);
        }

        var skipped = plan.Targets.Count - todo.Count;
        LogRenditions(original.Id, todo.Count, skipped);
        return RenditionRunOutcome.Completed;
    }

    public async Task MarkRenditionsFailedAsync(Guid originalId, int attempt, CancellationToken cancellationToken)
    {
        // Whatever the failed run left in the tracker must not ride along on this save.
        repository.DiscardChanges();

        // Two writes, deliberately, as in ProduceRenditionsAsync above: the status is the Worker's and goes out as
        // a targeted update outside the row-version check, so it neither blocks nor is blocked by an editor saving
        // alt text (media.md "Business decides; the DataLayer executes"). Loading the row to fold this into the
        // audit's save would put the Worker inside that check and invalidate an open editor's form.
        //
        // The order is status then audit, and the consequence is bounded: if the audit save below fails, the
        // handler throws and the message is retried, and the next attempt re-runs both (the update matches on id,
        // so it is idempotent). Only a failure on the final attempt leaves the asset Failed with no audit row, and
        // the Poisoned message is then the visible state instead (outbox.md).
        if (await repository.SetProcessingStatusAsync(originalId, MediaProcessingStatus.Failed, cancellationToken) == 0)
        {
            return;
        }

        audit.Record(new AuditEntry(
            ActorUserId: null,
            MediaAudit.RenditionsFailed,
            MediaAudit.AssetResourceType,
            originalId.ToString(),
            correlation.Current,
            $"Giving up on renditions after {attempt} attempt(s)."));

        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PurgeAsync(Guid assetId, IReadOnlySet<string> keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keep);

        var referenced = await repository.GetBlobKeysAsync(assetId, cancellationToken);
        var deleted = 0;
        foreach (var key in await blobs.ListAsync(MediaBlobKeys.Prefix(assetId), cancellationToken))
        {
            if (referenced.Contains(key) || keep.Contains(key))
            {
                continue;
            }

            await blobs.DeleteAsync(key, cancellationToken);
            deleted++;
        }

        LogPurged(assetId, deleted);
        return deleted;
    }

    public async Task RecordPurgeFailedAsync(Guid assetId, int attempt, CancellationToken cancellationToken)
    {
        repository.DiscardChanges();
        audit.Record(new AuditEntry(
            ActorUserId: null,
            MediaAudit.PurgeFailed,
            MediaAudit.AssetResourceType,
            assetId.ToString(),
            correlation.Current,
            $"Giving up on removing an asset's unused files after {attempt} attempt(s)."));

        await repository.SaveChangesAsync(cancellationToken);
    }

    private static async Task DisposeAsync(MediaTempFile first, MediaTempFile? second)
    {
        await first.DisposeAsync();
        if (second is not null)
        {
            await second.DisposeAsync();
        }
    }

    /// <summary>Best effort, before anything committed: the upload's blob, after its save failed.</summary>
    private async Task DeleteQuietlyAsync(Guid assetId, string key)
    {
        try
        {
            await blobs.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var reason = exception.GetType().Name;
            LogOrphaned(assetId, reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Media {Format} did not decode ({Reason}).")]
    private partial void LogUndecodable(MediaImageFormat format, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Media {AssetId}: {Produced} rendition(s) produced, {Skipped} already in place.")]
    private partial void LogRenditions(Guid assetId, int produced, int skipped);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Media {AssetId}: a failed upload's blob was not removed ({ExceptionType}).")]
    private partial void LogOrphaned(Guid assetId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Media {AssetId}: {Count} unused blob(s) purged.")]
    private partial void LogPurged(Guid assetId, int count);
}
