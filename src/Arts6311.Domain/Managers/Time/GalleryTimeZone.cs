namespace Arts6311.Domain.Managers.Time;

/// <summary>
/// The gallery's own time zone: the IANA id every time a visitor reads is shown in. Stored times stay UTC
/// (backend.md); this is what an API response sends as <c>displayTimeZone</c> so a client can show the wall-clock
/// time at 6311 N. Clark St. (api-contract.md "Times and money").
/// </summary>
public static class GalleryTimeZone
{
    public const string Id = "America/Chicago";
}
