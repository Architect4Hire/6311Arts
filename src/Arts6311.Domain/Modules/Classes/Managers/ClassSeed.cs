namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>One development class, as the seeder hands it to Business.</summary>
/// <param name="Runs">The terms it is offered in. Each gets a suggested SKU.</param>
internal sealed record ClassSeed(
    string Title,
    string Summary,
    string? Body,
    string? ClassType,
    string? Level,
    IReadOnlyList<ClassSeedRun> Runs);

/// <summary>One development run: who teaches it, what a seat costs, how many there are, and its sittings.</summary>
/// <param name="InstructorId">Who teaches it, as the seeder found them in the Instructors module; <c>null</c> when it did not.</param>
/// <param name="StartingPriceCents">What a seat costs; <c>0</c> for the free events the spike lists.</param>
/// <param name="Sessions">Its sittings, in start order.</param>
internal sealed record ClassSeedRun(
    string? Label,
    Guid? InstructorId,
    string? Cadence,
    long StartingPriceCents,
    int Capacity,
    IReadOnlyList<ClassSeedSession> Sessions);

/// <summary>One development sitting: when it meets.</summary>
internal sealed record ClassSeedSession(DateTimeOffset StartsAt, DateTimeOffset EndsAt);
