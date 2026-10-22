namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>
/// What the facade asked the other modules before a publish or a schedule: Media about the hero, Instructors about
/// the instructors the runs name. One record, because both come from one read of the offering, and a run can name
/// an instructor that has since been deleted (ADR 0052).
/// </summary>
internal sealed record ClassPublishChecks(ClassMediaCheck Media, ClassInstructorCheck Instructors);
