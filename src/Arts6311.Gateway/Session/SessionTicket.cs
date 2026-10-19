namespace Arts6311.Gateway.Session;

/// <summary>
/// The server-side session (ADR 0002, ADR 0009). The browser holds only a reference to it.
/// </summary>
/// <remarks>
/// <para>
/// This record is what Redis stores, so everything on it is chosen: there is no email address, no password hash
/// and no token. <see cref="SecurityStamp"/> is the one sensitive field, and it is Identity's revocation marker
/// rather than a credential — it cannot be used to sign in.
/// </para>
/// <para>
/// <see cref="SessionId"/> is <strong>not</strong> the cookie's value. It is a separate, non-secret id that goes
/// into the internal token's <c>sid</c> claim and into logs, so the value that would let somebody resume the
/// session never leaves the browser and the ticket store.
/// </para>
/// </remarks>
/// <param name="AbsoluteExpiresAt">
/// When this session ends however active it is. Nothing moves it, which is what makes the absolute limit absolute
/// (ADR 0018).
/// </param>
/// <param name="LastValidatedAt">
/// When the security stamp was last checked against the API. The idle window is kept by the store's own
/// expiry, so this is the only clock the ticket carries besides the absolute one.
/// </param>
internal sealed record SessionTicket(
    string SessionId,
    string UserId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string SecurityStamp,
    DateTimeOffset CreatedAt,
    DateTimeOffset AbsoluteExpiresAt,
    DateTimeOffset LastValidatedAt);
