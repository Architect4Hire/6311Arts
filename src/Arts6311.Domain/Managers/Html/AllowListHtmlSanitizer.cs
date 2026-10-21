using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace Arts6311.Domain.Managers.Html;

/// <summary>
/// <see cref="IHtmlSanitizer"/> over Ganss.Xss <c>HtmlSanitizer</c> (MIT, on AngleSharp; ADR 0034), configured to
/// <see cref="HtmlPolicy"/> and nothing else.
/// </summary>
/// <remarks>
/// <para>Three passes:</para>
/// <list type="number">
/// <item><strong>Before</strong>, on our own AngleSharp parse: elements in <see cref="HtmlPolicy.DroppedWithContents"/>
/// go with everything inside them, <c>b</c>/<c>i</c> become <c>strong</c>/<c>em</c>, and every heading becomes
/// <c>h2</c> (<see cref="HtmlPolicy.Renamed"/>). HtmlSanitizer is told to keep
/// the children of what it removes (so a <c>&lt;div&gt;</c> or <c>&lt;span&gt;</c> from a paste or an import keeps its
/// words); without this pass, a script's code would survive as text.</item>
/// <item><strong>HtmlSanitizer</strong> applies the allow-list: tags, attributes, URL schemes, no CSS at all. Its own
/// parse and serialization are what make the output safe against parser-confusion (mXSS) tricks, whatever the first
/// pass produced.</item>
/// <item><strong>After</strong>, on its DOM: a link must be site-relative, in-page or an allowed scheme; an image must
/// be a library rendition; a link left without a target is unwrapped, an image without a source removed, and empty
/// blocks dropped. This is also where the library images are listed.</item>
/// </list>
/// <para>
/// While the policy allows no links or pictures (ADR 0045), HtmlSanitizer takes every <c>a</c> and <c>img</c> out
/// before the URL filter or the last pass sees one, so those checks wait for the policy to allow them again.
/// </para>
/// <para>
/// A new HtmlSanitizer per call: its events carry this call's findings. Construction is cheap next to the parse, and a
/// body is sanitized only when staff save one.
/// </para>
/// </remarks>
internal sealed class AllowListHtmlSanitizer : IHtmlSanitizer
{
    private static readonly string[] Blocks = ["p", "h2", "li", "ul", "ol", "strong", "em", "a"];

