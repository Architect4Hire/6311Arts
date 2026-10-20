namespace Arts6311.Domain.Managers.Html;

/// <summary>
/// The shared allow-list HTML sanitizer (content.md "Content rules", ADR 0034). Every HTML field is passed through it
/// on write; what it returns is all that is ever stored. Scripts, styles and inline event handlers never survive.
/// </summary>
public interface IHtmlSanitizer
{
    /// <summary>
    /// <paramref name="html"/> cut down to <see cref="HtmlPolicy"/>. Deterministic and idempotent: sanitizing the output
    /// again returns it unchanged, so stored HTML never looks edited. <c>null</c> or blank in, empty out.
    /// </summary>
    SanitizedHtml Sanitize(string? html);
}

/// <summary>What the sanitizer kept, what it took out, and the library images the kept HTML shows.</summary>
/// <param name="Html">The HTML to store. Empty when nothing worth keeping was left.</param>
/// <param name="Removed">What was taken out, in words ("&lt;script&gt; and its contents"), each once, in order found.
/// For audit summaries and the importer's report; never shown as HTML.</param>
/// <param name="Images">Each <c>&lt;img&gt;</c> left in <paramref name="Html"/>, in document order. Every one is a
/// <c>/media/{id}/{rendition}</c> image from the library; any other source is removed.</param>
public sealed record SanitizedHtml(string Html, IReadOnlyList<string> Removed, IReadOnlyList<HtmlImage> Images)
{
    public static SanitizedHtml Empty { get; } = new(string.Empty, [], []);

    /// <summary>
    /// Why the input could not be sanitized at all, when it couldn't (nested past <see cref="HtmlPolicy.MaxNestingDepth"/>);
    /// otherwise <c>null</c>. A refused input yields empty HTML, and a content module refuses the write rather than
    /// storing an emptied body.
    /// </summary>
    public string? Refused { get; init; }

    public bool IsEmpty => Html.Length == 0;

    /// <summary>The distinct library assets the HTML shows, for the content module's Restrict references.</summary>
    public IReadOnlyList<Guid> MediaAssetIds => [.. Images.Select(image => image.MediaAssetId).Distinct()];
}

/// <summary>One library image in sanitized HTML.</summary>
/// <param name="AltText">Its <c>alt</c>, trimmed; <c>null</c> when it has none. Whether that is allowed is the content module's rule.</param>
public sealed record HtmlImage(Guid MediaAssetId, string? AltText);
