using System.Text.RegularExpressions;

namespace Arts6311.Tests.Architecture;

/// <summary>One Domain source file: its path relative to <c>src/Arts6311.Domain/</c> (forward slashes) and its text.</summary>
internal sealed record DomainSourceFile(string RelativePath, string Text);

/// <summary>
/// The module boundary rules from backend.md, checked against source text. Text rather than reflection: a
/// <c>using</c> directive or a fully qualified name is exactly how one module reaches into another, and it is
/// visible before any type exists to reflect over.
/// </summary>
/// <remarks>
/// <para>
/// Because it reads text, the scanner also closes the ways text can hide a reference: namespace aliases into the
/// Domain, relative names (<c>Modules.X</c> or <c>Domain.Modules.X</c> resolved from an enclosing namespace),
/// <c>global using</c> directives, global <c>&lt;Using&gt;</c> items in project files, and files placed outside
/// the three folders the rules know about.
/// </para>
/// <para>
/// Known gap: backend.md permits another module's entity types only where a foreign key already crosses. A text
/// scan cannot see foreign keys, so <c>.Data.Entities</c> is permitted for every module; the architecture
/// reviewer checks each such use.
/// </para>
/// </remarks>
internal static partial class ModuleBoundaryScanner
{
    /// <summary>What may cross from one module into another (backend.md "Module rules"), matched on namespace boundaries.</summary>
    public static readonly string[] PermittedForeignNamespaces = [".Facade", ".Managers.ServiceModels", ".Data.Entities"];

    /// <summary>Kernel files allowed to name modules: the one DbContext.</summary>
    public static readonly string[] KernelExemptions = ["Managers/Persistence/ArtsDbContext.cs"];

    /// <summary>The only top-level folders Domain source may live in.</summary>
    public static readonly string[] KnownRoots = ["Managers/", "Modules/", "Migrations/"];

    /// <summary>Layer-first folders that must never appear at the Domain root.</summary>
    public static readonly string[] ForbiddenRootFolders = ["Models", "Validation", "Facade", "Business", "Data", "Repositories"];

    public static IReadOnlyList<string> Violations(IEnumerable<DomainSourceFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var violations = new List<string>();
        foreach (var file in files)
        {
            var path = file.RelativePath;
            var code = CodeOnly(file.Text);

            if (!KnownRoots.Any(root => path.StartsWith(root, StringComparison.Ordinal)))
            {
                violations.Add($"{path}: Domain source must live under {string.Join(", ", KnownRoots)} (exact case)");
                continue;
            }

            violations.AddRange(DomainAlias().Matches(code).Select(match =>
                $"{path}: namespace alias into the Domain ({match.Value.Trim()}) hides module references; use the full namespace"));

            violations.AddRange(GlobalUsing().Matches(code).Select(match =>
                $"{path}: global using of a Domain namespace ({match.Value.Trim()}) leaks it into every file, kernel included"));

            if (path.StartsWith("Migrations/", StringComparison.Ordinal))
            {
                continue; // Generated; the snapshot names entity types as strings.
            }

            if (path.StartsWith("Managers/", StringComparison.Ordinal))
            {
                if (!KernelExemptions.Contains(path, StringComparer.Ordinal))
                {
                    violations.AddRange(ModuleReference().Matches(code).Select(match =>
                        $"{path}: the shared kernel names module '{match.Groups["module"].Value}' ({match.Value})"));
                }

                continue;
            }

            var owner = OwningModule(path);
            if (owner is null)
            {
                violations.Add($"{path}: a file directly under Modules/ belongs to no module");
                continue;
            }

            foreach (Match match in ModuleReference().Matches(code))
            {
                var target = match.Groups["module"].Value;
                var rest = match.Groups["rest"].Value;
                if (!string.Equals(target, owner, StringComparison.Ordinal) && !IsPermitted(rest))
                {
                    violations.Add($"{path}: module '{owner}' reaches past module '{target}''s facade ({match.Value})");
                }
            }
        }

        return violations;
    }

    /// <summary>Global <c>&lt;Using&gt;</c> items that name a Domain namespace, in any project or props file that builds the Domain.</summary>
    public static IReadOnlyList<string> ProjectFileViolations(IEnumerable<DomainSourceFile> projectFiles)
    {
        ArgumentNullException.ThrowIfNull(projectFiles);

        return
        [
            .. projectFiles.SelectMany(file => ProjectUsing().Matches(file.Text).Select(match =>
                $"{file.RelativePath}: global <Using> of '{match.Groups["ns"].Value}' leaks a Domain namespace into every file")),
        ];
    }

    private static bool IsPermitted(string rest) =>
        PermittedForeignNamespaces.Any(permitted =>
            string.Equals(rest, permitted, StringComparison.Ordinal) ||
            rest.StartsWith(permitted + ".", StringComparison.Ordinal));

    private static string? OwningModule(string relativePath)
    {
        const string prefix = "Modules/";
        var end = relativePath.IndexOf('/', prefix.Length);
        return end < 0 ? null : relativePath[prefix.Length..end];
    }

    // Drops block comments, line comments and XML docs, so a namespace mentioned in prose is not a reference.
    private static string CodeOnly(string text) =>
        string.Join('\n', BlockComment().Replace(text, string.Empty).Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // Full (Arts6311.Domain.Modules.X) or relative (Domain.Modules.X, Modules.X) module names. The lookbehind stops
    // the relative forms matching again inside a full name.
    [GeneratedRegex(@"(?<![\w.])(?:Arts6311\.Domain\.|Domain\.)?Modules\.(?<module>\w+)(?<rest>(?:\.\w+)*)")]
    private static partial Regex ModuleReference();

    [GeneratedRegex(@"\busing\s+(?:static\s+)?\w+\s*=\s*(?:global::)?(?:Arts6311(?:\.\w+)*|Domain(?:\.\w+)*|Modules(?:\.\w+)*)\s*;")]
    private static partial Regex DomainAlias();

    [GeneratedRegex(@"\bglobal\s+using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?:global::)?Arts6311\.Domain[\w.]*\s*;")]
    private static partial Regex GlobalUsing();

    [GeneratedRegex(@"<Using\s+Include\s*=\s*""(?<ns>Arts6311\.Domain[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectUsing();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();
}
