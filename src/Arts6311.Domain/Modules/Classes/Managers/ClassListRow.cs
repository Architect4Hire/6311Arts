using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>
/// One row of the CMS list as the repository projects it: everything in <see cref="ClassSummaryServiceModel"/> but
/// the instructors' names, which only the Instructors module can give, so the row carries their ids: one per run
/// that names one, repeats included, because a de-duplicating subquery needs SQL APPLY, which SQLite lacks.
/// </summary>
internal sealed record ClassListRow(
    Guid Id,
    string Title,
    string Slug,
    PublishStatus Status,
    string? ClassType,
    string? Level,
    IReadOnlyList<Guid> InstructorIds,
    Guid? HeroMediaId,
    int RunCount,
    DateTimeOffset? NextSessionStartsAt,
    DateTimeOffset UpdatedAt)
{
    /// <param name="names">Instructor id → name. An id with no name (gone since) is left out.</param>
    public ClassSummaryServiceModel ToServiceModel(IReadOnlyDictionary<Guid, string> names) => new(
        Id,
        Title,
        Slug,
        Status,
        ClassType,
        Level,
        [.. InstructorIds.Where(names.ContainsKey).Select(id => names[id]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
        HeroMediaId,
        RunCount,
        NextSessionStartsAt,
        UpdatedAt);
}
