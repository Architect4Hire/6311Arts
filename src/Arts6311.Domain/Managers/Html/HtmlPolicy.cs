using System.Text.RegularExpressions;

namespace Arts6311.Domain.Managers.Html;

/// <summary>
/// The allow-list (ADR 0034): the elements, attributes and URLs a stored HTML body may contain. Deliberately small —
/// what the site's copy blocks need (privacy, terms, about) and what the admin's editor can produce, nothing more.
/// Anything not listed is taken out; an element not listed keeps its text unless it is in <see cref="DroppedWithContents"/>.
/// </summary>
public static partial class HtmlPolicy
{
    /// <summary>The elements kept. <c>h1</c> is the page's own title, so a body starts at <c>h2</c>.</summary>
    public static IReadOnlySet<string> AllowedTags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h2", "h3", "h4", "ul", "ol", "li", "strong", "em", "a", "br", "blockquote", "img",
    };

    /// <summary>
    /// The attributes kept, anywhere: <c>href</c>, <c>src</c> and <c>alt</c>. No <c>style</c>, <c>class</c>, <c>id</c>,
    /// <c>target</c>, <c>rel</c>, <c>data-*</c> or <c>on*</c>. Each is then kept only on its own element
    /// (<see cref="AttributesOf"/>).
    /// </summary>
    public static IReadOnlySet<string> AllowedAttributes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "alt",
    };

    /// <summary>Which element each allowed attribute belongs on: <c>href</c> on links, <c>src</c> and <c>alt</c> on images.</summary>
    public static IReadOnlyDictionary<string, string> AttributesOf { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["href"] = "a",
        ["src"] = "img",
        ["alt"] = "img",
    };

    /// <summary>
    /// The deepest nesting a body may have. Real copy is a few levels deep; an attacker's thousands-deep body would
    /// exhaust the stack in the parser's tree walks and take the process down with it (a <c>StackOverflowException</c>
    /// can't be caught), so it is refused before anything parses it.
    /// </summary>
    public const int MaxNestingDepth = 100;

    /// <summary>
    /// The library's rendition widths (<c>MediaPolicy.RenditionWidths</c>), so a body can only point at a rendition
    /// Media actually makes. A test pins the two together: the kernel can't name the module.
    /// </summary>
    public static IReadOnlyList<int> MediaRenditionWidths { get; } = [400, 800, 1600];

    /// <summary>The absolute link schemes kept. Site-relative (<c>/…</c>) and in-page (<c>#…</c>) links are kept too.</summary>
    public static IReadOnlySet<string> AllowedLinkSchemes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto", "tel",
    };

    /// <summary>
    /// Elements removed together with everything inside them: their content is code, a form, embedded media or
    /// foreign markup (SVG, MathML) — the usual vehicles for script and parser-confusion attacks — never copy.
    /// </summary>
    public static IReadOnlySet<string> DroppedWithContents { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "template", "noscript", "noembed", "noframes", "iframe", "frame", "frameset", "object",
        "embed", "applet", "svg", "math", "form", "input", "button", "select", "option", "optgroup", "textarea",
        "datalist", "output", "head", "title", "meta", "link", "base", "canvas", "audio", "video", "source", "track",
        "map", "area", "dialog", "portal", "xmp", "plaintext", "listing",
    };

    /// <summary>Presentational emphasis kept as its semantic equivalent, so imported copy keeps its weight.</summary>
    public static IReadOnlyDictionary<string, string> Renamed { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "strong",
        ["i"] = "em",
    };

    /// <summary>
    /// An image's only allowed source: a library rendition as Media delivers it (<c>/media/{id}/{width}.{webp|jpg}</c>,
    /// optionally with its <c>?v=</c>), site-relative so the gateway serves it (ADR 0001, 0030).
    /// </summary>
    public static bool TryParseMediaImage(string? src, out Guid assetId)
    {
        assetId = Guid.Empty;
        if (src is null)
        {
            return false;
        }

        var match = MediaImageSource().Match(src);
        return match.Success
            && MediaRenditionWidths.Contains(int.Parse(match.Groups["width"].Value, System.Globalization.CultureInfo.InvariantCulture))
            && Guid.TryParseExact(match.Groups["id"].Value, "D", out assetId);
    }

    /// <summary>
    /// How deeply <paramref name="html"/> nests, estimated from its tags without parsing it — and so without the
    /// recursion a deep body would overflow. Void elements, and those whose end tag is optional (a new <c>p</c> or
    /// <c>li</c> closes the last), don't deepen it, so ordinary unclosed paragraphs and list items never count up.
    /// It errs high only on odd markup (a <c>&gt;</c> inside an attribute), which is refused rather than risked.
    /// </summary>
    public static int EstimateNestingDepth(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        // The elements currently open, innermost last. A plain counter is not enough: see the `closing` branch.
        var open = new List<string>(MaxNestingDepth + 1);
        var deepest = 0;
        for (var i = html.IndexOf('<'); i >= 0 && i < html.Length - 1; i = html.IndexOf('<', i + 1))
        {
            var closing = html[i + 1] == '/';
            var start = closing ? i + 2 : i + 1;
            var end = start;
            while (end < html.Length && char.IsAsciiLetterOrDigit(html[end]))
            {
                end++;
            }

            if (end == start || !char.IsAsciiLetter(html[start]))
            {
                continue;
            }

            var name = html[start..end];
            var close = html.IndexOf('>', end);
            var selfClosing = close > 0 && html[close - 1] == '/';
            if (Void.Contains(name) || OptionalEnd.Contains(name))
            {
                continue;
            }

            if (closing)
            {
                // An end tag only reduces the depth if it actually closes something. A stray `</u>` with no
                // `<u>` open is what the parser ignores entirely, so counting it down let `<b></u><b></u>…`
                // report a depth of 1 while the real tree nested once per `<b>` — walking straight past the
                // limit this estimate exists to enforce, into the recursive sanitize that a deep tree
                // overflows (ADR 0034).
                var at = -1;
                for (var candidate = open.Count - 1; candidate >= 0; candidate--)
                {
                    if (string.Equals(open[candidate], name, StringComparison.OrdinalIgnoreCase))
                    {
                        at = candidate;
                        break;
                    }
                }

                if (at >= 0)
                {
                    // Whatever the markup left open inside it closes with it, as a parser would.
                    open.RemoveRange(at, open.Count - at);
                }
            }
            else if (!selfClosing)
            {
                open.Add(name);
                deepest = Math.Max(deepest, open.Count);

                // Past the limit the caller refuses the input, so there is nothing further to learn. Stopping
                // here also keeps this O(n) on adversarial markup: without it, the scan above would search an
                // ever-growing open list for every one of thousands of stray end tags.
                if (deepest > MaxNestingDepth)
                {
                    return deepest;
                }
            }
        }

        return deepest;
    }

    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    };

    private static readonly HashSet<string> OptionalEnd = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "li", "dt", "dd", "option", "optgroup", "tr", "td", "th", "thead", "tbody", "tfoot", "colgroup", "caption",
        "rb", "rt", "rp", "rtc",
    };

    /// <summary>Whether a link target is one a stored body may carry.</summary>
    public static bool IsAllowedLink(string? href)
    {
        if (string.IsNullOrWhiteSpace(href) || href.Any(char.IsControl) || href.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (href.StartsWith('#'))
        {
            return href.Length > 1;
        }

        // Site-relative, never protocol-relative ("//host") or a backslash a browser may read as one ("/\host").
        if (href.StartsWith('/'))
        {
            return href.Length == 1 || (href[1] != '/' && href[1] != '\\');
        }

        return Uri.TryCreate(href, UriKind.Absolute, out var uri)
            && AllowedLinkSchemes.Contains(uri.Scheme)
            && (uri.Scheme is "mailto" or "tel" || !string.IsNullOrEmpty(uri.Host));
    }

    [GeneratedRegex(
        @"\A/media/(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})/(?<width>[0-9]{3,4})\.(?:webp|jpg)(?:\?v=[0-9a-f]{1,64})?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex MediaImageSource();
}
