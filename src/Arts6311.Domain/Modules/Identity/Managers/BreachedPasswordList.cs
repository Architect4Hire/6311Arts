using System.Collections.Frozen;
using System.IO.Compression;

namespace Arts6311.Domain.Modules.Identity.Managers;

/// <summary>
/// The bundled common/breached-password list (auth.md, ADR 0018): offline, with no network call, so a laptop
/// with no internet still enforces it and no password ever leaves the process.
/// </summary>
/// <remarks>
/// <para>
/// 10,000 entries from SecLists' <c>10k-most-common.txt</c> (MIT), lower-cased, de-duplicated and sorted, stored
/// gzipped as an embedded resource (~30 KB). Embedded rather than a file on disk so no host has to copy it and no
/// deployment can leave it out — a silently missing list would turn this control off without failing anything.
/// </para>
/// <para>
/// <strong>This list does almost nothing for staff passwords.</strong> Exactly one of its 10,000 entries is 14
/// characters or longer, so the staff minimum already rejects 9,999 of them on length alone. It earns its place
/// on <em>member</em> passwords, which have no such floor — which is why
/// <see cref="BreachedPasswordValidator"/> applies it to everyone rather than to staff only (ADR 0023).
/// </para>
/// </remarks>
public static class BreachedPasswordList
{
    private const string ResourceName =
        "Arts6311.Domain.Modules.Identity.Managers.Resources.CommonPasswords.txt.gz";

    // Inflated once, on first use rather than at startup, so a host that never checks a password never pays for
    // it. Ordinal-ignore-case: "Password1" is no better a choice than "password1".
    private static readonly Lazy<FrozenSet<string>> Entries = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static int Count => Entries.Value.Count;

    /// <summary>Whether this password is one of the listed ones. Case-insensitive.</summary>
    public static bool Contains(string? password) =>
        !string.IsNullOrEmpty(password) && Entries.Value.Contains(password);

    private static FrozenSet<string> Load()
    {
        using var resource = typeof(BreachedPasswordList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded common-password list '{ResourceName}' is missing. It is a security control, so a "
                + "build without it must fail rather than silently accept any password.");

        using var decompressed = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new StreamReader(decompressed);

        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                entries.Add(line);
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                "The embedded common-password list decompressed to nothing; it would accept every password.");
        }

        return entries.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
