using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Data.Entities;

namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>
/// What the classes list's order needs from the query (ADR 0062): the moment it is read, which the next session is
/// counted from, and the instructors the runs' foreign key reaches, whose first name alphabetically the "instructors"
/// column orders by. Read-only, and only in the ORDER BY, its keyset and the cursor's value; the names shown still
/// come from the Instructors facade.
/// </summary>
internal sealed record ClassListContext(IQueryable<Instructor> Instructors, DateTimeOffset AsOf);

/// <summary>
/// The classes list's sortable columns (ADR 0062): every column the CMS table shows. By title unless asked. A class
/// with no session still to come, or no instructor, sorts last on that column either way round.
/// </summary>
internal static class ClassListOrder
{
    public static readonly ListOrder<ClassOffering, ClassListRow> Columns =
        new ListOrder<ClassOffering, ClassListRow>(offering => offering.Id, row => row.Id, ListSort.Ascending("title"))
            .By("title", offering => offering.Title, row => row.Title)
            .By<ClassListContext, DateTimeOffset?>(
                "next",
                (offering, context) => offering.Runs
                    .SelectMany(run => run.Sessions)
                    .Where(session => session.StartsAt >= context.AsOf)
                    .Min(session => (DateTimeOffset?)session.StartsAt),
                row => row.NextSessionStartsAt)
            .By("status", offering => offering.Status, row => row.Status)
            .By("runs", offering => offering.Runs.Count, row => row.RunCount)
            .By<ClassListContext, string?>(
                "instructors",
                (offering, context) => context.Instructors
                    .Where(instructor => offering.Runs.Any(run => run.InstructorId == instructor.Id))
                    .Min(instructor => (string?)instructor.Name),
                row => row.FirstInstructorName)
            .By("updated", offering => offering.UpdatedAt, row => row.UpdatedAt);
}
