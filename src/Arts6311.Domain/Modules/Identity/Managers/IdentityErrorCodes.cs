namespace Arts6311.Domain.Modules.Identity.Managers;

/// <summary>
/// Why a credential check failed, as stable codes.
/// </summary>
/// <remarks>
/// These are deliberately distinct, and they are for the <strong>gateway</strong>, which is not a browser. The
/// gateway needs to tell a lockout from a bad password to word its message and to audit correctly.
/// <para>
/// <strong>1.10 must collapse them into one browser response.</strong> auth.md's "no user enumeration on login or
/// forgot-password" governs what reaches a browser, and the API has no browser-facing surface at all (the 1.9
/// restriction). If the BFF passes these through, enumeration comes back (ADR 0023).
/// </para>
/// </remarks>
public static class IdentityErrorCodes
{
    /// <summary>No such account, or the wrong password. One code for both, so the API itself never enumerates.</summary>
    public const string CredentialsInvalid = "auth.credentials_invalid";

    /// <summary>Too many failed attempts; the account is locked for now.</summary>
    public const string LockedOut = "auth.locked_out";

    /// <summary>Staff must confirm their email before reaching the CMS (auth.md, ADR 0018).</summary>
    public const string EmailUnconfirmed = "auth.email_unconfirmed";

    /// <summary>A password that does not meet the policy. Carries field errors saying which rule.</summary>
    public const string PasswordRejected = "auth.password_rejected";
}
