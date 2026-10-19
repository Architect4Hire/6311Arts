using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Arts6311.Gateway.Session;

/// <summary>
/// Tickets in a key-value store, keyed by the <strong>hash</strong> of the cookie's value.
/// </summary>
/// <remarks>
/// <para>
/// The cookie carries 256 bits from <see cref="RandomNumberGenerator"/> and the store keys on its SHA-256, the
/// same reasoning that puts a hash rather than a password in a user table: whoever reads the store — a dump, a
/// DbGate window, a support session — cannot turn what they see back into a usable cookie. The hash needs no
/// salt or stretching because the input is already full-entropy and uniformly random, so there is nothing to
/// guess and no dictionary to run.
/// </para>
/// <para>
/// <strong>The idle window is the key's time to live, and the absolute limit is on the ticket.</strong> Each
/// request resets the TTL, so an abandoned session disappears on its own rather than waiting for a sweep; the
/// TTL is also capped at the absolute deadline, so an expired ticket cannot sit in the store being read and
/// rejected.
/// </para>
/// </remarks>
internal sealed class SessionTicketStore(ISessionKeyValueStore store) : ISessionTicketStore
{
    /// <summary>Namespaced: Redis is shared with the API's HybridCache L2 (ADR 0009).</summary>
    private const string KeyPrefix = "6311:session:";

    /// <summary>32 bytes. The cookie value is the only thing standing between a thief and the session.</summary>
    private const int TicketKeyBytes = 32;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<string> CreateAsync(
        SessionTicket ticket, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        // Base64Url: a cookie value may not contain '+', '/' or '=' without escaping.
        var ticketKey = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TicketKeyBytes));

        await store.SetAsync(KeyFor(ticketKey), Serialize(ticket), timeToLive, cancellationToken);

        return ticketKey;
    }

    public async Task<SessionTicket?> GetAsync(string ticketKey, CancellationToken cancellationToken)
    {
        if (await store.GetAsync(KeyFor(ticketKey), cancellationToken) is not { Length: > 0 } stored)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SessionTicket>(stored, Json);
        }
        catch (JsonException)
        {
            // Unreadable means unusable: treat it as no session rather than throwing on a request the person
            // cannot fix. It happens if this record's shape changes while tickets from an older build are live.
            return null;
        }
    }

    public Task<bool> UpdateAsync(
        string ticketKey, SessionTicket ticket, TimeSpan timeToLive, CancellationToken cancellationToken) =>
        store.ReplaceAsync(KeyFor(ticketKey), Serialize(ticket), timeToLive, cancellationToken);

    public Task<bool> TouchAsync(string ticketKey, TimeSpan timeToLive, CancellationToken cancellationToken) =>
        store.ExpireAsync(KeyFor(ticketKey), timeToLive, cancellationToken);

    public Task RemoveAsync(string ticketKey, CancellationToken cancellationToken) =>
        store.RemoveAsync(KeyFor(ticketKey), cancellationToken);

    private static string Serialize(SessionTicket ticket) => JsonSerializer.Serialize(ticket, Json);

    private static string KeyFor(string ticketKey) =>
        KeyPrefix + Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(ticketKey)));
}
