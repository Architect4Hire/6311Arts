namespace Arts6311.Domain.Modules.Media.Managers;

/// <summary>
/// The site's own pictures (prompt 3.3): the logo its structured data names and the picture a page shares when it
/// has none of its own. MigrationService seeds them at these fixed ids, published, in every environment, so the site
/// can name them as <c>/media/{id}/{rendition}</c> without asking anything. The site's <c>core/site.ts</c> carries the
/// same ids, and a test keeps the two in step.
/// </summary>
public static class MediaSiteImages
{
    /// <summary>The 6311arts line logo (transparent PNG); the site names its <c>800.webp</c>, which keeps the transparency.</summary>
    public static readonly Guid Logo = Guid.Parse("6311a0f5-0000-4000-8000-000000000001");

    public const string LogoFileName = "6311arts-logo.png";

    public const string LogoAltText = "The 6311arts logo: 6311 above the word arts, in black line lettering inside a double circle.";

    /// <summary>The default social image, Boats &amp; Pianos (the spike's); the site names its <c>1600.jpg</c>.</summary>
    public static readonly Guid OpenGraph = Guid.Parse("6311a0f5-0000-4000-8000-000000000002");

    public const string OpenGraphFileName = "6311arts-boats-and-pianos.jpg";

    public const string OpenGraphAltText = "Boats & Pianos: a wall piece of salvaged piano parts, with rows of keys, a Wurlitzer nameboard and a red triangle.";

    /// <summary>The actor recorded on the seeded pictures.</summary>
    public const string SeedActor = "system:site-images";
}
