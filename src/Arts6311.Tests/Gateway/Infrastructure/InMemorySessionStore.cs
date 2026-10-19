using Arts6311.Gateway.Session;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Gateway.Infrastructure;

/// <summary>
/// <see cref="ISessionKeyValueStore"/> in a dictionary, with Redis' expiry semantics against a fake clock.
/// </summary>
/// <remarks>
/// It stands in for Redis so the layers above it — key hashing, serialization, the idle window and the absolute
/// limit — are exercised exactly as shipped, while a test can advance 31 minutes without waiting. What it does
/// <em>not</em> cover is <c>RedisSessionKeyValueStore</c> itself, which is four calls with no logic and is
/// verified by running the system (ADR 0024).
/// </remarks>
internal sealed class InMemorySessionStore(FakeTimeProvider time) : ISessionKeyValueStore
{
    private readonly Dictionary<string, (string Value, DateTimeOffset ExpiresAt)> entries =
        new(StringComparer.Ordinal);

    /// <summary>Every key currently held, expiry applied. The assertion for "the ticket is really gone".</summary>
    public IReadOnlyCollection<string> Keys
    {
        get
        {
            Sweep();
            return [.. entries.Keys];
        }
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        Sweep();
        return Task.FromResult(entries.TryGetValue(key, out var entry) ? entry.Value : null);
    }

    public Task SetAsync(string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        entries[key] = (value, time.GetUtcNow() + timeToLive);
        return Task.CompletedTask;
    }

    public Task<bool> ReplaceAsync(
        string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        Sweep();

        if (!entries.ContainsKey(key))
        {
            return Task.FromResult(false);
        }

        entries[key] = (value, time.GetUtcNow() + timeToLive);
        return Task.FromResult(true);
    }

    public Task<bool> ExpireAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        Sweep();

        if (!entries.TryGetValue(key, out var entry))
        {
            return Task.FromResult(false);
        }

        entries[key] = (entry.Value, time.GetUtcNow() + timeToLive);
        return Task.FromResult(true);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        entries.Remove(key);
        return Task.CompletedTask;
    }

    /// <summary>Deletes a key behind the gateway's back, standing in for a concurrent logout.</summary>
    public void Forget(string key) => entries.Remove(key);

    /// <summary>What Redis does for itself: a key past its time to live is simply not there.</summary>
    private void Sweep()
    {
        var now = time.GetUtcNow();

        foreach (var expired in entries.Where(entry => entry.Value.ExpiresAt <= now).Select(entry => entry.Key).ToArray())
        {
            entries.Remove(expired);
        }
    }
}
