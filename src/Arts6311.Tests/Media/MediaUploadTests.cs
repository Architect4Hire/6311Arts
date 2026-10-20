using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.Imaging;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Media;

/// <summary>
/// An upload through the real facade, Business and DataLayer: refused for what the bytes are, never for what the
/// name says; stored as a compressed master with GPS gone; and committed together with its rendition request.
/// </summary>
public sealed class MediaUploadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("png-named-jpg", MediaErrorCodes.ExtensionMismatch)]
    [InlineData("svg-named-png", MediaErrorCodes.TypeUnsupported)]
    [InlineData("html-named-jpg", MediaErrorCodes.TypeUnsupported)]
    [InlineData("animated-png", MediaErrorCodes.TypeUnsupported)]
    public async Task AForgedOrUnsupportedFileIsRefused(string kind, string code)
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var (bytes, name) = kind switch
        {
            "png-named-jpg" => (ImageFixtures.Png(32, 32), "photo.jpg"),
            "svg-named-png" => (ImageFixtures.Svg(), "logo.png"),
            "html-named-jpg" => ("<html><script>alert(1)</script></html>"u8.ToArray(), "photo.jpg"),
            _ => (ImageFixtures.AnimatedPng(), "spinner.png"),
        };

        var result = await host.UploadAsync(bytes, name, cancellationToken: Ct);

        Assert.Equal(code, result.Error?.Code);
        await AssertNothingStoredAsync(host);
    }

    [Fact]
    public async Task AnOversizedFileIsRefusedBeforeItIsReadToTheEnd()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var stream = new EndlessStream();

        var result = await host.WithFacadeAsync(facade => facade.UploadAsync(
            new MediaUploadViewModel("huge.jpg", stream, null, null, null), MediaTestHost.EditorId, Ct));

        Assert.Equal(MediaErrorCodes.TooLarge, result.Error?.Code);

        // Stopped one buffer past the limit, not at the end of a stream that has no end.
        Assert.InRange(stream.BytesRead, MediaPolicy.MaxUploadBytes, MediaPolicy.MaxUploadBytes + (1024 * 1024));
        await AssertNothingStoredAsync(host);
    }

    [Theory]
    [InlineData(20_000u, 100u)]
    [InlineData(8_000u, 8_000u)]
    [InlineData(8u, 400u)]
    public async Task DimensionsOutsideTheLimitsAreRefusedFromTheHeader(uint width, uint height)
    {
        // The header-only PNG has no pixels: refusing it proves nothing tried to decode it.
        await using var host = await MediaTestHost.CreateAsync(Ct);

        var result = await host.UploadAsync(ImageFixtures.PngHeaderOnly(width, height), "big.png", cancellationToken: Ct);

        Assert.Equal(MediaErrorCodes.DimensionsOutOfRange, result.Error?.Code);
    }

    [Fact]
    public async Task APictureThatDoesNotDecodeIsRefused()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var jpeg = ImageFixtures.Jpeg(300, 200);

        var result = await host.UploadAsync(jpeg[..(jpeg.Length / 2)], "cut.jpg", cancellationToken: Ct);

        Assert.Equal(MediaErrorCodes.Undecodable, result.Error?.Code);
        await AssertNothingStoredAsync(host);
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task TheStoredMasterHasNoGpsAndKeepsTheCopyright(string kind)
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var exif = ImageFixtures.ExifWithGps();
        var (bytes, name) = kind switch
        {
            "jpeg" => (ImageFixtures.Jpeg(640, 480, exifTiff: exif, xmp: true), "photo.jpg"),
            "png" => (ImageFixtures.Png(320, 240, exifTiff: exif, xmp: true), "scan.png"),
            _ => (ImageFixtures.Webp(320, 240, exifTiff: exif, xmp: true), "photo.webp"),
        };

        var result = await host.UploadAsync(bytes, name, cancellationToken: Ct);

        Assert.True(result.Succeeded, result.Error?.Message);
        var original = await host.OriginalAsync(result.Value!.Id);
        var stored = host.Blobs.Bytes(original.BlobKey);

        Assert.False(ImageFixtures.ContainsLeak(stored));
        Assert.Equal(ImageFixtures.Copyright, ImageProbe.Probe(new MemoryStream(stored))!.Exif.Copyright);
        Assert.Equal(original.MimeType, host.Blobs.ContentType(original.BlobKey));

        // The credit defaults to the file's own copyright, so attribution survives without retyping it.
        Assert.Equal(ImageFixtures.Copyright, original.Credit);
    }

    [Fact]
    public async Task ALargeUploadIsStoredAsASmallerMasterNoLongerThanTheCap()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var bytes = ImageFixtures.Jpeg(4000, 3000, quality: 98);

        var result = await host.UploadAsync(bytes, "camera.jpg", cancellationToken: Ct);

        Assert.True(result.Succeeded, result.Error?.Message);
        var asset = result.Value!;
        Assert.Equal((2560, 1920), (asset.Width, asset.Height));
        Assert.Equal(bytes.Length, asset.UploadedBytes);
        Assert.True(asset.Bytes < bytes.Length, $"{asset.Bytes} bytes stored for a {bytes.Length}-byte upload.");

        var stored = host.Blobs.Bytes((await host.OriginalAsync(asset.Id)).BlobKey);
        var probe = ImageProbe.Probe(new MemoryStream(stored))!;
        Assert.Equal((2560, 1920), (probe.Width, probe.Height));
        Assert.Equal(MediaImageFormat.Jpeg, probe.Format);
    }

    [Fact]
    public async Task AnAlreadyCompactJpegIsKeptRatherThanCompressedAgain()
    {
        // Re-encoding a quality-60 JPEG at 92 makes it bigger and adds a generation of loss for nothing; the
        // scrubbed upload itself is the master.
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var bytes = ImageFixtures.Jpeg(800, 600, quality: 60);

        var result = await host.UploadAsync(bytes, "small.jpg", cancellationToken: Ct);

        Assert.True(result.Succeeded, result.Error?.Message);
        var stored = host.Blobs.Bytes((await host.OriginalAsync(result.Value!.Id)).BlobKey);
        using var scrubbed = new MemoryStream();
        ImageMetadataRewriter.Rewrite(new MemoryStream(bytes), MediaImageFormat.Jpeg, ExifMetadata.None, scrubbed);
        Assert.Equal(scrubbed.ToArray(), stored);
    }

    [Fact]
    public async Task APngMasterIsLosslessAtTheSameSize()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var bytes = ImageFixtures.Png(600, 400, alpha: true);

        var result = await host.UploadAsync(bytes, "logo.png", cancellationToken: Ct);

        Assert.True(result.Succeeded, result.Error?.Message);
        var stored = host.Blobs.Bytes((await host.OriginalAsync(result.Value!.Id)).BlobKey);
        Assert.Equal(ImageFixtures.Pixels(bytes), ImageFixtures.Pixels(stored));
        Assert.True(stored.Length <= bytes.Length);
    }

    [Fact]
    public async Task AnOrientedPhotoIsStoredTheRightWayUp()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var bytes = ImageFixtures.Jpeg(3000, 2000, exifTiff: ImageFixtures.ExifWithGps(orientation: 6));

        var result = await host.UploadAsync(bytes, "portrait.jpg", cancellationToken: Ct);

        // Displayed 2000 × 3000, downscaled so the long side is 2560, and the orientation is now in the pixels.
        Assert.Equal((1707, 2560), (result.Value!.Width, result.Value.Height));
        var stored = host.Blobs.Bytes((await host.OriginalAsync(result.Value.Id)).BlobKey);
        Assert.Equal(1, ImageProbe.Probe(new MemoryStream(stored))!.Exif.Orientation);
    }

    [Fact]
    public async Task TheSameImageTwiceIsADuplicateThatNamesTheFirst()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var bytes = ImageFixtures.Jpeg(400, 300);
        var first = await host.UploadAsync(bytes, "a.jpg", cancellationToken: Ct);

        var second = await host.UploadAsync(bytes, "a-copy.jpg", cancellationToken: Ct);

        Assert.Equal(MediaErrorCodes.Duplicate, second.Error?.Code);
        Assert.Equal(first.Value!.Id, second.Error!.Extensions!["existingAssetId"]);
    }

    [Fact]
    public async Task AltTextThatIsJustTheFileNameIsRefused()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);

        var result = await host.UploadAsync(ImageFixtures.Jpeg(200, 100), "IMG_2041.jpg", altText: "IMG_2041", cancellationToken: Ct);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error?.Code);
        Assert.True(result.Error!.FieldErrors.ContainsKey("altText"));
    }

    [Fact]
    public async Task TheAssetTheAuditRowAndTheRenditionRequestCommitTogether()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);

        var result = await host.UploadAsync(ImageFixtures.Jpeg(400, 300), "a.jpg", altText: "A red square on a gradient", cancellationToken: Ct);

        Assert.True(result.Succeeded, result.Error?.Message);
        var (message, audit) = await host.QueryAsync(async context => (
            await context.OutboxMessages.SingleAsync(Ct),
            await context.AuditLogs.SingleAsync(Ct)));

        Assert.Equal(MediaRenditionRequested.MessageType, message.Type);
        Assert.Equal(new MediaRenditionRequested(result.Value!.Id), MediaRenditionRequested.TryParse(message.PayloadJson));
        Assert.Equal(MediaAudit.Uploaded, audit.Action);
        Assert.Equal(audit.CorrelationId, message.CorrelationId);

        // Ids only (outbox.md): the payload says which asset, and nothing about it.
        Assert.Equal($"{{\"assetId\":\"{result.Value.Id}\"}}", message.PayloadJson);
        Assert.Equal(MediaProcessingStatus.Pending, result.Value.ProcessingStatus);
        Assert.Equal(MediaVisibility.Draft, result.Value.Visibility);
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheAssetNorTheRenditionRequest()
    {
        // outbox.md's central guarantee, against a real database: half a commit would be an asset that never gets
        // renditions, or a message for an asset that does not exist.
        var failure = new FailInsertInto("OutboxMessages");
        await using var host = await MediaTestHost.CreateAsync(Ct, interceptor: failure);

        await Assert.ThrowsAnyAsync<Exception>(() => host.UploadAsync(ImageFixtures.Jpeg(400, 300), "a.jpg", cancellationToken: Ct));

        Assert.True(failure.Fired, "The interceptor never fired, so the save was not actually broken.");

        // The master's blob went up before the save; the failed save takes it down again.
        await AssertNothingStoredAsync(host);
    }

    private static async Task AssertNothingStoredAsync(MediaTestHost host, bool blobsToo = true)
    {
        await host.ExecuteAsync(async context =>
        {
            Assert.False(await context.Set<MediaAsset>().AnyAsync(Ct));
            Assert.False(await context.OutboxMessages.AnyAsync(Ct));
            Assert.False(await context.Set<AuditLog>().AnyAsync(Ct));
        });

        if (blobsToo)
        {
            Assert.Empty(host.Blobs.Keys);
        }
    }

    /// <summary>A body that never ends, as a hostile client would send. Counts what was read from it.</summary>
    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)0xFF, offset, count);
            BytesRead += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
