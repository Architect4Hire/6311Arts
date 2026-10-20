namespace Arts6311.Domain.Modules.Redirects.Managers;

/// <summary>
/// The audit vocabulary this module writes. Every redirect change is a sensitive operation (auth.md), audited with
/// the old and new addresses.
/// </summary>
internal static class RedirectAudit
{
    public const string ResourceType = "redirect";

    public const string Created = "redirect.created";

    public const string Updated = "redirect.updated";

    public const string Deleted = "redirect.deleted";

    /// <summary>One slug change applied: the redirects it added, rewrote and retired, in one entry.</summary>
    public const string SlugChanged = "redirect.slug-changed";

    public const string Seeded = "redirect.seeded";
}
