namespace Arts6311.Domain.Modules.Instructors.Managers;

/// <summary>One development instructor, with the artist the seeder found for them on the roster, if any.</summary>
internal sealed record InstructorSeed(string Name, string? Medium, string? Bio, Guid? ArtistId);
