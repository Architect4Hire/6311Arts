namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>One development class, as the seeder hands it to Business.</summary>
/// <param name="StartingPriceCents">What a seat costs; <c>0</c> for the free events the spike lists.</param>
/// <param name="Sessions">Its sittings, in start order. Each gets a suggested SKU.</param>
internal sealed record ClassSeed(
    string Title,
    string Summary,
    string? Body,
    string? ClassType,
    string? Level,
    string? Instructor,
    string? Cadence,
    long StartingPriceCents,
    IReadOnlyList<ClassSeedSession> Sessions);

/// <summary>One development sitting: when it meets and how many seats it has.</summary>
internal sealed record ClassSeedSession(DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity);
