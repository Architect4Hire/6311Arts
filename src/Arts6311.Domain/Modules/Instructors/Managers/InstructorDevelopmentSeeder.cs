using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Instructors.Business;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Instructors.Managers;

/// <summary>
/// The spike's instructor profiles (<c>projects/site/src/app/data/instructors.ts</c>), the people teaching the spike's
/// classes, so a clean clone's CMS and site have instructors to show and the class seed has instructors to name.
/// Development only.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent as <see cref="IDataSeeder"/> requires: an instructor whose slug is already in use is left alone, so a
/// rerun adds nothing and never undoes a staff edit. Seeded as Published without portraits: the spike lists every one
/// of them publicly and has no portraits to offer. The bios are the spike's draft copy.
/// </para>
/// <para>
/// The artist link is resolved through <see cref="IArtistsFacade"/> by slug, after the artist roster's own seeder has
/// run (MigrationService registers it first). An instructor with no artist of that name, or one staff have archived
/// since, is seeded without a link.
/// </para>
/// </remarks>
internal sealed partial class InstructorDevelopmentSeeder(
    IInstructorsBusiness business,
    IArtistsFacade artists,
    ILogger<InstructorDevelopmentSeeder> logger) : IDataSeeder
{
    /// <summary>
    /// <c>UpdatedBy</c> on a seeded instructor. Not a user id, so the importer's "edited by staff since?" comparison
    /// (content.md) can tell a seeded row from a staff one. The same actor as the artist seed.
    /// </summary>
    public const string SeedActor = "system:development-seed";

    /// <summary>One spike profile, and whether the spike links them to an artist of the same name.</summary>
    internal sealed record SpikeInstructor(string Name, string Medium, string Bio, bool AlsoArtist = false);

    internal static readonly IReadOnlyList<SpikeInstructor> Roster =
    [
        new("Candice Johnson", "Drawing & mixed media",
            "Candice teaches our drawing and mixed-media workshops, from scratchboard to Day of the Dead ofrendas, and sells her work in the 6311arts shop.",
            AlsoArtist: true),
        new("Luis Valenzuela Jr.", "Talks & art history",
            "Luis leads our talks on art and activism, tracing how artists have organized and made the case for change."),
        new("Michelle Peterson-Albandoz", "Exhibiting artist",
            "Michelle shows at 6311arts and hosts the opening reception for “Boats & Pianos” in the East gallery."),
        new("Gwen Schilling", "Printmaking",
            "Gwen runs Intro to Screen Printing, the class most first-timers start with. Everyone leaves with something they pulled themselves."),
        new("Diego Granath", "Printmaking",
            "Diego teaches intermediate screen printing, bringing the press-room habits he uses at Rathaus into our studio."),
        new("Nicholas Waguespack", "Printmaking",
            "Nicholas teaches Sunday-morning screen printing and runs Printmaking Playground, the open session where you try a process without signing up for a course."),
    ];

    public string Name => "Development instructors";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var artistSlugs = Roster.Where(entry => entry.AlsoArtist).Select(entry => Slugs.FromName(entry.Name)).ToList();
        var linked = (await artists.FindBySlugsAsync(artistSlugs, cancellationToken))
            .Where(artist => artist.Status != PublishStatus.Archived)
            .ToDictionary(artist => artist.Slug, artist => artist.Id, StringComparer.Ordinal);

        var seeds = Roster
            .Select(entry => new InstructorSeed(
                entry.Name,
                entry.Medium,
                entry.Bio,
                entry.AlsoArtist && linked.TryGetValue(Slugs.FromName(entry.Name), out var artistId) ? artistId : null))
            .ToList();

        var added = await business.SeedAsync(seeds, SeedActor, cancellationToken);
        LogSeeded(added);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} development instructors.")]
    private partial void LogSeeded(int count);
}
