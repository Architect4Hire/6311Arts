using System.Buffers.Binary;
using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Media.Business;
using Arts6311.Domain.Modules.Media.Data;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Data.Repositories;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Media;

/// <summary>
/// What the 2.3 reviews asked for: atomicity on every staging path, the purge's poison and in-flight rules, the
/// Worker never colliding with a staff edit, and the parsers refusing what no encoder writes.
/// </summary>
public sealed class MediaHardeningTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFailedDeleteSaveLeavesTheRowsAndNoPurge()
    {
        var failure = new FailInsertInto("OutboxMessages") { Armed = false };
        await using var host = await MediaTestHost.CreateAsync(Ct, interceptor: failure);
        var asset = await ReadyAssetAsync(host);
        await ClearOutboxAsync(host);

        failure.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() =>
            host.WithFacadeAsync(facade => facade.DeleteAsync(asset.Id, asset.ETag, MediaTestHost.EditorId, Ct)));

        Assert.True(failure.Fired);
        Assert.Equal(7, await host.QueryAsync(context => context.Set<MediaAsset>().CountAsync(Ct)));
        Assert.False(await host.QueryAsync(context => context.OutboxMessages.AnyAsync(Ct)));
        Assert.False(await host.QueryAsync(context => context.AuditLogs.AnyAsync(log => log.Action == MediaAudit.Deleted, Ct)));
    }

    [Fact]
    public async Task AFailedPresetSaveLeavesThePresetAndNoRenditionRequest()
    {
        var failure = new FailInsertInto("OutboxMessages") { Armed = false };
        await using var host = await MediaTestHost.CreateAsync(Ct, interceptor: failure);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(400, 300), "a.jpg", cancellationToken: Ct)).Value!;
        await ClearOutboxAsync(host);

        failure.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.UpdateAsync(
            asset.Id, new MediaAssetUpdateViewModel(CompressionPreset: MediaCompressionPreset.Smallest), asset.ETag, MediaTestHost.EditorId, Ct)));

        Assert.True(failure.Fired);
        var stored = await host.OriginalAsync(asset.Id);
        Assert.Equal(MediaCompressionPreset.Balanced, stored.CompressionPreset);
        Assert.Equal(asset.ETag, EntityTag.Format(stored.RowVersion));
        Assert.False(await host.QueryAsync(context => context.OutboxMessages.AnyAsync(Ct)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("{\"assetId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public async Task AMalformedPurgeIsPoisonedWithoutTouchingStorage(string payload)
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        host.Blobs.Put($"{Guid.NewGuid():N}/original-x.jpg", [1], "image/jpeg");

        await Assert.ThrowsAsync<OutboxPoisonException>(() => host.HandleAsync(MediaPurgeRequested.MessageType, payload, 1, Ct));

        Assert.Equal(0, host.Blobs.Deletes);
        Assert.Single(host.Blobs.Keys);
    }

    [Fact]
    public async Task AMalformedPurgeIsPoisonedOnItsFirstAttemptByTheDispatcher()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        using (var scope = host.CreateScope())
        {
            MediaTestHost.Resolve<IOutboxWriter>(scope).Enqueue(MediaPurgeRequested.MessageType, "{ not json", Guid.NewGuid());
            await MediaTestHost.Resolve<ArtsDbContext>(scope).SaveChangesAsync(Ct);
        }

        Assert.Equal(new OutboxDispatchSummary(1, 0, 0, 1), await host.DispatchAsync(Ct));
        var message = await host.QueryAsync(context => context.OutboxMessages.SingleAsync(Ct));
        Assert.Equal(OutboxMessageStatus.Poisoned, message.Status);
        Assert.Equal(1, message.Attempts);
    }

    [Fact]
    public async Task AStaffEditDuringRenderingNeitherBlocksTheWorkerNorIsLost()
    {
        // The usual order of events: upload, then type the alt text while the Worker is still encoding.
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(1200, 800), "a.jpg", cancellationToken: Ct)).Value!;
        MediaAssetServiceModel? edited = null;
        host.Blobs.BeforeUpload = async _ =>
        {
            var result = await host.WithFacadeAsync(facade => facade.UpdateAsync(
                asset.Id, new MediaAssetUpdateViewModel(AltText: "A gradient with a red corner"), asset.ETag, MediaTestHost.EditorId, Ct));
            edited = result.Value;
        };

        Assert.Equal(new OutboxDispatchSummary(1, 1, 0, 0), await host.DispatchAsync(Ct));

        var stored = await host.OriginalAsync(asset.Id);
        Assert.NotNull(edited);
        Assert.Equal(MediaProcessingStatus.Ready, stored.ProcessingStatus);
        Assert.Equal("A gradient with a red corner", stored.AltText);
        Assert.Equal(edited.ETag, EntityTag.Format(stored.RowVersion));
        Assert.Equal(6, (await host.RenditionsAsync(asset.Id)).Count);
    }

    [Fact]
    public async Task APurgeOfALiveAssetSparesWhatARunMayBeWritingAndRemovesTheRest()
    {
        // Renditions not made yet: a run for the current preset could be uploading its files right now, ahead of the
        // save that will name them. Those keys survive; a file nothing will ever name does not.
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(1200, 800), "a.jpg", cancellationToken: Ct)).Value!;
        var original = await host.OriginalAsync(asset.Id);
        var inFlight = MediaBusiness.PlanFor(original).Targets[0].BlobKey;
        var leftover = $"{asset.Id:N}/{new string('0', 64)}/400-old.webp";
        host.Blobs.Put(inFlight, [1], "image/webp");
        host.Blobs.Put(leftover, [2], "image/webp");

        await host.HandleAsync(MediaPurgeRequested.MessageType, new MediaPurgeRequested(asset.Id).Serialize(), 1, Ct);

        Assert.Contains(inFlight, host.Blobs.Keys);
        Assert.Contains(original.BlobKey, host.Blobs.Keys);
        Assert.DoesNotContain(leftover, host.Blobs.Keys);
    }

    [Fact]
    public async Task ARenditionMessageForADeletedAssetSweepsWhatARacingRunLeftBehind()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var gone = Guid.NewGuid();
        host.Blobs.Put($"{gone:N}/{new string('a', 64)}/800-abcd1234.webp", [1], "image/webp");

        await host.HandleAsync(MediaRenditionRequested.MessageType, new MediaRenditionRequested(gone).Serialize(), 2, Ct);

        Assert.Empty(host.Blobs.Keys);
        Assert.Equal(0, host.Blobs.Uploads);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"0123456789abcdef0123456789abcdef\"")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("\"a\", \"b\"")]
    public async Task AnIfMatchThatIsNotOneStrongETagIsMalformedNotMissing(string ifMatch)
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(200, 100), "a.jpg", cancellationToken: Ct)).Value!;

        var result = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            asset.Id, new MediaAssetUpdateViewModel(Credit: "Someone"), ifMatch, MediaTestHost.EditorId, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error?.Code);
        Assert.True(result.Error!.FieldErrors.ContainsKey("ifMatch"));
    }

    [Fact]
    public async Task ALongCopyrightBecomesACreditThatFits()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var copyright = new string('c', 250);

        var result = await host.UploadAsync(
            ImageFixtures.Jpeg(200, 100, exifTiff: ImageFixtures.ExifWithGps(copyright: copyright)), "a.jpg", cancellationToken: Ct);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(copyright[..MediaPolicy.CreditMaxLength], result.Value!.Credit);
    }

    [Fact]
    public async Task AFileWithMoreChunksThanAnyEncoderWritesIsRefused()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var png = ImageFixtures.Png(32, 32);
        using var flooded = new MemoryStream();
        flooded.Write(png, 0, 33);
        for (var i = 0; i < 5000; i++)
        {
            WritePngTextChunk(flooded);
        }

        flooded.Write(png, 33, png.Length - 33);

        var result = await host.UploadAsync(flooded.ToArray(), "flood.png", cancellationToken: Ct);

        Assert.Equal(MediaErrorCodes.Undecodable, result.Error?.Code);
    }

    [Fact]
    public async Task InvisibleFormatCharactersAreRemovedFromTheFileName()
    {
        // U+202E, a right-to-left override: "photo<RLO>gpj.png" would display as "photognp.jpg".
        await using var host = await MediaTestHost.CreateAsync(Ct);

        var result = await host.UploadAsync(ImageFixtures.Png(32, 32), "photo‮gpj.png", cancellationToken: Ct);

        Assert.Equal("photogpj.png", result.Value!.FileName);
    }

    [Fact]
    public async Task AReferenceTheDeleteGuardCannotFollowIsAFaultNotASkip()
    {
        // A content module that declares Cascade instead of Restrict would let a delete take its rows with it.
        await using var host = await MediaTestHost.CreateAsync(Ct, referencing: DeleteBehavior.Cascade);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(200, 100), "a.jpg", cancellationToken: Ct)).Value!;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.WithFacadeAsync(facade => facade.DeleteAsync(asset.Id, asset.ETag, MediaTestHost.EditorId, Ct)));

        Assert.Contains("Restrict", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDataLayerStagesEachMessageBeforeItsOneSave()
    {
        // backend.md's DataLayer test: the order of calls, against recording fakes.
        var calls = new List<string>();
        var repository = new RecordingRepository(calls);
        var dataLayer = new MediaDataLayer(
            repository,
            new RecordingBlobs(calls),
            new SkiaImageCodecGateway(),
            new RecordingOutbox(calls),
            new RecordingAudit(calls),
            new CorrelationContext(),
            new SystemClock(new FakeTimeProvider()),
            NullLogger<MediaDataLayer>.Instance);
        var asset = new MediaAsset { Id = Guid.NewGuid(), BlobKey = "k", MimeType = "image/jpeg", UploadedBy = "u" };

        await dataLayer.CreateOriginalAsync(asset, new MemoryStream([1, 2, 3]), Ct);
        Assert.Equal(["upload", "add", "audit", $"enqueue {MediaRenditionRequested.MessageType}", "save"], calls);

        calls.Clear();
        await dataLayer.DeleteAsync(asset, "u", Ct);
        Assert.Equal(["renditions", "remove", "audit", $"enqueue {MediaPurgeRequested.MessageType}", "save"], calls);

        calls.Clear();
        await dataLayer.UpdateAsync(asset, requestRenditions: true, "u", "Updated.", Ct);
        Assert.Equal(["audit", $"enqueue {MediaRenditionRequested.MessageType}", "save"], calls);
    }

    private static void WritePngTextChunk(Stream output)
    {
        byte[] data = [.. "c\0"u8];
        var type = "tEXt"u8.ToArray();
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);

        var crc = 0xFFFFFFFFu;
        foreach (var value in (byte[])[.. type, .. data])
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    private static async Task<MediaAssetServiceModel> ReadyAssetAsync(MediaTestHost host)
    {
        var uploaded = await host.UploadAsync(ImageFixtures.Jpeg(1000, 500), "ready.jpg", cancellationToken: Ct);
        await host.DispatchAsync(Ct);
        return (await host.WithFacadeAsync(facade => facade.GetAsync(uploaded.Value!.Id, Ct))).Value!;
    }

    private static Task ClearOutboxAsync(MediaTestHost host) =>
        host.ExecuteAsync(context => context.OutboxMessages.ExecuteDeleteAsync(Ct));

    private sealed class RecordingRepository(List<string> calls) : IMediaRepository
    {
        public void Add(MediaAsset asset) => calls.Add("add");

        public void Remove(MediaAsset asset) => calls.Add("remove");

        public Task<List<MediaAsset>> GetRenditionsAsync(Guid originalId, CancellationToken cancellationToken)
        {
            calls.Add("renditions");
            return Task.FromResult(new List<MediaAsset>());
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            calls.Add("save");
            return Task.CompletedTask;
        }

        public void DiscardChanges() => calls.Add("discard");

        public Task<MediaAsset?> FindOriginalAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid?> FindOriginalByChecksumAsync(string checksum, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<List<MediaAssetSummaryServiceModel>> ListOriginalsAsync(
            MediaListPosition? after, int take, bool missingAltText, MediaProcessingStatus? status, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<List<(Guid Id, bool HasAltText)>> FindAttachabilityAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> IsReferencedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MediaDeliveryRecord?> FindDeliveryAsync(Guid id, string? rendition, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<HashSet<string>> GetBlobKeysAsync(Guid assetId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> SetProcessingStatusAsync(Guid originalId, MediaProcessingStatus status, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingBlobs(List<string> calls) : IMediaBlobGateway
    {
        public Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
        {
            calls.Add("upload");
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingOutbox(List<string> calls) : IOutboxWriter
    {
        public void Enqueue(string messageType, string payloadJson, Guid correlationId) => calls.Add($"enqueue {messageType}");
    }

    private sealed class RecordingAudit(List<string> calls) : IAuditWriter
    {
        public void Record(AuditEntry entry) => calls.Add("audit");
    }
}
