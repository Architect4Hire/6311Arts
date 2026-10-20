using Arts6311.Domain.Modules.Media.Managers;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Arts6311.Domain.Modules.Media.Gateways;

/// <summary>
/// <see cref="IMediaBlobGateway"/> over the Aspire-registered <see cref="BlobServiceClient"/> ("blobs"): Azurite in
/// run mode, Azure Storage when published. The container is declared by the AppHost and never created here.
/// </summary>
/// <remarks>
/// Azure SDK exceptions are left to propagate: their messages name the account and the request, so every caller
/// that could hand one to <c>OutboxMessage.LastError</c> or a response wraps it in a type-only message first.
/// </remarks>
internal sealed class AzureMediaBlobGateway(BlobServiceClient blobs) : IMediaBlobGateway
{
    private BlobContainerClient Container => blobs.GetBlobContainerClient(MediaPolicy.ContainerName);

    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await Container.GetBlobClient(key).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) =>
        (await Container.GetBlobClient(key).ExistsAsync(cancellationToken)).Value;

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await Container.GetBlobClient(key).OpenReadAsync(
                new BlobOpenReadOptions(allowModifications: false), cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        await Container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        await foreach (var item in Container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
        {
            keys.Add(item.Name);
        }

        return keys;
    }
}
