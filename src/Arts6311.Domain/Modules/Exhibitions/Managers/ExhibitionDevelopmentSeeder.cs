using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Exhibitions.Business;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Exhibitions.Managers;

/// <summary>
/// The spike's exhibitions (<c>projects/site/src/app/data/mock-exhibitions.ts</c>, copied from 6311arts.com/gallery on
/// 2026-10-18), so a clean clone's CMS and, from Phase 3, its site have shows to list. Development only.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent as <see cref="IDataSeeder"/> requires: a show whose slug is already in use is left alone, so a rerun adds
/// nothing and never undoes a staff edit. Seeded as Published without heroes: the spike's images are WordPress URLs,
/// not library originals.
/// </para>
/// <para>
/// Credits are resolved through <see cref="IArtistsFacade"/> by slug, after the artist roster's own seeder has run
/// (MigrationService registers it first). A name with no artist on the roster is left out — four of the spike's names
/// are not in the roster — and so is an artist staff have archived since.
/// </para>
/// </remarks>
internal sealed partial class ExhibitionDevelopmentSeeder(
    IExhibitionsBusiness business,
    IArtistsFacade artists,
    ILogger<ExhibitionDevelopmentSeeder> logger) : IDataSeeder
{
    /// <summary>
    /// <c>UpdatedBy</c> on a seeded exhibition. Not a user id, so the importer's "edited by staff since?" comparison
    /// (content.md) can tell a seeded row from a staff one. The same actor as the artist seed.
    /// </summary>
    public const string SeedActor = "system:development-seed";

    /// <summary>One spike show: its dates, summary, reception and credited names, in the spike's order.</summary>
    internal sealed record SpikeShow(
        string Title, DateOnly Start, DateOnly End, string Summary, ExhibitionReceptionServiceModel? Reception, string[] Artists);

    internal static readonly IReadOnlyList<SpikeShow> Shows =
    [
        new("Local High School Art Exhibition", new(2026, 12, 12), new(2027, 1, 30),
            "Year-end work by students from the high schools around us. The next generation of North Side artists, on our walls.",
            null, []),
        new("Boats & Pianos", new(2026, 10, 24), new(2026, 12, 5),
            "New work by Michelle Peterson-Albandoz: oversized constructions built from reclaimed wood, boats and pianos, alongside new multimedia pieces.",
            new(new(2026, 10, 24, 18, 0, 0, TimeSpan.FromHours(-5)), new(2026, 10, 24, 21, 0, 0, TimeSpan.FromHours(-5)), "Free and open to all"),
            ["Michelle Peterson-Albandoz"]),
        new("Portrait Mode", new(2026, 8, 29), new(2026, 10, 17),
            "A group show about capturing people across mediums and styles, with Brian Eaton’s “Trying to Keep It All Together” in the East and West galleries.",
            null, ["Harry Sudman", "Jeffrey Brooks", "Eddwin Meyers", "Alejandra Guerrero", "Hector Hernandez", "Adrian Zavala", "Brian Eaton"]),
        new("Nooks and Crannies", new(2026, 7, 11), new(2026, 8, 22),
            "A solo show by Jeremy Humbert, with work by Mike Famous, Laurence Hoffman, Candice Johnson and Kevin Johnson.",
            null, ["Jeremy Humbert", "Candice Johnson", "Kevin Johnson", "Piloto"]),
        new("Queue", new(2026, 5, 16), new(2026, 7, 4),
            "A solo show by Brad Gibbs, with Austin Special Chicago and DePaul Artists Coalition in the East and West galleries.",
            null, ["Brad Gibbs", "Piloto"]),
        new("The Observed & The Imagined", new(2026, 3, 28), new(2026, 5, 9),
            "Yvonne Doll and Victoria Loeb pair figure and abstraction, with ten North Side photographers.",
            null, ["Yvonne Doll", "Victoria Loeb", "Justin Matthew", "Eddie Pedroza", "Mike Sebahar"]),
        new("A Community of Artists II", new(2026, 2, 7), new(2026, 3, 21),
            "Seventeen neighborhood artists, one room.",
            null, ["Annette Baksinskas"]),
        new("Grand Opening", new(2025, 11, 22), new(2026, 1, 31),
            "Thirty local artists opened the doors of the old funeral home on Clark Street.",
            null, ["Lisa Barcy", "Stephen Bell"]),
    ];

    public string Name => "Development exhibitions";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var slugs = Shows.SelectMany(show => show.Artists).Select(Slugs.FromName).Distinct(StringComparer.Ordinal).ToList();
        var roster = (await artists.FindBySlugsAsync(slugs, cancellationToken))
            .Where(artist => artist.Status != PublishStatus.Archived)
            .ToDictionary(artist => artist.Slug, artist => artist.Id, StringComparer.Ordinal);

        var seeds = Shows
            .Select(show => new ExhibitionSeed(
                show.Title,
                show.Start,
                show.End,
                show.Summary,
                show.Reception,
                [.. show.Artists.Select(Slugs.FromName).Where(roster.ContainsKey).Select(slug => roster[slug])]))
            .ToList();

        var added = await business.SeedAsync(seeds, SeedActor, cancellationToken);
        LogSeeded(added, slugs.Count - roster.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} development exhibitions; {Uncredited} credited names have no artist on the roster to credit (missing or archived).")]
    private partial void LogSeeded(int count, int uncredited);
}
