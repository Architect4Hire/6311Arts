using Arts6311.Domain.Modules.Identity.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace Arts6311.Domain.Modules.Identity.Managers;

/// <summary>
/// Refuses any password on the bundled list, for every account (auth.md, ADR 0018).
/// </summary>
/// <remarks>
/// Applies to members as well as staff, deliberately. The list is framed in auth.md as a staff control, but the
/// staff 14-character minimum already rejects all but one of its entries — it is on member passwords, which have
/// no such floor, that it does real work (ADR 0023).
/// </remarks>
internal sealed class BreachedPasswordValidator : IPasswordValidator<ArtsUser>
{
    /// <summary>The code the facade maps to a field error. Stable: a client may branch on it.</summary>
    public const string ErrorCode = "PasswordIsCommon";

    public Task<IdentityResult> ValidateAsync(UserManager<ArtsUser> manager, ArtsUser user, string? password)
    {
        if (!BreachedPasswordList.Contains(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        // The message says the password is known, not where it appeared: naming the source invites a guess at
        // which list, and the person only needs to know to pick something else.
        return Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = ErrorCode,
            Description = "That password appears on a list of commonly used passwords. Choose a different one.",
        }));
    }
}
