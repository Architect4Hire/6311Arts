namespace Arts6311.Domain.Modules.Identity.Managers;

/// <summary>The three roles (auth.md). The API's policies read these names from the token's <c>role</c> claim.</summary>
public static class IdentityRoles
{
    /// <summary>Everything, including deletes, sync replay and user management.</summary>
    public const string Admin = "Admin";

    /// <summary>CMS content.</summary>
    public const string Editor = "Editor";

    /// <summary>The member area on the public site.</summary>
    public const string Member = "Member";

    /// <summary>
    /// The roles that reach <c>/admin</c>. They carry the extra controls that stand in for the 2FA ADR 0018
    /// removed: a confirmed email, a longer password, and shorter sessions.
    /// </summary>
    public static readonly string[] Staff = [Admin, Editor];

    public static readonly string[] All = [Admin, Editor, Member];

    public static bool IsStaff(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        return roles.Any(role => Staff.Contains(role, StringComparer.Ordinal));
    }
}
