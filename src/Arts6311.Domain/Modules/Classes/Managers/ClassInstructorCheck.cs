using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>
/// What the Instructors module said about the instructors a write names, asked by the facade (backend.md:
/// cross-module traffic is facade to facade). Business decides what the answer means: an instructor who is not there
/// is refused; an archived one is refused only for a run that did not already have them (ADR 0033, 0044).
/// </summary>
/// <param name="Known">The instructors that exist, by id. One the write names that is missing here does not exist.</param>
internal sealed record ClassInstructorCheck(IReadOnlyDictionary<Guid, ClassInstructorFact> Known)
{
    /// <summary>The answer for a write that names no instructor: nothing to check.</summary>
    public static ClassInstructorCheck None { get; } = new(new Dictionary<Guid, ClassInstructorFact>());
}

/// <summary>An instructor's name, for a refusal staff can read, and status.</summary>
internal sealed record ClassInstructorFact(string Name, PublishStatus Status);
