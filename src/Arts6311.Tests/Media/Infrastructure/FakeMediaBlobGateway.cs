using System.Collections.Concurrent;
using Arts6311.Domain.Modules.Media.Gateways;

namespace Arts6311.Tests.Media.Infrastructure;

/// <summary>
/// The <c>media</c> container in memory. Counts every write and delete, so "one external effect" can be asserted
/// rather than inferred, and can be told to fail the way an unreachable storage account does.
/// </summary>
internal sealed class FakeMediaBlobGateway : IMediaBlobGateway
{
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> blobs = new(StringComparer.Ordinal);
    private int uploads;
    private int deletes;

    /// <summary>When set, every call throws this, as the Azure SDK would — with a message naming the account.</summary>
    public Exception? Throw { get; set; }

    /// <summary>When set, only deletes throw this.</summary>
    public Exception? ThrowOnDelete { get; set; }

    /// <summary>Runs before each upload is stored, once: a hook for interleaving another request mid-run.</summary>
    public Func<string, Task>? BeforeUpload { get; set; }

    public int Uploads => uploads;

    public int Deletes => deletes;

    public IReadOnlyCollection<string> Keys => [.. blobs.Keys];

    public byte[] Bytes(string key) => blobs[key].Bytes;

    public string ContentType(string key) => blobs[key].ContentType;

    public void Put(string key, byte[] bytes, string contentType) => blobs[key] = (bytes, contentType);

    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        Fail();
        if (BeforeUpload is { } hook)
        {
            BeforeUpload = null;
            await hook(key);
        }

        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        blobs[key] = (copy.ToArray(), contentType);
        Interlocked.Increment(ref uploads);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        Fail();
        return Task.FromResult(blobs.ContainsKey(key));
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        Fail();
        return Task.FromResult<Stream?>(blobs.TryGetValue(key, out var blob) ? new MemoryStream(blob.Bytes, writable: false) : null);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Fail();
        if (ThrowOnDelete is { } exception)
        {
            throw exception;
        }

        if (blobs.TryRemove(key, out _))
        {
            Interlocked.Increment(ref deletes);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken)
    {
        Fail();
        return Task.FromResult<IReadOnlyList<string>>([.. blobs.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal))]);
    }

    private void Fail()
    {
        if (Throw is { } exception)
        {
            throw exception;
        }
    }
}
