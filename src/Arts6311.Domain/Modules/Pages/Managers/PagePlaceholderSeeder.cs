using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Pages.Business;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Pages.Managers;

/// <summary>
/// Makes sure the privacy policy and the terms exist, in every environment, as Drafts with placeholder text (prompt 2.6,
/// ADR 0034). The site's footer links to <c>/privacy</c> and <c>/terms</c>; staff write the real text and publish it.
/// </summary>
/// <remarks>
/// Drafts, so a legal placeholder can never go public by accident. Idempotent as <see cref="IDataSeeder"/> requires:
/// a slug already in use is left alone, so a rerun adds nothing and the policy staff wrote is never overwritten.
/// <c>UpdatedBy</c> is <see cref="SeedActor"/>, so the importer (4.x) can tell an untouched placeholder from staff work.
/// </remarks>
internal sealed partial class PagePlaceholderSeeder(
    IPagesBusiness business,
    ILogger<PagePlaceholderSeeder> logger) : IDataSeeder
{
    public const string SeedActor = "system:placeholder-seed";

    /// <summary>The slugs match the site's routes, <c>/privacy</c> and <c>/terms</c>; the titles, its page titles.</summary>
    internal static readonly IReadOnlyList<PagePlaceholder> Placeholders =
    [
        new(
            "Privacy Policy",
            "privacy",
            "<h2>Placeholder</h2><p>This privacy policy has not been written yet. Replace this text with "
            + "6311arts’ privacy policy, then publish the page.</p>"
            + "<p>Questions in the meantime: info@6311arts.com.</p>"),
        new(
            "Terms and Conditions",
            "terms",
            "<h2>Placeholder</h2><p>These terms have not been written yet. Replace this text with 6311arts’ terms "
            + "and conditions, then publish the page.</p>"
            + "<p>Questions in the meantime: info@6311arts.com.</p>"),
    ];

    public string Name => "Placeholder pages";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var added = await business.SeedAsync(Placeholders, SeedActor, cancellationToken);
        LogSeeded(added);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} placeholder pages.")]
    private partial void LogSeeded(int count);
}
