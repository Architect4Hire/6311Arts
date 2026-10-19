using Microsoft.AspNetCore.Identity;

namespace Arts6311.Domain.Modules.Identity.Data.Entities;

/// <summary>
/// A staff member or site member. ASP.NET Core Identity owns the credential columns — password hash, security
/// stamp, lockout, email confirmation — and this adds only what the application needs beyond them (ADR 0002).
/// </summary>
/// <remarks>
/// <para>
/// String keys, inherited from <see cref="IdentityUser"/>, rather than <c>Guid</c>. The shared kernel already
/// chose this: <c>AuditLog.ActorUserId</c> is a <c>string?</c> whose length limit is documented as "the length of
/// an ASP.NET Core Identity user id column", and ADR 0020 sizes idempotency subjects for <c>user:{id}</c> against
/// the same assumption.
/// </para>
/// <para>
/// There is no 2FA (ADR 0018), so Identity's two-factor columns exist in the schema and stay unused. The
/// password is the only factor, which is why lockout, the confirmed-email requirement for staff and the
/// password policy are not optional here.
/// </para>
/// </remarks>
public sealed class ArtsUser : IdentityUser
{
    /// <summary>
    /// What the CMS and the member area show. Never an email address: <c>/bff/session</c> returns a display name
    /// and roles, never ids or addresses (auth.md).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>When the account was created, for support and for the audit trail.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
