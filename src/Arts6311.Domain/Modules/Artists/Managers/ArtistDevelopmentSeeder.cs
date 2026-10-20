using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Artists.Business;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Artists.Managers;

/// <summary>
/// The spike's artist roster (<c>projects/site/src/app/data/mock-shop.ts</c>, copied from 6311arts.com on
/// 2026-10-18), so a clean clone's CMS and, from Phase 3, its site have artists to show. Development only.
/// </summary>
/// <remarks>
/// Idempotent as <see cref="IDataSeeder"/> requires: an artist whose slug is already in use is left alone, so a
/// rerun adds nothing and never undoes a staff edit. Seeded as Published without portraits: the spike lists every
/// one of them publicly and has no portraits to offer.
/// </remarks>
internal sealed partial class ArtistDevelopmentSeeder(
    IArtistsBusiness business,
    ILogger<ArtistDevelopmentSeeder> logger) : IDataSeeder
{
    /// <summary>
    /// <c>UpdatedBy</c> on a seeded artist. Not a user id, so the importer's "edited by staff since?" comparison
    /// (content.md) can tell a seeded row from a staff one.
    /// </summary>
    public const string SeedActor = "system:development-seed";

    /// <summary>Name and, where the shop's categories said, medium.</summary>
    internal static readonly IReadOnlyList<(string Name, string? Medium)> Roster =
    [
        ("Mike Sebahar", null), ("Candice Johnson", "Drawing & mixed media"), ("Socorro Mucino", null),
        ("Micki Croisant", null), ("Mike Winchester", null), ("Nolan Code", null), ("Susan Volk", "Ceramics"),
        ("Jesy Grose", null), ("Harry Sudman", "Painting"), ("Wendy Love", null), ("Brian Eaton", "Painting"),
        ("Todd Lewis", null), ("Audrey Avril", "Illustration & prints"), ("Joseph Frumkin", null),
        ("Marco Da'Vila", null), ("Carrie Haase", "Jewelry"), ("Hedda Lubin", null), ("Kevin Johnson", null),
        ("Justin Matthew", null), ("Eddie Pedroza", "Photography"), ("Aaron Becker", null),
        ("Ralph Skunkie Davis", null), ("Annette Baksinskas", null), ("Madeline Usher", null), ("Xavier Wynn", null),
        ("Roslyn Broder", null), ("Carol Hammerman", null), ("Paul Schultz", null), ("Michael Johnson", null),
        ("Stephen Bell", "Sculpture"), ("Alejandro Figueroa", null), ("Hector Hernandez", "Portraiture"),
        ("Katherine Knapp", null), ("Aaron Wooten", "Painting"), ("Anoush Bargamian", null), ("Jenna Summa", null),
        ("Keith Sklar", null), ("Jeanne Reilly", null), ("Anne Terpstra", null), ("Jayne Lilienfeld", null),
        ("Adrian Zavala", null), ("Mary Laskey", null), ("Eddwin Meyers", null), ("Alejandra Guerrero", null),
        ("Jeffrey Brooks", null), ("Piloto", "Sculpture"), ("Dan Steinke", null), ("Jeremy Humbert", null),
        ("Jacob Vega", null), ("Lisa Barcy", null), ("Jacobo", null),
    ];

    public string Name => "Development artist roster";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var added = await business.SeedAsync(Roster, SeedActor, cancellationToken);
        LogSeeded(added);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} development artists.")]
    private partial void LogSeeded(int count);
}
