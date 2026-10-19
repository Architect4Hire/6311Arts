using Microsoft.AspNetCore.Identity;

namespace Arts6311.Domain.Modules.Identity.Data.Entities;

/// <summary>
/// One of the three roles in <see cref="Managers.IdentityRoles"/>. Roles are a fixed, seeded set, not something
/// staff manage: adding one changes what the API's policies mean, so it is a code change and an ADR.
/// </summary>
public sealed class ArtsRole : IdentityRole
{
    public ArtsRole()
    {
    }

    public ArtsRole(string roleName) : base(roleName)
    {
    }
}
