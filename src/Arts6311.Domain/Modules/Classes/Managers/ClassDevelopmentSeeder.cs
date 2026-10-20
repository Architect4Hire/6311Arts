using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Classes.Business;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>
/// The spike's classes (<c>projects/site/src/app/data/mock-classes.ts</c>, copied from 6311arts.com/events on
/// 2026-10-18), so a clean clone's CMS and, from Phase 3, its site have classes to list. Development only.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent as <see cref="IDataSeeder"/> requires: a class whose slug is already in use is left alone, so a rerun
/// adds nothing and never undoes a staff edit. Seeded as Published without heroes: the spike's images are WordPress
/// URLs, not library originals. Only summaries are seeded — the spike has no long descriptions, and the real ones
/// arrive with the WordPress import (4.x).
/// </para>
/// <para>
/// A multi-week course is seeded as one sitting per week, at the same wall-clock time in the gallery's zone, so a
/// course that runs across the end of daylight saving still meets at 2 p.m. Each session gets a suggested
/// <c>CLS-&lt;offering&gt;-&lt;n&gt;</c> SKU reserved in the shared registry (ADR 0037); no price, seat count or
/// sold-out value is seeded, because those are Square's record (ADR 0032).
/// </para>
/// </remarks>
internal sealed partial class ClassDevelopmentSeeder(
    IClassesBusiness business,
    ILogger<ClassDevelopmentSeeder> logger) : IDataSeeder
{
    /// <summary>
    /// <c>UpdatedBy</c> on a seeded class. Not a user id, so the importer's "edited by staff since?" comparison
    /// (content.md) can tell a seeded row from a staff one. The same actor as the artist and exhibition seeds.
    /// </summary>
    public const string SeedActor = "system:development-seed";

    /// <summary>One spike class: its copy, its price in cents, and when and how often it meets.</summary>
    /// <param name="FirstSession">The first sitting's local date and time in the gallery's zone.</param>
    /// <param name="Hours">How long one sitting runs.</param>
    /// <param name="Sessions">How many weekly sittings it has.</param>
    internal sealed record SpikeClass(
        string Title,
        string Summary,
        string ClassType,
        string Level,
        string Instructor,
        string? Cadence,
        long PriceCents,
        int Capacity,
        DateTime FirstSession,
        double Hours,
        int Sessions);

    internal static readonly IReadOnlyList<SpikeClass> Classes =
    [
        new("Day of the Dead Ofrenda & Altar Making",
            "Build a personal ofrenda to honor someone you love, just in time for Día de los Muertos.",
            "Mixed media", "Beginner", "Candice Johnson", null, 3000, 14, new(2026, 10, 22, 18, 0, 0), 3, 1),
        new("Art & Activism: History and Context",
            "From protest posters to street murals: how artists have moved movements, and how print still does.",
            "Talk", "All levels", "Luis Valenzuela Jr.", null, 5000, 20, new(2026, 10, 22, 18, 30, 0), 3, 1),
        new("“Boats & Pianos” Opening Reception",
            "Free and open to all. Meet the artist behind the oversized reclaimed-wood constructions filling the gallery.",
            "Event", "All levels", "Michelle Peterson-Albandoz", null, 0, 40, new(2026, 10, 24, 18, 0, 0), 3, 1),
        new("Intro to Screen Printing",
            "Burn your first screen and pull a full edition of prints in five weeks. No experience needed.",
            "Printmaking", "Beginner", "Gwen Schilling", "Saturdays", 30000, 10, new(2026, 10, 24, 14, 0, 0), 3, 5),
        new("Intermediate Screen Printing with Diego from Rathaus",
            "Multi-color registration, halftones and shop tricks from a working Chicago print studio.",
            "Printmaking", "Intermediate", "Diego Granath", "Wednesdays", 30000, 10, new(2026, 11, 4, 18, 30, 0), 3, 5),
        new("Intro to Scratchboard",
            "Draw with light. Scratch bold, high-contrast images out of black clay board in one Friday night.",
            "Drawing", "Beginner", "Candice Johnson", null, 7500, 12, new(2026, 11, 6, 18, 30, 0), 3, 1),
        new("Intro to Screen Printing (Sunday mornings)",
            "The same beginner course, at a slower Sunday-morning pace.",
            "Printmaking", "Beginner", "Nicholas Waguespack", "Sundays", 30000, 10, new(2026, 11, 8, 10, 0, 0), 3, 5),
        new("Printmaking Playground",
            "Monotype, linocut, collagraph and gelli plates. Try a new technique every week and find your favorite.",
            "Printmaking", "Beginner", "Nicholas Waguespack", "Sundays", 32500, 10, new(2026, 11, 8, 14, 0, 0), 3, 5),
    ];

    public string Name => "Development classes";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seeds = Classes
            .Select(spike => new ClassSeed(
                spike.Title,
                spike.Summary,
                Body: null,
                spike.ClassType,
                spike.Level,
                spike.Instructor,
                spike.Cadence,
                spike.PriceCents,
                [.. Sittings(spike)]))
            .ToList();

        var added = await business.SeedAsync(seeds, SeedActor, cancellationToken);
        LogSeeded(added);
    }

    /// <summary>One sitting per week, each at the same wall-clock time in the gallery's zone.</summary>
    private static IEnumerable<ClassSeedSession> Sittings(SpikeClass spike)
    {
        for (var week = 0; week < spike.Sessions; week++)
        {
            var startsAt = spike.FirstSession.AddDays(7 * week);
            yield return new ClassSeedSession(
                GalleryTime(startsAt), GalleryTime(startsAt.AddHours(spike.Hours)), spike.Capacity);
        }
    }

    /// <summary>
    /// A local date and time at 6311 N. Clark St. as the instant it names. The offset is read per date, so a sitting
    /// after the clocks change is still at the hour staff typed.
    /// </summary>
    private static DateTimeOffset GalleryTime(DateTime local)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(GalleryTimeZone.Id);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} development class offerings.")]
    private partial void LogSeeded(int count);
}
