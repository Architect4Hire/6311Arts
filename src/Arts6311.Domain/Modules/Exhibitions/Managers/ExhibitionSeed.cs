using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Exhibitions.Managers;

/// <summary>One development exhibition, as the seeder hands it to Business: the artists already resolved to ids.</summary>
internal sealed record ExhibitionSeed(
    string Title,
    DateOnly StartDate,
    DateOnly EndDate,
    string Summary,
    ExhibitionReceptionServiceModel? Reception,
    IReadOnlyList<Guid> ArtistIds);
