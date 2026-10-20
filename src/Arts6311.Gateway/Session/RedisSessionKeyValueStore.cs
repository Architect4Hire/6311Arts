using StackExchange.Redis;

namespace Arts6311.Gateway.Session;

/// <summary>
/// <see cref="ISessionKeyValueStore"/> on Redis (ADR 0009), which in run mode is the AppHost's <c>cache</c>
/// container and in Azure is whatever 6.1 chooses.
/// </summary>
/// <remarks>
/// Deliberately the thinnest possible layer: one call per method, no logic. <c>StackExchange.Redis</c> has no test double,
/// so everything above this class is tested over an in-memory store instead, and this one is covered by running
/// the system (<c>aspire run</c>) — the same split ADR 0022 recorded for YARP's request-size limit.
/// <para>
/// <c>IConnectionMultiplexer</c> comes from Aspire's <c>AddRedisClient</c>, which brings the retry policy, the
/// health check and the telemetry with it, so Redis being down shows up as an unhealthy gateway rather than as
/// slow sign-ins.
/// </para>
/// </remarks>
internal sealed class RedisSessionKeyValueStore(IConnectionMultiplexer redis) : ISessionKeyValueStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Database.StringGetAsync(key);
    }

    public async Task SetAsync(string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Database.StringSetAsync(key, value, timeToLive);
    }

    public async Task<bool> ReplaceAsync(
        string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // When.Exists: SET fails rather than recreating a key a concurrent logout has deleted.
        return await Database.StringSetAsync(key, value, timeToLive, When.Exists);
    }

    public async Task<bool> ExpireAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Returns false when the key does not exist, which is exactly "this session went idle and expired".
        return await Database.KeyExpireAsync(key, timeToLive);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Database.KeyDeleteAsync(key);
    }

    public async Task<bool> AddIfAbsentAsync(
        string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // When.NotExists: SET NX, so the check and the write are one command and a race has exactly one winner.
        return await Database.StringSetAsync(key, value, timeToLive, When.NotExists);
    }

    private IDatabase Database => redis.GetDatabase();
}
