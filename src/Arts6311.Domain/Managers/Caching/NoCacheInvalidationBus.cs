namespace Arts6311.Domain.Managers.Caching;

/// <summary>
/// The bus of a host with no shared cache (MigrationService, tests): one process, so there is no one to tell.
/// <see cref="CachingServiceCollectionExtensions.AddArtsCacheInvalidation"/> replaces it where Redis is wired.
/// </summary>
internal sealed class NoCacheInvalidationBus : ICacheInvalidationBus
{
    public ValueTask PublishAsync(string tag, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask<IAsyncDisposable> SubscribeAsync(
        Func<string, CancellationToken, ValueTask> onTag, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IAsyncDisposable>(Nothing.Instance);

    private sealed class Nothing : IAsyncDisposable
    {
        public static readonly Nothing Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
