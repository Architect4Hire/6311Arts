namespace Arts6311.Domain.Managers.Time;

/// <summary>
/// The single application clock. UTC only: local times are derived with an explicit IANA zone
/// (class sessions store <c>America/Chicago</c>), never the server's. <c>DateTime.UtcNow</c> and friends
/// are banned (src/BannedSymbols.txt).
/// </summary>
public interface IClock
{
    /// <summary>The current instant with a zero offset.</summary>
    DateTimeOffset UtcNow { get; }
}
