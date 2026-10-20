using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Shop.Business;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>
/// The spike's shop (<c>projects/site/src/app/data/mock-shop.ts</c>, re-pulled from the WooCommerce Store API on
/// 2026-10-18), so a clean clone's CMS and, from Phase 3, its site have artwork to show. Development only.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent as <see cref="IDataSeeder"/> requires: a piece whose slug is already in use is left alone, so a rerun
/// adds nothing and never undoes a staff edit. Artists are resolved through <see cref="IArtistsFacade"/> by slug,
/// after the roster's own seeder has run (MigrationService registers it first); a name with no artist on the roster
/// is seeded uncredited, as the spike's magazine is.
/// </para>
/// <para>
/// <strong>This is the one path that publishes a shop item with no photograph.</strong> The spike's pictures are
/// WordPress URLs rather than library originals, so there are none to attach, and a shop of drafts would leave the
/// development site empty. Every staff and importer write still goes through the facade, which requires a described
/// photograph before it will publish (ADR 0038).
/// </para>
/// <para>
/// The descriptions are short placeholders so the pages read as pages; the real copy arrives with the WooCommerce
/// import (4.x). No price, stock or sold-out value is seeded as a projection — only the starting figures, which are
/// the CMS's (ADR 0032).
/// </para>
/// </remarks>
internal sealed partial class ShopDevelopmentSeeder(
    IShopBusiness business,
    IArtistsFacade artists,
    ILogger<ShopDevelopmentSeeder> logger) : IDataSeeder
{
    /// <summary>
    /// <c>UpdatedBy</c> on a seeded item. Not a user id, so the importer's "edited by staff since?" comparison
    /// (content.md) can tell a seeded row from a staff one. The same actor as the other content seeds.
    /// </summary>
    public const string SeedActor = "system:development-seed";

    /// <summary>One spike piece: its copy, who made it, what it is filed under and what it costs.</summary>
    /// <param name="Artist">The artist's name as the spike spells it, or <c>null</c> when nobody is credited.</param>
    /// <param name="Stock">How many there are. One-of-a-kind pieces are seeded with 1 by the rule itself.</param>
    internal sealed record SpikePiece(
        string Title, string? Artist, string Category, long PriceCents, bool OneOfAKind, int Stock, string Description);

    internal static readonly IReadOnlyList<SpikePiece> Pieces =
    [
        new("Ammonite Fossil & Variscite Necklace", "Carrie Haase", "jewelry", 52500, true, 1,
            "An ammonite fossil and a variscite cabochon set in oxidized silver, on a 22-inch chain."),
        new("Large Branch Handle Bowl", "Susan Volk", "ceramics", 27500, true, 1,
            "A wide stoneware serving bowl with a hand-pulled branch handle."),
        new("The Recovery Series (Pt. 2) #6", "Stephen Bell", "sculpture", 12500, true, 1,
            "A small decorative trunk from the Recovery Series, built from salvaged wood."),
        new("Australian Opal Silver Ring", "Carrie Haase", "jewelry", 26500, true, 1,
            "An Australian opal in a low sterling bezel, sized 7."),
        new("Mushrooms in Fall (print)", "Audrey Avril", "prints", 1000, false, 25,
            "A small open-edition print of autumn mushrooms, on archival matte paper."),
        new("Peekaboo Sterling Oval Ring", "Carrie Haase", "jewelry", 30000, true, 1,
            "A sterling oval with a cut-out window, sized 9."),
        new("Boystown", "Aaron Wooten", "painting", 40000, true, 1,
            "A collage of a Chicago block in paper, paint and tape."),
        new("Grandma (Turn Me Into a Newt Next)", "Audrey Avril", "prints", 800, false, 25,
            "A small open-edition print from the Grandma series."),
        new("Copper Cuff", "Carrie Haase", "jewelry", 11000, true, 1,
            "A forged copper cuff with a hammered face, adjustable."),
        new("Anabelle’s First Summon (print)", "Audrey Avril", "prints", 1000, false, 25,
            "A small open-edition print of Anabelle's first summoning, on archival matte paper."),
        new("Lodolite Pendant", "Carrie Haase", "jewelry", 35000, true, 1,
            "A lodolite quartz pendant in sterling, on an 18-inch chain."),
        new("Piloto (sculpture book)", "Piloto", "books-magazines", 3500, false, 10,
            "A softcover book of Piloto's sculpture, photographed in the gallery."),
        new("The Guide (print)", "Audrey Avril", "prints", 800, false, 25,
            "A small open-edition print of The Guide, on archival matte paper."),
        new("Serpentine Pendant", "Carrie Haase", "jewelry", 23500, true, 1,
            "A serpentine cabochon in a sterling bezel, on an 18-inch chain."),
        new("Sterling Hexagon Studs", "Carrie Haase", "jewelry", 13500, false, 4,
            "Minimalist sterling hexagon studs, about 8 mm across."),
        new("Chicago Collage Magazine Vol. 1", null, "books-magazines", 2000, false, 10,
            "The first volume of Chicago Collage Magazine, printed locally."),
        new("Iron Buffalo Ring", "Carrie Haase", "jewelry", 24500, true, 1,
            "An iron buffalo intaglio set in sterling, sized 8."),
        new("Petrified Palmwood Earrings", "Carrie Haase", "jewelry", 29500, true, 1,
            "Petrified palmwood drops on sterling wires."),
    ];

    public string Name => "Development shop items";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var slugs = Pieces
            .Select(piece => piece.Artist)
            .OfType<string>()
            .Select(Slugs.FromName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var roster = (await artists.FindBySlugsAsync(slugs, cancellationToken))
            .Where(artist => artist.Status != PublishStatus.Archived)
            .ToDictionary(artist => artist.Slug, artist => artist.Id, StringComparer.Ordinal);

        var seeds = Pieces
            .Select(piece => new ShopItemSeed(
                piece.Title,
                $"<p>{piece.Description}</p>",
                piece.Artist is { } name && roster.TryGetValue(Slugs.FromName(name), out var artistId) ? artistId : null,
                [piece.Category],
                piece.OneOfAKind,
                piece.PriceCents,
                piece.Stock))
            .ToList();

        var added = await business.SeedAsync(seeds, SeedActor, cancellationToken);
        LogSeeded(added, slugs.Count - roster.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} development shop items; {Uncredited} artist names have no artist on the roster to credit (missing or archived).")]
    private partial void LogSeeded(int count, int uncredited);
}
