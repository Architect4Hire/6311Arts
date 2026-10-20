using Arts6311.Domain.Managers.Caching;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Revalidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// <c>site.revalidate-requested</c> and its handler (ADR 0036): which tags one event clears, that clearing them
/// twice has one effect, and that a payload the tag builders would throw on poisons the message instead.
/// </summary>
public sealed class RevalidationTests
{
    private static readonly Guid EntityId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void TheEventClearsTheTypeTheEntityAndEachDistinctPath()
    {
        var request = new SiteRevalidateRequested("artist", EntityId, ["/artists/jane-doe", "/artists/jane-doe", "/artists"]);

        var tags = SiteRevalidationFacade.TagsFor(request);

        Assert.Equal(
            [
                CacheTags.Type("artist"),
                CacheTags.Entity("artist", EntityId.ToString()),
                CacheTags.SitePath("/artists/jane-doe"),
                CacheTags.SitePath("/artists"),
            ],
            tags);
    }

    [Fact]
    public void AnEventWithNoPathsStillClearsTheTypeAndTheEntity()
    {
        var tags = SiteRevalidationFacade.TagsFor(new SiteRevalidateRequested("page", EntityId, []));

        Assert.Equal([CacheTags.Type("page"), CacheTags.Entity("page", EntityId.ToString())], tags);
    }

    [Fact]
    public async Task TheHandlerStopsACachedEntryBeingServedAndADeliveredTwiceCostsOneRefresh()
    {
        // A real HybridCache, so the invalidation is HybridCache's own (as CachingTests does).
        await using var services = new ServiceCollection().AddArtsCaching().BuildServiceProvider();
        var cache = services.GetRequiredService<IAppCache>();
        var handler = Handler(cache);
        var calls = 0;
        ValueTask<int> Factory(CancellationToken _) => ValueTask.FromResult(++calls);
        string[] tags = [CacheTags.Type("artist"), CacheTags.SitePath("/artists/jane-doe")];
        var key = CacheKeys.Item("artist", "jane-doe");
        var envelope = Envelope(new SiteRevalidateRequested("artist", EntityId, ["/artists/jane-doe"]));

        await cache.GetOrCreateAsync(key, Factory, tags, TimeSpan.FromMinutes(5), Ct);

        // At-least-once delivery: the same message arrives twice. Invalidating a tag is idempotent in the sense
        // that matters — it records "ignore anything older than now" rather than accumulating anything — so two
        // deliveries leave the same state and cost one refresh between them, not two.
        await handler.HandleAsync(envelope, Ct);
        await handler.HandleAsync(envelope, Ct);

        var afterInvalidation = await cache.GetOrCreateAsync(key, Factory, tags, TimeSpan.FromMinutes(5), Ct);
        var afterThat = await cache.GetOrCreateAsync(key, Factory, tags, TimeSpan.FromMinutes(5), Ct);

        Assert.Equal(2, afterInvalidation);
        Assert.Equal(2, afterThat);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AMalformedPayloadPoisonsTheMessageWithoutTouchingTheCache()
    {
        var cache = new ThrowingCache();
        var handler = Handler(cache);

        foreach (var payload in MalformedPayloads())
        {
            await Assert.ThrowsAsync<OutboxPoisonException>(() => handler.HandleAsync(
                new OutboxMessageEnvelope(
                    Guid.NewGuid(), SiteRevalidateRequested.MessageType, payload, Guid.NewGuid(), 1),
                Ct));
        }

        Assert.Equal(0, cache.Calls);
    }

    [Fact]
    public void TryParseRejectsWhatTheTagBuildersWouldThrowOn()
    {
        Assert.All(MalformedPayloads(), payload => Assert.Null(SiteRevalidateRequested.TryParse(payload)));

        var good = SiteRevalidateRequested.TryParse(
            new SiteRevalidateRequested("page", EntityId, ["/terms"]).Serialize());

        Assert.NotNull(good);
        Assert.Equal("page", good.EntityType);
        Assert.Equal(EntityId, good.EntityId);
        Assert.Equal(["/terms"], good.Paths);
    }

    [Fact]
    public async Task ACacheFailureIsRetriedWithOnlyTheExceptionTypeNamed()
    {
        var handler = Handler(new ThrowingCache { Fail = true });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            Envelope(new SiteRevalidateRequested("artist", EntityId, ["/artists/jane-doe"])), Ct));

        // outbox.md: LastError names the exception type and nothing a cache client would say about its host.
        Assert.Equal("Revalidation failed (TimeoutException).", failure.Message);
        Assert.Null(failure.InnerException);
    }

    private static SiteRevalidateRequestedHandler Handler(IAppCache cache) =>
        new SiteRevalidateRequestedHandler(
            new SiteRevalidationFacade(cache, NullLogger<SiteRevalidationFacade>.Instance),
            NullLogger<SiteRevalidateRequestedHandler>.Instance);

    private static OutboxMessageEnvelope Envelope(SiteRevalidateRequested request) =>
        new(Guid.NewGuid(), SiteRevalidateRequested.MessageType, request.Serialize(), Guid.NewGuid(), 1);

    private static string[] MalformedPayloads() =>
    [
        "",
        "   ",
        "not json",
        """{"entityType":"artist","entityId":"00000000-0000-0000-0000-000000000000","paths":[]}""",
        $$"""{"entityType":"","entityId":"{{EntityId}}","paths":[]}""",

        // A ':' in a segment, or an unrooted path: CacheTags throws on both, so the payload is poison, not a retry.
        $$"""{"entityType":"artist:x","entityId":"{{EntityId}}","paths":[]}""",
        $$"""{"entityType":"artist","entityId":"{{EntityId}}","paths":["artists/jane"]}""",
        $$"""{"entityType":"artist","entityId":"{{EntityId}}","paths":["/artists/jane:doe"]}""",
        $$"""{"entityType":"artist","entityId":"{{EntityId}}","paths":null}""",
        $$"""{"entityType":"artist","entityId":"{{EntityId}}","paths":[{{string.Join(',', Enumerable.Repeat("\"/a\"", SiteRevalidateRequested.MaxPaths + 1))}}]}""",
    ];

    /// <summary>A cache that counts what it was asked, and can fail the way a cache client fails.</summary>
    private sealed class ThrowingCache : IAppCache
    {
        public int Calls { get; private set; }

        public bool Fail { get; init; }

        public ValueTask<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> factory,
            IReadOnlyCollection<string> tags,
            TimeSpan lifetime,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask RemoveAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken)
        {
            Calls++;
            return Fail
                ? throw new TimeoutException("The cache at redis-cache.example:6379 did not answer.")
                : ValueTask.CompletedTask;
        }
    }
}
