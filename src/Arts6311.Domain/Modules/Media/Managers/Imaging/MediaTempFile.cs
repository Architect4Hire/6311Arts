namespace Arts6311.Domain.Modules.Media.Managers.Imaging;

/// <summary>
/// A scratch file that deletes itself when disposed — including when the process dies, because it is opened with
/// <see cref="FileOptions.DeleteOnClose"/>. Uploads and masters pass through these rather than memory, so a 25 MB
/// upload costs 25 MB of disk for a few seconds instead of 25 MB of heap.
/// </summary>
public sealed class MediaTempFile : IAsyncDisposable, IDisposable
{
    private MediaTempFile(FileStream stream) => Stream = stream;

    /// <summary>Readable, writable and seekable.</summary>
    public FileStream Stream { get; }

    public long Length => Stream.Length;

    public static MediaTempFile Create() => new(new FileStream(
        Path.Combine(Path.GetTempPath(), $"arts6311-media-{Guid.NewGuid():N}.tmp"),
        FileMode.CreateNew,
        FileAccess.ReadWrite,
        FileShare.None,
        bufferSize: 81920,
        FileOptions.DeleteOnClose | FileOptions.Asynchronous));

    /// <summary>
    /// Copies <paramref name="content"/> into a new temp file, stopping as soon as it passes
    /// <paramref name="maxBytes"/>, so an oversized upload is refused after reading one byte too many rather than
    /// all of it.
    /// </summary>
    /// <returns>The file positioned at 0, or <c>null</c> when the content was larger than allowed.</returns>
    public static async Task<MediaTempFile?> SpoolAsync(Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var file = Create();
        try
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    await file.DisposeAsync();
                    return null;
                }

                await file.Stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await file.Stream.FlushAsync(cancellationToken);
            file.Stream.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => Stream.DisposeAsync();

    public void Dispose() => Stream.Dispose();
}
