using System.Text.RegularExpressions;

namespace Arts6311.Tests.Architecture;

/// <summary>
/// backend.md "Layer ownership" and "Defects", as a text scan of each module's <c>Business/</c> and <c>Facade/</c>
/// folders: the seam's upper layers never reach persistence, the outbox, a repository or a gateway themselves.
/// </summary>
/// <remarks>
/// <para>
/// Business calls only its DataLayer; staging an outbox message or an audit row is DataLayer bookkeeping (outbox.md:
/// "Never call Enqueue from Business, a facade or a controller"). A facade calls only Business — never a DataLayer,
/// a repository, a gateway or the <c>DbContext</c>.
/// </para>
/// <para>
/// Comments are stripped first, so prose that explains why a layer has no DataLayer is not a reference. A type
/// name ending in one of these words is enough to flag; the scan errs towards a false alarm a reviewer can wave
/// through rather than a quiet leak.
/// </para>
/// </remarks>
internal static partial class LayerRuleScanner
{
    public static IReadOnlyList<string> Violations(IEnumerable<DomainSourceFile> files)
    {
        var violations = new List<string>();

        foreach (var file in files)
        {
            var layer = LayerOf(file.RelativePath);
            if (layer is null)
            {
                continue;
            }

            var code = ModuleBoundaryScanner.CodeOnly(file.Text);
            var forbidden = layer == "Business" ? BusinessForbidden() : FacadeForbidden();

            violations.AddRange(forbidden.Matches(code)
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .Select(reference => $"{file.RelativePath}: {layer} references {reference}"));
        }

        return violations;
    }

    /// <summary><c>Business</c> or <c>Facade</c> for a file directly in a module's folder of that name, else null.</summary>
    private static string? LayerOf(string relativePath)
    {
        var match = LayerPath().Match(relativePath);
        return match.Success ? match.Groups["layer"].Value : null;
    }

    [GeneratedRegex(@"^Modules/\w+/(?<layer>Business|Facade)/[^/]+\.cs$")]
    private static partial Regex LayerPath();

    // Identity's own types too: UserManager and IdentityResult belong to the DataLayer, which maps them into this
    // module's terms (ADR 0023, 0028), so a Business that reads Identity's error codes has skipped a layer.
    [GeneratedRegex(@"\bMicrosoft\.(?:EntityFrameworkCore|AspNetCore\.Identity)\b|\b(?:\w*DbContext|IOutboxWriter|IAuditWriter|UserManager|IdentityResult|\w*Repository|I\w+Gateway)\b")]
    private static partial Regex BusinessForbidden();

    [GeneratedRegex(@"\bMicrosoft\.EntityFrameworkCore\b|\b(?:\w*DbContext|IOutboxWriter|IAuditWriter|UserManager|\w*Repository|I\w+Gateway|\w*DataLayer)\b")]
    private static partial Regex FacadeForbidden();
}
