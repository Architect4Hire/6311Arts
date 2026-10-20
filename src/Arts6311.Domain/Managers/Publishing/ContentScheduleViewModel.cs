using FluentValidation;

namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// The body of the <c>schedule</c> command: when the scheduled publisher should publish the record (UTC).
/// </summary>
/// <remarks>
/// In the shared kernel beside the lifecycle it belongs to, rather than copied into each module's
/// <c>Managers/ViewModels/</c>: the command is the kernel's, every publishable type takes exactly this body, and
/// three identical records would give the API three names for one shape (ADR 0036). A type with a rule of its own
/// about scheduling still enforces it in its own Business.
/// </remarks>
/// <param name="ScheduledFor">When to publish. Must be in the future, which Business checks against <c>IClock</c>.</param>
public sealed record ContentScheduleViewModel(DateTimeOffset ScheduledFor);

/// <summary>
/// Shape only: that a time was sent at all. "In the future" is a rule, so Business owns it (backend.md "Where an
/// ambiguous rule goes") — a validator has no clock.
/// </summary>
internal sealed class ContentScheduleValidator : AbstractValidator<ContentScheduleViewModel>
{
    public ContentScheduleValidator() =>
        RuleFor(schedule => schedule.ScheduledFor)
            .NotEqual(default(DateTimeOffset))
            .WithMessage("Choose when to publish this.");
}
