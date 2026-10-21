using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Classes.Business;
using Arts6311.Domain.Modules.Instructors.Facade;
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
/// Each spike class is one run (ADR 0044) — except the two beginner screen-printing courses, which the spike lists as
/// separate events but are the same class taught on Saturdays and on Sunday mornings by two instructors, so they are
/// seeded as one offering with two runs. A multi-week run is seeded as one sitting per week, at the same wall-clock
/// time in the gallery's zone, so a course that runs across the end of daylight saving still meets at 2 p.m. Each
/// run gets a suggested <c>CLS-&lt;offering&gt;-&lt;n&gt;</c> SKU reserved in the shared registry (ADR 0037); no
/// price, seat count or sold-out value is seeded, because those are Square's record (ADR 0032).
/// </para>
/// <para>
/// Instructors are found through <see cref="IInstructorsFacade"/> by slug, after the instructors' own seeder has run
/// (MigrationService registers it first). A run whose instructor is missing or archived is seeded without one, and
/// its class as a Draft, because publishing needs every run to have an instructor (content.md).
/// </para>
/// </remarks>
internal sealed partial class ClassDevelopmentSeeder(
    IClassesBusiness business,
    IInstructorsFacade instructors,
    ILogger<ClassDevelopmentSeeder> logger) : IDataSeeder
{
    /// <summary>
    /// <c>UpdatedBy</c> on a seeded class. Not a user id, so the importer's "edited by staff since?" comparison
    /// (content.md) can tell a seeded row from a staff one. The same actor as the artist and exhibition seeds.
    /// </summary>
    public const string SeedActor = "system:development-seed";

    /// <summary>One spike class: its copy and the runs it is offered in.</summary>
    internal sealed record SpikeClass(string Title, string Summary, string ClassType, string Level, SpikeRun[] Runs);

    /// <summary>One spike run: who teaches it, its price in cents and seats, and when and how often it meets.</summary>
    /// <param name="FirstSession">The first sitting's local date and time in the gallery's zone.</param>
    /// <param name="Hours">How long one sitting runs.</param>
    /// <param name="Sessions">How many weekly sittings it has.</param>
    internal sealed record SpikeRun(
        string? Label,
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
            "Mixed media", "Beginner",
            [new(null, "Candice Johnson", null, 3000, 14, new(2026, 10, 22, 18, 0, 0), 3, 1)]),
        new("Art & Activism: History and Context",
            "From protest posters to street murals: how artists have moved movements, and how print still does.",
            "Talk", "All levels",
            [new(null, "Luis Valenzuela Jr.", null, 5000, 20, new(2026, 10, 22, 18, 30, 0), 3, 1)]),
        new("“Boats & Pianos” Opening Reception",
            "Free and open to all. Meet the artist behind the oversized reclaimed-wood constructions filling the gallery.",
            "Event", "All levels",
            [new(null, "Michelle Peterson-Albandoz", null, 0, 40, new(2026, 10, 24, 18, 0, 0), 3, 1)]),
        new("Intro to Screen Printing",
            "Burn your first screen and pull a full edition of prints in five weeks. No experience needed.",
            "Printmaking", "Beginner",
            [
                new("Saturday afternoons", "Gwen Schilling", "Saturdays", 30000, 10, new(2026, 10, 24, 14, 0, 0), 3, 5),
                new("Sunday mornings", "Nicholas Waguespack", "Sundays", 30000, 10, new(2026, 11, 8, 10, 0, 0), 3, 5),
            ]),
        new("Intermediate Screen Printing with Diego from Rathaus",
            "Multi-color registration, halftones and shop tricks from a working Chicago print studio.",
            "Printmaking", "Intermediate",
            [new(null, "Diego Granath", "Wednesdays", 30000, 10, new(2026, 11, 4, 18, 30, 0), 3, 5)]),
        new("Intro to Scratchboard",
            "Draw with light. Scratch bold, high-contrast images out of black clay board in one Friday night.",
            "Drawing", "Beginner",
            [new(null, "Candice Johnson", null, 7500, 12, new(2026, 11, 6, 18, 30, 0), 3, 1)]),
        new("Printmaking Playground",
            "Monotype, linocut, collagraph and gelli plates. Try a new technique every week and find your favorite.",
            "Printmaking", "Beginner",
            [new(null, "Nicholas Waguespack", "Sundays", 32500, 10, new(2026, 11, 8, 14, 0, 0), 3, 5)]),
    ];

    public string Name => "Development classes";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var slugs = Classes.SelectMany(spike => spike.Runs).Select(run => Slugs.FromName(run.Instructor)).Distinct().ToList();
        var teaching = (await instructors.FindBySlugsAsync(slugs, cancellationToken))
            .Where(instructor => instructor.Status != PublishStatus.Archived)
            .ToDictionary(instructor => instructor.Slug, instructor => instructor.Id, StringComparer.Ordinal);

        var seeds = Classes
            .Select(spike => new ClassSeed(
                spike.Title,
                spike.Summary,
                Body: null,
                spike.ClassType,
                spike.Level,
                [.. spike.Runs.Select(run => new ClassSeedRun(
                    run.Label,
                    teaching.TryGetValue(Slugs.FromName(run.Instructor), out var instructorId) ? instructorId : null,
                    run.Cadence,
                    run.PriceCents,
                    run.Capacity,
                    [.. Sittings(run)]))]))
            .ToList();

        var added = await business.SeedAsync(seeds, SeedActor, cancellationToken);
        LogSeeded(added);
    }

    /// <summary>One sitting per week, each at the same wall-clock time in the gallery's zone.</summary>
    private static IEnumerable<ClassSeedSession> Sittings(SpikeRun run)
    {
        for (var week = 0; week < run.Sessions; week++)
        {
            var startsAt = run.FirstSession.AddDays(7 * week);
            yield return new ClassSeedSession(GalleryTime(startsAt), GalleryTime(startsAt.AddHours(run.Hours)));
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
