namespace Arts6311.Domain.Modules.Identity.Managers;

/// <summary>
/// The credential policy (auth.md, ADR 0018). Gathered here because these numbers are the compensating controls
/// for having no second factor, and `auth.md` fixes only one of them — changing any of the rest is a decision.
/// </summary>
public static class IdentityPolicy
{
    /// <summary>
    /// Staff minimum. The one number auth.md fixes: with no 2FA, a staff password is the only thing between an
    /// attacker and the CMS.
    /// </summary>
    public const int StaffPasswordMinLength = 14;

    /// <summary>
    /// Everyone's minimum, members included. Length and a breached-list check rather than character classes:
    /// composition rules push people towards predictable substitutions without adding real entropy.
    /// </summary>
    public const int PasswordMinLength = 8;

    /// <summary>Failed attempts before the account locks.</summary>
    public const int MaxFailedAccessAttempts = 5;

    /// <summary>How long a lockout lasts. Long enough to make guessing impractical, short enough to self-heal.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long an emailed confirmation or reset link stays valid. The framework default is 24 hours; an hour is
    /// long enough to find the email and short enough that a link sitting in a mailbox later is useless.
    /// </summary>
    public static readonly TimeSpan EmailTokenLifetime = TimeSpan.FromHours(1);

    public const int DisplayNameMaxLength = 100;
}
