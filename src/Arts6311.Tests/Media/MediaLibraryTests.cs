using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Media;

/// <summary>
/// Everything after the upload: delivery and who may see a draft, the library list, edits under If-Match, the
/// delete that is blocked while something uses the image, and the purge that follows a delete.
/// </summary>
public sealed class MediaLibraryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADraftIsNotServedToAnonymousVisitors()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = await ReadyAssetAsync(host);

        var result = await OpenAsync(host, asset.Id, "800.webp", MediaViewer.Public);

        // The same answer as for an asset that does not exist: a draft's existence is not public either.
        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
    }

    [Fact]
    public async Task ADraftIsServedToStaffPrivately()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = await ReadyAssetAsync(host);

        var result = await OpenAsync(host, asset.Id, "800.webp", MediaViewer.Staff);

        Assert.True(result.Succeeded, result.Error?.Message);
        await using var content = result.Value!.Content!;
        Assert.Equal("image/webp", result.Value.ContentType);
        Assert.Equal(MediaCacheability.Private, result.Value.Cacheability);
    }

    [Fact]
    public async Task APublishedAssetAtItsCurrentVersionIsImmutableAndWithoutOneRevalidates()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = await ReadyAssetAsync(host);
        await PublishAsync(host, asset.Id);
        var rendition = asset.Renditions.Single(item => item.Name == "400.jpg");
        var version = rendition.Url.Split("?v=")[1];

        var versioned = await OpenAsync(host, asset.Id, "400.jpg", MediaViewer.Public, version);
        var bare = await OpenAsync(host, asset.Id, "400.jpg", MediaViewer.Public);

        Assert.Equal(MediaCacheability.Immutable, versioned.Value!.Cacheability);
        Assert.Equal(MediaCacheability.Revalidate, bare.Value!.Cacheability);
        await versioned.Value.Content!.DisposeAsync();
        await bare.Value.Content!.DisposeAsync();
    }

    [Fact]
    public async Task AMatchingETagIsANotModifiedWithoutOpeningTheBlob()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = await ReadyAssetAsync(host);
        var first = await OpenAsync(host, asset.Id, "400.webp", MediaViewer.Staff);
        await first.Value!.Content!.DisposeAsync();

        // Storage is down, and it does not matter: a 304 never reads the blob.
        host.Blobs.Throw = new InvalidOperationException("storage down");
        var second = await OpenAsync(host, asset.Id, "400.webp", MediaViewer.Staff, ifNoneMatch: first.Value.ETag);

        Assert.True(second.Value!.NotModified);
        Assert.Null(second.Value.Content);
        Assert.Equal(first.Value.ETag, second.Value.ETag);
    }

    [Theory]
    [InlineData("original", MediaViewer.Public)]
    [InlineData("801.webp", MediaViewer.Staff)]
    [InlineData("800.gif", MediaViewer.Staff)]
    [InlineData("../../etc/passwd", MediaViewer.Staff)]
    public async Task OnlyKnownRenditionsExistAndTheMasterIsForStaff(string rendition, MediaViewer viewer)
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = await ReadyAssetAsync(host);
        await PublishAsync(host, asset.Id);

        Assert.Equal(ErrorCodes.NotFound, (await OpenAsync(host, asset.Id, rendition, viewer)).Error?.Code);
    }

    [Fact]
    public async Task StaffCanFetchTheMaster()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = await ReadyAssetAsync(host);

        var result = await OpenAsync(host, asset.Id, MediaPolicy.OriginalRendition, MediaViewer.Staff);

        await using var content = result.Value!.Content!;
        Assert.Equal(asset.Bytes, result.Value.Length);
    }

    [Fact]
    public async Task TheLibraryPagesNewestFirstAndFiltersMissingAltText()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            host.Time.Advance(TimeSpan.FromMinutes(1));
            var uploaded = await host.UploadAsync(
                ImageFixtures.Jpeg(100 + i, 100), $"p{i}.jpg", altText: i == 1 ? "A described picture" : null, cancellationToken: Ct);
            ids.Add(uploaded.Value!.Id);
        }

        var first = (await host.WithFacadeAsync(facade => facade.ListAsync(new MediaListViewModel(Limit: 2), Ct))).Value!;
        var second = (await host.WithFacadeAsync(facade => facade.ListAsync(new MediaListViewModel(first.NextCursor, 2), Ct))).Value!;
        var missing = (await host.WithFacadeAsync(facade => facade.ListAsync(new MediaListViewModel(MissingAltText: true), Ct))).Value!;

        Assert.Equal([ids[2], ids[1]], first.Items.Select(item => item.Id));
        Assert.Equal([ids[0]], second.Items.Select(item => item.Id));
        Assert.Null(second.NextCursor);
        Assert.Equal([ids[2], ids[0]], missing.Items.Select(item => item.Id));

        // A cursor from one list is refused by another.
        var crossed = await host.WithFacadeAsync(facade => facade.ListAsync(new MediaListViewModel(first.NextCursor, MissingAltText: true), Ct));
        Assert.Equal(ErrorCodes.CursorInvalid, crossed.Error?.Code);
    }

    [Fact]
    public async Task AnEditNeedsTheETagAndAStaleOneIsAConflict()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(200, 100), "a.jpg", cancellationToken: Ct)).Value!;
        var update = new MediaAssetUpdateViewModel(AltText: "A red square in the corner of a gradient");

        var missing = await host.WithFacadeAsync(facade => facade.UpdateAsync(asset.Id, update, null, MediaTestHost.EditorId, Ct));
        var saved = await host.WithFacadeAsync(facade => facade.UpdateAsync(asset.Id, update, asset.ETag, MediaTestHost.EditorId, Ct));
        var stale = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            asset.Id, new MediaAssetUpdateViewModel(Credit: "Someone"), asset.ETag, MediaTestHost.EditorId, Ct));

        Assert.Equal(ErrorCodes.PreconditionRequired, missing.Error?.Code);
        Assert.True(saved.Succeeded, saved.Error?.Message);
        Assert.NotEqual(asset.ETag, saved.Value!.ETag);
        Assert.Equal("A red square in the corner of a gradient", saved.Value.AltText);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error?.Code);
    }

    [Fact]
    public async Task AnEmptyStringClearsAField()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(200, 100), "a.jpg", altText: "Something visible", cancellationToken: Ct)).Value!;

        var cleared = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            asset.Id, new MediaAssetUpdateViewModel(AltText: ""), asset.ETag, MediaTestHost.EditorId, Ct));

        Assert.Null(cleared.Value!.AltText);
    }

    [Fact]
    public async Task AnAssetWithoutAltTextIsNotAttachableToPublishedContent()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var described = (await host.UploadAsync(ImageFixtures.Jpeg(200, 100), "a.jpg", altText: "A gradient", cancellationToken: Ct)).Value!;
        var bare = (await host.UploadAsync(ImageFixtures.Jpeg(201, 100), "b.jpg", cancellationToken: Ct)).Value!;
        var unknown = Guid.NewGuid();

        var check = await host.WithFacadeAsync(facade => facade.CheckAttachableAsync([described.Id, bare.Id, unknown], Ct));

        Assert.False(check.Attachable);
        Assert.Equal([bare.Id], check.MissingAltText);
        Assert.Equal([unknown], check.Missing);
        Assert.True((await host.WithFacadeAsync(facade => facade.CheckAttachableAsync([described.Id], Ct))).Attachable);
    }

    [Fact]
    public async Task DeleteIsBlockedWhileContentReferencesTheImage()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct, referencing: DeleteBehavior.Restrict);
        var asset = await ReadyAssetAsync(host);
        await host.ExecuteAsync(async context =>
        {
            context.Add(new MediaReferenceProbe { PortraitId = asset.Id });
            await context.SaveChangesAsync(Ct);
        });

        var result = await host.WithFacadeAsync(facade => facade.DeleteAsync(asset.Id, asset.ETag, MediaTestHost.EditorId, Ct));

        Assert.Equal(MediaErrorCodes.InUse, result.Error?.Code);
        Assert.Equal(7, await host.QueryAsync(context => context.Set<MediaAsset>().CountAsync(Ct)));
    }

    [Fact]
    public async Task DeleteRemovesTheRowsAuditsItAndStagesThePurge()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct, referencing: DeleteBehavior.Restrict);
        var asset = await ReadyAssetAsync(host);

        var result = await host.WithFacadeAsync(facade => facade.DeleteAsync(asset.Id, asset.ETag, MediaTestHost.EditorId, Ct));

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.False(await host.QueryAsync(context => context.Set<MediaAsset>().AnyAsync(Ct)));
        Assert.True(await host.QueryAsync(context => context.AuditLogs.AnyAsync(
            log => log.Action == MediaAudit.Deleted && log.ActorUserId == MediaTestHost.EditorId, Ct)));
        var purge = await host.QueryAsync(context => context.OutboxMessages.SingleAsync(
            message => message.Type == MediaPurgeRequested.MessageType, Ct));
        Assert.Equal(new MediaPurgeRequested(asset.Id), MediaPurgeRequested.TryParse(purge.PayloadJson));

        // The bytes go only when the purge runs, after the commit.
        Assert.NotEmpty(host.Blobs.Keys);
        await host.DispatchAsync(Ct);
        Assert.Empty(host.Blobs.Keys);
    }

    [Fact]
    public async Task DeleteNeedsTheCurrentETag()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var asset = (await host.UploadAsync(ImageFixtures.Jpeg(200, 100), "a.jpg", cancellationToken: Ct)).Value!;
        await host.WithFacadeAsync(facade => facade.UpdateAsync(
            asset.Id, new MediaAssetUpdateViewModel(Credit: "Someone"), asset.ETag, MediaTestHost.EditorId, Ct));

        var stale = await host.WithFacadeAsync(facade => facade.DeleteAsync(asset.Id, asset.ETag, MediaTestHost.EditorId, Ct));
        var none = await host.WithFacadeAsync(facade => facade.DeleteAsync(asset.Id, null, MediaTestHost.EditorId, Ct));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error?.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, none.Error?.Code);
    }

    [Fact]
    public async Task APurgeIsIdempotentAndNeverTouchesALiveAsset()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var live = await ReadyAssetAsync(host);
        var gone = Guid.NewGuid();
        host.Blobs.Put($"{gone:N}/original-x.jpg", [1, 2, 3], "image/jpeg");
        var liveKeys = host.Blobs.Keys.Where(key => key.StartsWith($"{live.Id:N}/", StringComparison.Ordinal)).ToList();

        await host.HandleAsync(MediaPurgeRequested.MessageType, new MediaPurgeRequested(gone).Serialize(), 1, Ct);
        await host.HandleAsync(MediaPurgeRequested.MessageType, new MediaPurgeRequested(gone).Serialize(), 1, Ct);
        await host.HandleAsync(MediaPurgeRequested.MessageType, new MediaPurgeRequested(live.Id).Serialize(), 1, Ct);

        Assert.Equal(liveKeys.Order(StringComparer.Ordinal), host.Blobs.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(1, host.Blobs.Deletes);
    }

    [Fact]
    public async Task APurgeThatKeepsFailingLeavesAnAuditRowOnItsLastAttempt()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        host.Blobs.Throw = new InvalidOperationException("arts6311prod.blob.core.windows.net refused PRIVATE-DETAIL");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.HandleAsync(
            MediaPurgeRequested.MessageType,
            new MediaPurgeRequested(Guid.NewGuid()).Serialize(),
            OutboxPolicy.MaxAttempts,
            Ct));

        Assert.DoesNotContain("PRIVATE-DETAIL", exception.Message, StringComparison.Ordinal);
        Assert.True(await host.QueryAsync(context => context.AuditLogs.AnyAsync(log => log.Action == MediaAudit.PurgeFailed, Ct)));
    }

    /// <summary>An uploaded image whose renditions the Worker has produced.</summary>
    private static async Task<MediaAssetServiceModel> ReadyAssetAsync(MediaTestHost host)
    {
        var uploaded = await host.UploadAsync(ImageFixtures.Jpeg(1000, 500), "ready.jpg", cancellationToken: Ct);
        Assert.True(uploaded.Succeeded, uploaded.Error?.Message);
        await host.DispatchAsync(Ct);
        return (await host.WithFacadeAsync(facade => facade.GetAsync(uploaded.Value!.Id, Ct))).Value!;
    }

    /// <summary>What content publishing will do from 2.4 on (ADR 0030 DECIDE): mark the image published.</summary>
    private static Task PublishAsync(MediaTestHost host, Guid id) => host.ExecuteAsync(context =>
        context.Set<MediaAsset>().Where(asset => asset.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(asset => asset.Visibility, MediaVisibility.Published), Ct));

    private static Task<OperationResult<MediaContentServiceModel>> OpenAsync(
        MediaTestHost host, Guid id, string rendition, MediaViewer viewer, string? version = null, string? ifNoneMatch = null) =>
        host.WithFacadeAsync(facade => facade.OpenAsync(
            new MediaDeliveryViewModel(id, rendition, version, ifNoneMatch, HeadOnly: false), viewer, Ct));
}
