using System.Text.RegularExpressions;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Preview.Managers;
using Arts6311.Domain.Modules.Shop.Managers;

namespace Arts6311.Tests.Api;

/// <summary>
/// The lists the admin has to mirror by hand, pinned to the C# they mirror.
/// </summary>
/// <remarks>
/// api-contract.md makes the OpenAPI snapshot the contract, and a snapshot cannot carry these: a category slug is a
/// plain string in the schema, and a preview type never appears in it at all. A mismatch would not fail a build —
/// it would quietly give staff a filter the API refuses, or a preview link the API will not mint. So the lists are
/// compared here, by reading the TypeScript the admin ships.
/// </remarks>
public sealed class AdminConstantContractTests
{
    [Fact]
    public void TheAdminsShopCategoriesAreTheOnesTheApiAllows()
    {
        var slugs = Slugs(Read("models", "shop.ts"), "SHOP_CATEGORIES", @"slug:\s*'([^']+)'");

        Assert.Equal(ShopPolicy.Categories.Select(category => category.Slug), slugs);
    }

    [Fact]
    public void TheAdminsMembershipCadencesAreTheOnesTheApiAccepts()
    {
        var values = Slugs(Read("models", "membership.ts"), "MEMBERSHIP_CADENCES", @"value:\s*'([^']+)'");

        // The API writes the enum by name (JsonStringEnumConverter), so the admin's values are those names. The
        // labels beside them are the admin's own words, as SHOP_CATEGORIES' are.
        Assert.Equal(
            Enum.GetNames<MembershipCadence>().Order(StringComparer.Ordinal),
            values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheAdminsPreviewTypesAreTheOnesTheApiMints()
    {
        var types = Slugs(Read("models", "content.ts"), "PREVIEW_TYPES", @"'([^']+)'");

        Assert.Equal(PreviewPolicy.PathPrefixes.Keys, types);
    }

    [Fact]
    public void TheSitesOwnPicturesAreTheOnesMigrationServiceSeeds()
    {
        // The site names the logo and the social image by id in every environment (prompt 3.3): a mismatch would be a
        // broken image in every shared link and in the search result's logo, and no build would notice.
        var source = File.ReadAllText(Path.Combine(RepoPaths.SourceRoot(), "web", "projects", "site", "src", "app", "core", "site.ts"));
        var ids = new Regex(@"export const SITE_IMAGES = \{\s*logo: '([^']+)',\s*openGraph: '([^']+)',").Match(source);

        Assert.True(ids.Success, "SITE_IMAGES is not declared as logo then openGraph any more.");
        Assert.Equal(MediaSiteImages.Logo.ToString(), ids.Groups[1].Value);
        Assert.Equal(MediaSiteImages.OpenGraph.ToString(), ids.Groups[2].Value);
        Assert.Contains("/media/${SITE_IMAGES.logo}/800.webp", source, StringComparison.Ordinal);
        Assert.Contains("/media/${SITE_IMAGES.openGraph}/1600.jpg", source, StringComparison.Ordinal);
    }

    /// <summary>The strings in one <c>export const NAME = [...]</c> declaration, in the order they are written.</summary>
    private static List<string> Slugs(string source, string name, string pattern)
    {
        var declaration = new Regex($@"export const {name}[^=]*=\s*\[(?<body>.*?)\]\s*as const;", RegexOptions.Singleline)
            .Match(source);
        Assert.True(declaration.Success, $"{name} is not declared as an `as const` array any more.");

        return [.. Regex.Matches(declaration.Groups["body"].Value, pattern).Select(match => match.Groups[1].Value)];
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(
        [RepoPaths.SourceRoot(), "web", "projects", "admin", "src", "app", .. parts]));
}
