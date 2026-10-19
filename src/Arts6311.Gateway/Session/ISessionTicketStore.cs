namespace Arts6311.Gateway.Session;

/// <summary>
/// The server-side ticket store (ADR 0009). Every method takes the <em>cookie's</em> value; what the store keys
/// on is its hash, which the implementation owns.
/// </summary>
internal interface ISessionTicketStore
{
    /// <summary>Creates a ticket and returns the opaque value the cookie carries.</summary>
    Task<string> CreateAsync(SessionTicket ticket, TimeSpan timeToLive, CancellationToken cancellationToken);

    Task<SessionTicket?> GetAsync(string ticketKey, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a ticket's contents — after a revalidation refreshed its roles — and its expiry. <c>false</c>
    /// when the ticket went away while we were revalidating it, which a concurrent logout does.
    /// </summary>
    Task<bool> UpdateAsync(
        string ticketKey, SessionTicket ticket, TimeSpan timeToLive, CancellationToken cancellationToken);

    /// <summary>
    /// Restarts the idle window without rewriting the ticket. <c>false</c> when the ticket is already gone.
    /// </summary>
    Task<bool> TouchAsync(string ticketKey, TimeSpan timeToLive, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the ticket. This is what makes logout real, and what makes a copied cookie worthless the moment
    /// the original signs out (<c>auth.md</c>).
    /// </summary>
    Task RemoveAsync(string ticketKey, CancellationToken cancellationToken);
}