    public SanitizedHtml Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return SanitizedHtml.Empty;
        }

        // Before any parse: every pass below walks the tree recursively, and a stack overflow can't be caught.
        if (HtmlPolicy.EstimateNestingDepth(html) > HtmlPolicy.MaxNestingDepth)
        {
            return SanitizedHtml.Empty with
            {
                Refused = $"The formatting is nested more than {HtmlPolicy.MaxNestingDepth} levels deep.",
            };
        }

        var removed = new List<string>();
        var images = new List<HtmlImage>();

        var prepared = DropAndRename(html, removed);
        var sanitizer = Create(removed, images);
        var output = sanitizer.Sanitize(prepared).Trim();

        return output.Length == 0
            ? new SanitizedHtml(string.Empty, Distinct(removed), [])
            : new SanitizedHtml(output, Distinct(removed), images);
    }

    /// <summary>The first pass, on a parse of our own: see the class remarks.</summary>
    private static string DropAndRename(string html, List<string> removed)
    {
        var document = new HtmlParser().ParseDocument(string.Empty);
        var body = document.Body!;
        body.InnerHtml = html;

        foreach (var element in body.QuerySelectorAll("*").ToList())
        {
            if (element.Parent is null)
            {
                continue;
            }

            if (HtmlPolicy.DroppedWithContents.Contains(element.LocalName))
            {
                removed.Add($"<{Name(element.LocalName)}> and its contents");
                element.Remove();
            }
        }

        foreach (var (from, to) in HtmlPolicy.Renamed)
        {
            foreach (var element in body.QuerySelectorAll(from).ToList())
            {
                var replacement = document.CreateElement(to);
                while (element.FirstChild is { } child)
                {
                    replacement.AppendChild(child);
                }

                element.Replace(replacement);
            }
        }

        return body.InnerHtml;
    }

    private static HtmlSanitizer Create(List<string> removed, List<HtmlImage> images)
    {
        var options = new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(HtmlPolicy.AllowedTags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(HtmlPolicy.AllowedAttributes, StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(HtmlPolicy.AllowedLinkSchemes, StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(["href", "src"], StringComparer.OrdinalIgnoreCase),
            UriListAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedCssProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedCssClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>(),
            AllowCssCustomProperties = false,
            AllowDataAttributes = false,
        };

        var sanitizer = new HtmlSanitizer(options) { KeepChildNodes = true };

        sanitizer.RemovingTag += (_, e) => removed.Add(e.Tag.LocalName switch
        {
            "a" => "a link (its text kept)",
            "img" => "a picture",
            var name => $"<{Name(name)}> (its text kept)",
        });
        sanitizer.RemovingAttribute += (_, e) => removed.Add(e.Reason == RemoveReason.NotAllowedUrlValue
            ? $"a {(e.Tag.LocalName == "img" ? "picture" : "link")} address that isn't allowed"
            : $"the {Name(e.Attribute.Name)} attribute");
        sanitizer.RemovingComment += (_, _) => removed.Add("a comment");
        sanitizer.RemovingStyle += (_, _) => removed.Add("inline styles");

        // Stricter than schemes alone: links stay on allowed schemes or this site; images come from the library.
        sanitizer.FilterUrl += (_, e) =>
        {
            if (e.SanitizedUrl is null)
            {
                return;
            }

            e.SanitizedUrl = e.Tag.LocalName switch
            {
                "img" => HtmlPolicy.TryParseMediaImage(e.OriginalUrl.Trim(), out Guid _) ? e.OriginalUrl.Trim() : null,
                _ => HtmlPolicy.IsAllowedLink(e.SanitizedUrl) ? e.SanitizedUrl : null,
            };
        };

        sanitizer.PostProcessDom += (_, e) => Finish(e.Document.Body!, removed, images);

        return sanitizer;
    }

    /// <summary>The last pass: see the class remarks.</summary>
    private static void Finish(IElement body, List<string> removed, List<HtmlImage> images)
    {
        // Each allowed attribute only on its own element: href on links, src and alt on images.
        foreach (var element in body.QuerySelectorAll("[href], [src], [alt]").ToList())
        {
            foreach (var attribute in element.Attributes.ToList())
            {
                if (HtmlPolicy.AttributesOf.TryGetValue(attribute.Name, out var owner) && owner != element.LocalName)
                {
                    removed.Add($"the {attribute.Name} attribute");
                    element.RemoveAttribute(attribute.Name);
                }
            }
        }

        foreach (var link in body.QuerySelectorAll("a").ToList())
        {
            if (string.IsNullOrEmpty(link.GetAttribute("href")))
            {
                Unwrap(link);
            }
        }

        foreach (var image in body.QuerySelectorAll("img").ToList())
        {
            if (!HtmlPolicy.TryParseMediaImage(image.GetAttribute("src"), out var assetId))
            {
                removed.Add("a picture that isn't from the media library");
                image.Remove();
                continue;
            }

            var alt = image.GetAttribute("alt")?.Trim();
            if (string.IsNullOrEmpty(alt))
            {
                image.RemoveAttribute("alt");
                alt = null;
            }
            else
            {
                image.SetAttribute("alt", alt);
            }

            images.Add(new HtmlImage(assetId, alt));
        }

        // Empty blocks are dropped innermost first, so a list emptied of its items goes too.
        bool dropped;
        do
        {
            dropped = false;
            foreach (var element in body.QuerySelectorAll(string.Join(',', Blocks)).Reverse().ToList())
            {
                if (element.Parent is not null
                    && string.IsNullOrWhiteSpace(element.TextContent)
                    && element.QuerySelector("img") is null)
                {
                    element.Remove();
                    dropped = true;
                }
            }
        }
        while (dropped);
    }

    private static void Unwrap(IElement element)
    {
        var children = element.ChildNodes.ToArray();
        if (children.Length == 0)
        {
            element.Remove();
        }
        else
        {
            element.Replace(children);
        }
    }

    private static List<string> Distinct(List<string> removed) => [.. removed.Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// A tag or attribute name from the input, as an audit line or a report may show it: letters, digits and
    /// <c>- _ : .</c> only, and short. The name is the attacker's to choose, and it is only ever text, but it stays plain and bounded.
    /// </summary>
    private static string Name(string name)
    {
        var clean = new string([.. name.Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or ':' or '.')]);
        return clean.Length == 0 ? "unnamed" : clean.Length <= 30 ? clean : clean[..30] + "…";
    }
}
