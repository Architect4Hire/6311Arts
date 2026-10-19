namespace Arts6311.Gateway.Session;

/// <summary>
/// The four Redis operations the session store needs, behind an interface.
/// </summary>
/// <remarks>
/// The seam exists so the ticket logic above it — key derivation, serialization and the two expiry clocks — is
/// exercised in tests without a Redis container, while the implementation below it stays thin enough to read.
/// <c>StackExchange.Redis</c>' <c>IDatabase</c> has hundreds of members and no usable fake, and this repo writes
/// fakes by hand rather than taking a mocking library.
/// </remarks>
internal interface ISessionKeyValueStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a value <strong>only if the key still exists</strong>, and says whether it did.
    /// </summary>
    /// <remarks>
    /// Not <see cref="SetAsync"/>: an unconditional write races with a logout. A request that read a ticket and
    /// then went away to revalidate it would recreate the ticket that the logout deleted in the meantime, and a
    /// copied cookie would come back to life — which is the one thing a server-side ticket exists to prevent.
    /// </remarks>
    Task<bool> ReplaceAsync(string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken);

    /// <summary>
    /// Resets a key's time to live. <c>false</c> when there is no such key — which is how an idle session is
    /// noticed: the key simply expired.
    /// </summary>
    Task<bool> ExpireAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken);

    Task RemoveAsync(string key, CancellationToken cancellationToken);
}
