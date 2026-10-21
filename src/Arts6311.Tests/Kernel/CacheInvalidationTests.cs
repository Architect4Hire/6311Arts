using Arts6311.Domain.Managers.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// Two processes, one L2 (ADR 0049): the Worker removes a tag and the API stops serving what carried it. Each side is
/// its own service provider with its own HybridCache, as the hosts are; they share an L2 and a bus, as the hosts share
/// Redis. Prompt 3.4 found this path broken live while every single-process test passed.
/// </summary>
public sealed class CacheInvalidationTests : IAsyncDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private static readonly string Tag = CacheTags.Type("class-offering");
    private static readonly string Key = CacheKeys.List("class-offering", "site");

    private readonly IDistributedCache l2 = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly Hub hub = new();
    private readonly List<ServiceProvider> processes = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AWorkerInvalidationReachesTheApiEvenAfterTheApiHasReadTheTag()
    {
        var api = await StartAsync();
        var worker = await StartAsync();
        var version = 0;
        ValueTask<int> Read(CancellationToken _) => ValueTask.FromResult(++version);

        Assert.Equal(1, await Cache(api).GetOrCreateAsync(Key, Read, [Tag], Lifetime, Ct));
        await Cache(worker).RemoveByTagAsync(Tag, Ct);
        Assert.Equal(2, await Cache(api).GetOrCreateAsync(Key, Read, [Tag], Lifetime, Ct));

        // The second time is the one that failed live: by now the API has the tag's time and never reads it again.
        await Cache(worker).RemoveByTagAsync(Tag, Ct);
        Assert.Equal(3, await Cache(api).GetOrCreateAsync(Key, Read, [Tag], Lifetime, Ct));
    }

    [Fact]
    public async Task WithoutTheListenerTheApiKeepsServingItsCopy()
    {
        // The control: the shared L2 alone does not carry the invalidation across, which is why the bus exists.
        var api = await StartAsync(listen: false);
        var worker = await StartAsync();
        var version = 0;
        ValueTask<int> Read(CancellationToken _) => ValueTask.FromResult(++version);

        await Cache(api).GetOrCreateAsync(Key, Read, [Tag], Lifetime, Ct);
        await Cache(worker).RemoveByTagAsync(Tag, Ct);

        Assert.Equal(1, await Cache(api).GetOrCreateAsync(Key, Read, [Tag], Lifetime, Ct));
    }

    [Fact]
    public async Task OnlyTheProcessThatRemovedATagPublishesIt()
    {
        var api = await StartAsync();
        var worker = await StartAsync();

        await Cache(worker).RemoveByTagAsync(Tag, Ct);

        // The API applied it without publishing it back, so nothing echoes between processes.
        var published = Assert.Single(hub.Published);
        Assert.Equal((BusOf(worker), Tag), published);
        Assert.NotSame(BusOf(api), published.Publisher);
    }

    [Fact]
    public async Task AFailedPublishThrowsSoTheRevalidationIsRetried()
    {
        hub.FailPublishing = true;
        var worker = await StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Cache(worker).RemoveByTagAsync(Tag, Ct).AsTask());
    }

    [Fact]
    public async Task AHostWithoutRedisHasNothingToTell()
    {
        await using var single = new ServiceCollection().AddArtsCaching().BuildServiceProvider();

        await single.GetRequiredService<IAppCache>().RemoveByTagAsync(Tag, Ct);

        Assert.Empty(single.GetServices<IHostedService>());
    }

    [Fact]
    public void TheRedisMessageCarriesTheTagToEveryProcessButItsSender()
    {
        var message = RedisCacheInvalidationBus.Format("worker-1", Tag);

        Assert.Equal(Tag, RedisCacheInvalidationBus.TagFrom(message, "api-1"));
        Assert.Null(RedisCacheInvalidationBus.TagFrom(message, "worker-1"));

        // A site path tag survives as it was sent: only the first line break separates.
        Assert.Equal("site:/classes/screen-printing",
            RedisCacheInvalidationBus.TagFrom(RedisCacheInvalidationBus.Format("w", "site:/classes/screen-printing"), "a"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData("\ntype:class-offering")]
    [InlineData("worker-1\n")]
    public void AMessageThatIsNotOursIsIgnored(string? message) =>
        Assert.Null(RedisCacheInvalidationBus.TagFrom(message, "api-1"));

    [Fact]
    public async Task AFailedSubscribeStopsTheHostWithoutNamingTheServer()
    {
        var listener = new CacheInvalidationListener(
            new Unreachable(),
            new ServiceCollection().AddArtsCaching().BuildServiceProvider()
                .GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CacheInvalidationListener>.Instance);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => listener.StartAsync(Ct));

        Assert.DoesNotContain("redis.internal", failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(TimeoutException), failure.Message, StringComparison.Ordinal);
    }

    private async Task<ServiceProvider> StartAsync(bool listen = true)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(l2)
            .AddArtsCaching();
        if (listen)
        {
            services.AddArtsCacheInvalidation();
        }

        // AddArtsCacheInvalidation's wiring, over the test bus rather than Redis.
        services.Replace(ServiceDescriptor.Singleton<ICacheInvalidationBus>(new Bus(hub)));

        var provider = services.BuildServiceProvider();
        processes.Add(provider);
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(Ct);
        }

        return provider;
    }

    private static IAppCache Cache(ServiceProvider process) => process.GetRequiredService<IAppCache>();

    private static Bus BusOf(ServiceProvider process) => (Bus)process.GetRequiredService<ICacheInvalidationBus>();

    public async ValueTask DisposeAsync()
    {
        foreach (var process in processes)
        {
            foreach (var hosted in process.GetServices<IHostedService>())
            {
                await hosted.StopAsync(CancellationToken.None);
            }

            await process.DisposeAsync();
        }
    }

    /// <summary>A Redis that times out, with the server named in the message as StackExchange.Redis does.</summary>
    private sealed class Unreachable : ICacheInvalidationBus
    {
        public ValueTask PublishAsync(string tag, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<IAsyncDisposable> SubscribeAsync(
            Func<string, CancellationToken, ValueTask> onTag, CancellationToken cancellationToken) =>
            throw new TimeoutException("Timeout performing SUBSCRIBE on redis.internal:6380.");
    }

    /// <summary>Redis's channel, in memory: every subscriber but the publisher hears each tag.</summary>
    private sealed class Hub
    {
        public List<(Bus Publisher, string Tag)> Published { get; } = [];

        public List<(Bus Owner, Func<string, CancellationToken, ValueTask> OnTag)> Subscribers { get; } = [];

        public bool FailPublishing { get; set; }
    }

    private sealed class Bus(Hub hub) : ICacheInvalidationBus
    {
        public async ValueTask PublishAsync(string tag, CancellationToken cancellationToken)
        {
            if (hub.FailPublishing)
            {
                throw new InvalidOperationException("The channel is unreachable.");
            }

            hub.Published.Add((this, tag));
            foreach (var (owner, onTag) in hub.Subscribers.ToList())
            {
                if (!ReferenceEquals(owner, this))
                {
                    await onTag(tag, cancellationToken);
                }
            }
        }

        public ValueTask<IAsyncDisposable> SubscribeAsync(
            Func<string, CancellationToken, ValueTask> onTag, CancellationToken cancellationToken)
        {
            var subscription = (this, onTag);
            hub.Subscribers.Add(subscription);
            return ValueTask.FromResult<IAsyncDisposable>(new Unsubscribe(() => hub.Subscribers.Remove(subscription)));
        }

        private sealed class Unsubscribe(Action remove) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                remove();
                return ValueTask.CompletedTask;
            }
        }
    }
}
