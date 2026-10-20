namespace Arts6311.Domain.Modules.Media.Gateways;

/// <summary>
/// The private <c>media</c> blob container (media.md "Never public blob access"). Only the API reads it, and only
/// to stream an asset back through <c>/media/**</c>; nothing outside the system ever gets a blob URL.
/// </summary>
internal interface IMediaBlobGateway
{
    /// <summary>
    /// Writes a blob, replacing one already at the key. Keys are content-addressed, so a replacement writes the same
    /// bytes: that is what makes a redelivered rendition message harmless.
    /// </summary>
    Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    /// <summary>A read stream positioned at the start, or <c>null</c> when there is no such blob.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deletes one blob; absent is success.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>Every blob key under a prefix.</summary>
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken);
}
