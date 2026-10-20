using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Redirects.Business;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Redirects.Managers;

/// <summary>
/// The spike's fixed legacy redirects (<c>projects/site/src/server.ts</c>: WordPress addresses the new site doesn't
/// have), seeded in every environment so the site can read them from the API in Phase 3 rather than from code (ADR 0035).
/// </summary>
/// <remarks>
/// Only the fixed ones. The pattern rules (<c>/product/{slug}</c>, <c>/artist/{slug}</c>) depend on which slugs exist
/// and stay in the site's code. Idempotent, and once per database: it runs only if it never has (its audit entry is the
/// marker), so a redirect staff edited or deleted stays as they left it. A seed that would break the one-hop rules is
/// skipped.
/// </remarks>
internal sealed partial class LegacyRedirectSeeder(
    IRedirectsBusiness business,
    ILogger<LegacyRedirectSeeder> logger) : IDataSeeder
{
    public const string SeedActor = "system:redirect-seed";

    internal static readonly IReadOnlyList<(string FromPath, string ToPath)> Legacy =
    [
        ("/events", "/classes"),
        ("/gallery", "/exhibitions"),
        ("/submit-your-work", "/submit"),
    ];

    public string Name => "Legacy redirects";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var added = await business.SeedAsync(Legacy, SeedActor, cancellationToken);
        LogSeeded(added);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} legacy redirects.")]
    private partial void LogSeeded(int count);
}
