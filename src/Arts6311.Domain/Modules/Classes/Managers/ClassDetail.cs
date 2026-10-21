using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>
/// One offering as Business hands it to the facade: the CMS model with every run's
/// <see cref="ClassRunServiceModel.Instructor"/> still empty, and which instructor each run names, for the facade to
/// ask the Instructors module about (backend.md: cross-module reads go facade to facade).
/// </summary>
/// <param name="RunInstructors">Run id → instructor id, for the runs that name one.</param>
internal sealed record ClassDetail(ClassServiceModel Model, IReadOnlyDictionary<Guid, Guid> RunInstructors)
{
    /// <summary>The finished model, with each run's instructor as <paramref name="instructors"/> describes them.</summary>
    public ClassServiceModel Complete(IReadOnlyDictionary<Guid, ClassInstructorServiceModel> instructors) => Model with
    {
        Runs = [.. Model.Runs.Select(run =>
            RunInstructors.TryGetValue(run.Id, out var instructorId) && instructors.TryGetValue(instructorId, out var instructor)
                ? run with { Instructor = instructor }
                : run)],
    };
}
