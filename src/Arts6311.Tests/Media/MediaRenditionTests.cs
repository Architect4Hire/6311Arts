using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.Imaging;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Media;

/// <summary>
/// <c>media.rendition-requested</c> (add-outbox-message's required tests): content-addressed and idempotent,
/// poisoned on a malformed payload, visible on its last attempt, and always rendering the asset as it is now.
/// </summary>
public sealed class MediaRenditionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUploadBecomesSixRenditionsThroughTheDispatcher()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(2000, 1000), "wide.jpg", cancellationToken: Ct)).Value!;

        var summary = await host.DispatchAsync(Ct);

        Assert.Equal(new OutboxDispatchSummary(1, 1, 0, 0), summary);
        var renditions = await host.RenditionsAsync(asset.Id);
        Assert.Equal(
            ["1600.jpg", "1600.webp", "400.jpg", "400.webp", "800.jpg", "800.webp"],
            renditions.Select(rendition => rendition.RenditionName).Order(StringComparer.Ordinal));

        foreach (var rendition in renditions)
        {
            var name = MediaRenditionName.TryParse(rendition.RenditionName)!;
            var bytes = host.Blobs.Bytes(rendition.BlobKey);
            var probe = ImageProbe.Probe(new MemoryStream(bytes))!;

            Assert.Equal(name.Format, probe.Format);
            Assert.Equal((name.Width, name.Width / 2), (probe.Width, probe.Height));
            Assert.Equal((rendition.Width, rendition.Height), (probe.Width, probe.Height));
            Assert.Equal(bytes.Length, rendition.Bytes);
            Assert.Equal(MediaAssetKind.Derived, rendition.Kind);
        }

        Assert.Equal(MediaProcessingStatus.Ready, (await host.OriginalAsync(asset.Id)).ProcessingStatus);
    }

    [Fact]
    public async Task ANarrowImageIsNeverUpscaled()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Png(500, 250), "narrow.png", cancellationToken: Ct)).Value!;

        await host.DispatchAsync(Ct);

        var widths = (await host.RenditionsAsync(asset.Id)).ToDictionary(rendition => rendition.RenditionName!, rendition => rendition.Width);
        Assert.Equal(400, widths["400.webp"]);
        Assert.Equal(500, widths["800.webp"]);
        Assert.Equal(500, widths["1600.jpg"]);
    }

    [Fact]
    public async Task RunningTheSameMessageTwiceHasOneEffect()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(1200, 800), "a.jpg", cancellationToken: Ct)).Value!;
        var payload = new MediaRenditionRequested(asset.Id).Serialize();

        await host.HandleAsync(MediaRenditionRequested.MessageType, payload, attempt: 1, Ct);
        var uploadsAfterFirst = host.Blobs.Uploads;
        var keysAfterFirst = host.Blobs.Keys.Order(StringComparer.Ordinal).ToList();

        await host.HandleAsync(MediaRenditionRequested.MessageType, payload, attempt: 1, Ct);

        // Content-addressed: every rendition is already at its key, so the redelivery encodes and uploads nothing.
        Assert.Equal(uploadsAfterFirst, host.Blobs.Uploads);
        Assert.Equal(keysAfterFirst, host.Blobs.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(6, (await host.RenditionsAsync(asset.Id)).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("{\"assetId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public async Task AMalformedPayloadIsPoisonedWithoutTouchingStorage(string payload)
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);

        await Assert.ThrowsAsync<OutboxPoisonException>(() =>
            host.HandleAsync(MediaRenditionRequested.MessageType, payload, attempt: 1, Ct));

        Assert.Equal(0, host.Blobs.Uploads);
    }

    [Fact]
    public async Task AMalformedMessageIsPoisonedOnItsFirstAttemptByTheDispatcher()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        await EnqueueAsync(host, MediaRenditionRequested.MessageType, "{ not json");

        var summary = await host.DispatchAsync(Ct);

        Assert.Equal(new OutboxDispatchSummary(1, 0, 0, 1), summary);
        var message = await host.QueryAsync(context => context.OutboxMessages.SingleAsync(Ct));
        Assert.Equal(OutboxMessageStatus.Poisoned, message.Status);
        Assert.Equal(1, message.Attempts);
    }

    [Fact]
    public async Task AStorageFailureIsRetriedWithASanitizedErrorAndTheAssetStaysPending()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(800, 600), "a.jpg", cancellationToken: Ct)).Value!;
        host.Blobs.Throw = new InvalidOperationException("https://arts6311prod.blob.core.windows.net/media refused PRIVATE-DETAIL");

        var summary = await host.DispatchAsync(Ct);

        Assert.Equal(new OutboxDispatchSummary(1, 0, 1, 0), summary);
        var message = await host.QueryAsync(context => context.OutboxMessages.SingleAsync(Ct));
        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.DoesNotContain("arts6311prod", message.LastError, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-DETAIL", message.LastError, StringComparison.Ordinal);
        Assert.Equal(MediaProcessingStatus.Pending, (await host.OriginalAsync(asset.Id)).ProcessingStatus);
    }

    [Fact]
    public async Task TheLastAttemptMarksTheAssetFailedForStaffToSee()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(800, 600), "a.jpg", cancellationToken: Ct)).Value!;
        host.Blobs.Throw = new InvalidOperationException("storage down");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.HandleAsync(
            MediaRenditionRequested.MessageType,
            new MediaRenditionRequested(asset.Id).Serialize(),
            OutboxPolicy.MaxAttempts,
            Ct));

        Assert.DoesNotContain("storage down", exception.Message, StringComparison.Ordinal);
        Assert.Equal(MediaProcessingStatus.Failed, (await host.OriginalAsync(asset.Id)).ProcessingStatus);
        Assert.True(await host.QueryAsync(context => context.AuditLogs.AnyAsync(log => log.Action == MediaAudit.RenditionsFailed, Ct)));
    }

    [Fact]
    public async Task AMasterThatDoesNotDecodeFailsAtOnceInsteadOfRetrying()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(800, 600), "a.jpg", cancellationToken: Ct)).Value!;
        var key = (await host.OriginalAsync(asset.Id)).BlobKey;
        var master = host.Blobs.Bytes(key);
        host.Blobs.Put(key, master[..(master.Length / 3)], "image/jpeg");

        await Assert.ThrowsAsync<OutboxPoisonException>(() => host.HandleAsync(
            MediaRenditionRequested.MessageType, new MediaRenditionRequested(asset.Id).Serialize(), attempt: 1, Ct));

        Assert.Equal(MediaProcessingStatus.Failed, (await host.OriginalAsync(asset.Id)).ProcessingStatus);
        Assert.Empty(await host.RenditionsAsync(asset.Id));
    }

    [Fact]
    public async Task AStaleMessageRendersTheAssetAsItIsNow()
    {
        // Two requests are staged — the upload's, then a preset change's — and the first is delivered after the
        // change. It must render the new preset, not the one it was staged under.
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(1200, 800), "a.jpg", cancellationToken: Ct)).Value!;

        var updated = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            asset.Id, new MediaAssetUpdateViewModel(CompressionPreset: MediaCompressionPreset.Smallest), asset.ETag, MediaTestHost.EditorId, Ct));
        Assert.True(updated.Succeeded, updated.Error?.Message);

        await host.HandleAsync(MediaRenditionRequested.MessageType, new MediaRenditionRequested(asset.Id).Serialize(), 1, Ct);

        var profile = RenditionProfile.For(MediaCompressionPreset.Smallest);
        Assert.All(await host.RenditionsAsync(asset.Id), rendition => Assert.Equal(profile, rendition.ProfileHash));
    }

    [Fact]
    public async Task AChangedPresetReplacesTheRenditionsUnderNewKeys()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(1200, 800), "a.jpg", cancellationToken: Ct)).Value!;
        await host.DispatchAsync(Ct);
        var before = (await host.RenditionsAsync(asset.Id)).ToDictionary(rendition => rendition.RenditionName!, rendition => rendition);

        var reread = (await host.WithFacadeAsync(facade => facade.GetAsync(asset.Id, Ct))).Value!;
        await host.WithFacadeAsync(facade => facade.UpdateAsync(
            asset.Id, new MediaAssetUpdateViewModel(CompressionPreset: MediaCompressionPreset.HighQuality), reread.ETag, MediaTestHost.EditorId, Ct));
        Assert.Equal(MediaProcessingStatus.Pending, (await host.OriginalAsync(asset.Id)).ProcessingStatus);
        await host.DispatchAsync(Ct);

        var after = await host.RenditionsAsync(asset.Id);
        Assert.Equal(6, after.Count);
        Assert.All(after, rendition =>
        {
            // Same row, new content address: a browser holding the old URL as immutable keeps a valid file.
            Assert.Equal(before[rendition.RenditionName!].Id, rendition.Id);
            Assert.NotEqual(before[rendition.RenditionName!].BlobKey, rendition.BlobKey);
        });
        Assert.Equal(MediaProcessingStatus.Ready, (await host.OriginalAsync(asset.Id)).ProcessingStatus);

        // The replaced files are not deleted by the rendition run itself (nothing calls storage after a save); the
        // purge it staged in the same save removes them on the next pass.
        Assert.Contains(before["800.webp"].BlobKey, host.Blobs.Keys);
        await host.DispatchAsync(Ct);
        Assert.All(before.Values, old => Assert.DoesNotContain(old.BlobKey, host.Blobs.Keys));
        Assert.All(after, current => Assert.Contains(current.BlobKey, host.Blobs.Keys));
    }

    [Fact]
    public async Task AMessageForADeletedAssetCompletesWithNothingToDo()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);

        await host.HandleAsync(MediaRenditionRequested.MessageType, new MediaRenditionRequested(Guid.NewGuid()).Serialize(), 1, Ct);

        Assert.Equal(0, host.Blobs.Uploads);
    }

    private static async Task EnqueueAsync(MediaTestHost host, string type, string payload)
    {
        using var scope = host.CreateScope();
        MediaTestHost.Resolve<IOutboxWriter>(scope).Enqueue(type, payload, Guid.NewGuid());
        await MediaTestHost.Resolve<ArtsDbContext>(scope).SaveChangesAsync(Ct);
    }
}
