namespace Arts6311.Domain.Modules.Redirects.Managers;

/// <summary>
/// What an address on a redirect may be, and its one normal form (ADR 0035). Two spellings of one address are one
/// redirect: the path is lower-cased and loses a trailing slash, so <c>/Gallery/</c> and <c>/gallery</c> collide.
/// The site (Phase 3) normalises an incoming path the same way before it looks one up.
/// </summary>
public static class RedirectPaths
{
    /// <summary>
    /// Addresses the gateway, the API or staff sign-in answer: never redirected away, so a redirect can't hijack them,
    /// and never redirected to (bar sign-in, which isn't listed as a destination).
    /// </summary>
    public static IReadOnlyList<string> Reserved { get; } =
        ["/admin", "/api", "/bff", "/media", "/webhooks", "/health", "/alive", "/signin", "/account"];

    /// <summary>The reserved prefixes a destination may not point into: machine endpoints, not pages.</summary>
    private static readonly string[] NotDestinations = ["/api", "/bff", "/webhooks", "/health", "/alive"];

    /// <summary>
    /// The old address in normal form — a site path with an optional query, never the home page, a fragment or a
    /// reserved prefix — or why it can't be one.
    /// </summary>
    public static (string? Path, string? Error) NormalizeFrom(string? input)
    {
        var (path, error) = NormalizeSitePath(input, RedirectPolicy.FromMaxLength, allowFragment: false);
        if (error is not null)
        {
            return (null, error);
        }

        if (path == "/")
        {
            return (null, "The home page can't be redirected.");
        }

        return IsUnder(path!, Reserved)
            ? (null, $"{PathOnly(path!)} belongs to the site itself and can't be redirected.")
            : (path, null);
    }

    /// <summary>
    /// The destination in normal form — a site path (with any query and fragment) or a full <c>https://</c> address —
    /// or why it can't be one.
    /// </summary>
    public static (string? Path, string? Error) NormalizeTo(string? input)
    {
        var text = input?.Trim() ?? string.Empty;
        if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Length > RedirectPolicy.ToMaxLength || text.Any(char.IsWhiteSpace) || text.Any(char.IsControl))
            {
                return (null, "Enter a web address without spaces, up to 800 characters.");
            }

            return Uri.TryCreate(text, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.Host.Length > 0
                && uri.UserInfo.Length == 0
                    ? (uri.AbsoluteUri, null)
                    : (null, "A web address must start with https:// and name a site.");
        }

        var (path, error) = NormalizeSitePath(text, RedirectPolicy.ToMaxLength, allowFragment: true);
        if (error is not null)
        {
            return (null, error);
        }

        return IsUnder(path!, NotDestinations)
            ? (null, $"{PathOnly(path!)} isn't a page, so nothing can be sent there.")
            : (path, null);
    }

    /// <summary>
    /// The site address a destination arrives at, for loop and chain checks — its path and query without the fragment
    /// — or <c>null</c> for an address on another site, which can't redirect onward through this one.
    /// </summary>
    public static string? TargetOf(string normalizedTo)
    {
        ArgumentNullException.ThrowIfNull(normalizedTo);
        if (!normalizedTo.StartsWith('/'))
        {
            return null;
        }

        var hash = normalizedTo.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? normalizedTo : normalizedTo[..hash];
    }

    /// <summary>
    /// <paramref name="normalizedTo"/> sent on to <paramref name="newTarget"/>, keeping its own fragment — unless the new
    /// destination has one of its own, which wins (a destination never carries two).
    /// </summary>
    public static string Retarget(string normalizedTo, string newTarget)
    {
        ArgumentNullException.ThrowIfNull(normalizedTo);
        ArgumentNullException.ThrowIfNull(newTarget);
        var hash = normalizedTo.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 || newTarget.Contains('#', StringComparison.Ordinal) ? newTarget : newTarget + normalizedTo[hash..];
    }

    private static (string? Path, string? Error) NormalizeSitePath(string? input, int maxLength, bool allowFragment)
    {
        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return (null, "Enter an address on this site, starting with /.");
        }

        if (text.Length > maxLength)
        {
            return (null, $"Keep the address under {maxLength} characters.");
        }

        if (!text.StartsWith('/') || text.StartsWith("//", StringComparison.Ordinal) || text.Contains('\\', StringComparison.Ordinal))
        {
            return (null, "Start with a single / (for example /gallery), or give a full https:// address.");
        }

        if (text.Any(char.IsWhiteSpace) || text.Any(char.IsControl))
        {
            return (null, "Addresses can't contain spaces.");
        }

        var fragment = string.Empty;
        var hash = text.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            if (!allowFragment)
            {
                return (null, "An old address can't have a # part: browsers never send it.");
            }

            fragment = text[hash..];
            text = text[..hash];
        }

        var query = string.Empty;
        var question = text.IndexOf('?', StringComparison.Ordinal);
        if (question >= 0)
        {
            query = text[question..];
            text = text[..question];
        }

        // A browser resolves dot segments before it asks, so "/a/../admin" would reach a reserved address.
        var segments = text.Split('/');
        if (segments.Any(segment => segment is "." or ".."))
        {
            return (null, "Addresses can't contain . or .. segments.");
        }

        var path = text.ToLowerInvariant();
        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        return ((path.Length == 0 ? "/" : path) + (query == "?" ? string.Empty : query) + fragment, null);
    }

    private static bool IsUnder(string normalized, IEnumerable<string> prefixes)
    {
        var path = PathOnly(normalized);
        return prefixes.Any(prefix =>
            path.Equals(prefix, StringComparison.Ordinal) || path.StartsWith(prefix + "/", StringComparison.Ordinal));
    }

    private static string PathOnly(string normalized)
    {
        var end = normalized.IndexOfAny(['?', '#']);
        return end < 0 ? normalized : normalized[..end];
    }
}
