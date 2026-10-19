using Arts6311.Domain.Managers.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Kernel;

/// <summary>Runs against a real (in-memory) HybridCache, so the tag behaviour is HybridCache's own.</summary>
public sealed class CachingTests : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ServiceProvider services = new ServiceCollection().AddArtsCaching().BuildServiceProvider();

    private IAppCache Cache => services.GetRequiredService<IAppCache>();

    [Fact]
    public async Task TheFactoryRunsOncePerKey()
    {
        var calls = 0;
        ValueTask<string> Factory(CancellationToken _) => ValueTask.FromResult($"value-{++calls}");
        var ct = TestContext.Current.CancellationToken;

        var first = await Cache.GetOrCreateAsync(CacheKeys.Item("artist", "jane"), Factory, [], Lifetime, ct);
        var second = await Cache.GetOrCreateAsync(CacheKeys.Item("artist", "jane"), Factory, [], Lifetime, ct);

        Assert.Equal("value-1", first);
        Assert.Equal("value-1", second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RemovingATagInvalidatesEveryEntryCarryingIt()
    {
        var calls = 0;
        ValueTask<int> Factory(CancellationToken _) => ValueTask.FromResult(++calls);
        var ct = TestContext.Current.CancellationToken;
        string[] tags = [CacheTags.Type("artist"), CacheTags.Entity("artist", "jane")];

        await Cache.GetOrCreateAsync(CacheKeys.List("artist", "published"), Factory, tags, Lifetime, ct);
        await Cache.RemoveByTagAsync(CacheTags.Entity("artist", "jane"), ct);
        var after = await Cache.GetOrCreateAsync(CacheKeys.List("artist", "published"), Factory, tags, Lifetime, ct);

        Assert.Equal(2, after);
    }

    [Fact]
    public void KeysAndTagsHaveStableShapes()
    {
        Assert.Equal("artist:jane-doe", CacheKeys.Item("artist", "jane-doe"));
        Assert.Equal("artist:list:published", CacheKeys.List("artist", "published"));
        Assert.Equal("type:artist", CacheTags.Type("artist"));
        Assert.Equal("entity:artist:jane-doe", CacheTags.Entity("artist", "jane-doe"));
        Assert.Equal("site:/artists", CacheTags.SitePath("/artists"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("list:published")]
    public void EmptyOrSeparatorBearingSegmentsAreRejected(string segment)
    {
        Assert.Throws<ArgumentException>(() => CacheKeys.Item("artist", segment));
        Assert.Throws<ArgumentException>(() => CacheTags.Type(segment));
    }

    [Fact]
    public void AnItemKeyCannotForgeAListKey()
    {
        Assert.Throws<ArgumentException>(() => CacheKeys.Item("artist", "list:published"));
        Assert.Equal("artist:list:published", CacheKeys.List("artist", "published"));
    }

    [Fact]
    public void ASitePathMustBeRooted()
    {
        Assert.Throws<ArgumentException>(() => CacheTags.SitePath("artists"));
    }

    public void Dispose() => services.Dispose();
}
