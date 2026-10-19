using System.Reflection;

namespace Arts6311.Tests;

/// <summary>Locates the repository's <c>src/</c> folder for tests that read source or project files.</summary>
internal static class RepoPaths
{
    /// <summary>
    /// The source root recorded at build time (<c>Arts6311.SourceRoot</c> in the test project), which holds even when
    /// the build output is redirected outside the repo (-p:BaseOutputPath=&lt;scratch&gt;/ while `aspire run` holds
    /// bin/, per CLAUDE.md). Falls back to walking up from the test binaries.
    /// </summary>
    public static string SourceRoot()
    {
        var recorded = typeof(RepoPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "Arts6311.SourceRoot")?.Value;
        if (recorded is not null && IsSourceRoot(recorded))
        {
            return recorded;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (IsSourceRoot(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The repository's src/ folder was not found (Arts6311.slnx beside it).");
    }

    private static bool IsSourceRoot(string path) =>
        File.Exists(Path.Combine(path, "..", "Arts6311.slnx"));
}
