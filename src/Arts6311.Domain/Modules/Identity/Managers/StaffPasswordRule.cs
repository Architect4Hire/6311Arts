namespace Arts6311.Domain.Modules.Identity.Managers;

/// <summary>
/// The extra password rule for Admin and Editor accounts (auth.md, ADR 0018).
/// </summary>
/// <remarks>
/// <para>
/// A plain rule rather than an <c>IPasswordValidator</c> because a validator runs inside
/// <c>UserManager.CreateAsync</c>, before any role has been assigned, so it cannot tell a staff account from a
/// member one. Whoever is creating the account knows which it is, so the rule is applied there.
/// </para>
/// <para>
/// Today its only caller is the seeder, since 1.9 adds no user-management routes. It lives here, named and
/// tested, so the prompt that adds them has one place to call rather than a number to re-type.
/// </para>
/// </remarks>
public static class StaffPasswordRule
{
    /// <summary>
    /// Why this password is not acceptable for a staff account, or <c>null</c> if it is.
    /// </summary>
    /// <remarks>
    /// The breached-list check is here as well as in <c>BreachedPasswordValidator</c>, which covers everyone: for
    /// staff it is all but redundant, since only one entry on the list reaches 14 characters. Kept so that this
    /// rule is a complete statement of the staff policy on its own.
    /// </remarks>
    public static string? Violation(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < IdentityPolicy.StaffPasswordMinLength)
        {
            return $"A staff password must be at least {IdentityPolicy.StaffPasswordMinLength} characters.";
        }

        if (BreachedPasswordList.Contains(password))
        {
            return "That password appears on a list of commonly used passwords. Choose a different one.";
        }

        return null;
    }

    public static bool IsAcceptable(string? password) => Violation(password) is null;
}
