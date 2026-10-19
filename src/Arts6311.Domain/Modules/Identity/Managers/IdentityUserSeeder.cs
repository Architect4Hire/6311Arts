using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arts6311.Domain.Modules.Identity.Managers;

/// <summary>
/// The seed admin's credentials, from the AppHost's <c>seed-admin-email</c> and <c>seed-admin-password</c>
/// parameters (aspire.md). Read the generated password with
/// <c>dotnet user-secrets list --project src/Arts6311.AppHost</c>.
/// </summary>
public sealed class SeedAdminOptions
{
    public const string SectionName = "SeedAdmin";

    public string Email { get; set; } = string.Empty;

    /// <summary>A secret. Never logged, never in an audit row, never in an error message.</summary>
    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "6311arts Admin";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>
/// Creates the three roles and the first Admin, so a clean clone has an account to sign in with (auth.md).
/// </summary>
/// <remarks>
/// <para>
/// Run by <c>MigrationWorker</c> after every migration, and idempotent as <see cref="IDataSeeder"/> requires:
/// existing roles are left alone and an existing admin is never modified — in particular its password is never
/// reset, so a rerun cannot hand back an account whose password someone has since changed.
/// </para>
/// <para>
/// <strong>The seeded Admin is created with its email already confirmed.</strong> auth.md requires staff to
/// confirm before reaching <c>/admin</c>, and this account is the documented exception: the confirmation link
/// points at a browser route that does not exist until 1.10, so an unconfirmed seed would lock the only account
/// out of the system it exists to administer. The audit row records that it was seeded this way (ADR 0023).
/// </para>
/// </remarks>
internal sealed partial class IdentityUserSeeder(
    UserManager<ArtsUser> users,
    RoleManager<ArtsRole> roles,
    IAuditWriter audit,
    ICorrelationContext correlation,
    IClock clock,
    IOptions<SeedAdminOptions> options,
    ILogger<IdentityUserSeeder> logger) : IDataSeeder
{
    public string Name => "Identity roles and the first Admin";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        foreach (var role in IdentityRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                Check(await roles.CreateAsync(new ArtsRole(role)), $"creating the {role} role");
                LogRoleCreated(role);
            }
        }

        var seed = options.Value;
        if (!seed.IsConfigured)
        {
            // Not an error: a deployment may manage its first admin another way. Loud enough to notice if it was
            // meant to be configured.
            LogNoSeedAdmin();
            return;
        }

        // Keyed on "does any Admin exist?", not "does this address exist?". Keyed on the address, changing
        // seed-admin-email — or deleting or renaming the admin — would recreate an Admin with the configured,
        // known password on the next migration run, so every restart would re-assert a standing credential
        // somebody may have deliberately retired (ADR 0023).
        if (await AnyAdminExistsAsync())
        {
            LogAdminExists();
            return;
        }

        // Fail before creating anything if the configured password would not pass the staff policy. A seeded
        // account exempt from the rule every other staff password must meet is a hole, not a convenience.
        // The message says which rule, never the password.
        if (StaffPasswordRule.Violation(seed.Password) is { } violation)
        {
            throw new InvalidOperationException(
                $"{SeedAdminOptions.SectionName}:Password does not meet the staff policy: {violation} The AppHost's "
                + "generated parameter satisfies it; a value set by hand must too.");
        }

        var admin = new ArtsUser
        {
            UserName = seed.Email,
            Email = seed.Email,
            DisplayName = seed.DisplayName,
            CreatedAt = clock.UtcNow,

            // The documented exception; see the remarks above.
            EmailConfirmed = true,
        };

        Check(await users.CreateAsync(admin, seed.Password), "creating the seed Admin");
        Check(await users.AddToRoleAsync(admin, IdentityRoles.Admin), "assigning the Admin role");

        audit.Record(new AuditEntry(
            ActorUserId: null,
            IdentityAudit.AdminSeeded,
            IdentityAudit.UserResourceType,
            admin.Id,
            correlation.Current,
            "First Admin seeded by the migration service with its email pre-confirmed (ADR 0023)."));

        // UserManager has already saved the user and the role assignment; this commits the audit row.
        await users.UpdateSecurityStampAsync(admin);
        LogAdminSeeded();
    }

    /// <summary>
    /// Whether anybody already holds the Admin role. <c>GetUsersInRoleAsync</c> rather than a count of users: a
    /// database with members but no staff still needs its first Admin.
    /// </summary>
    private async Task<bool> AnyAdminExistsAsync() =>
        (await users.GetUsersInRoleAsync(IdentityRoles.Admin)).Count > 0;

    /// <summary>
    /// Turns an <see cref="IdentityResult"/> into an exception the migration run reports as a non-zero exit.
    /// </summary>
    /// <remarks>
    /// Only Identity's own error codes and descriptions reach the message — never the password, which is what
    /// most of these failures are about.
    /// </remarks>
    private static void Check(IdentityResult result, string what)
    {
        if (result.Succeeded)
        {
            return;
        }

        var reasons = string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"Identity seeding failed while {what}. {reasons}");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the {Role} role.")]
    private partial void LogRoleCreated(string role);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No seed admin is configured (SeedAdmin:Email / SeedAdmin:Password); none was created.")]
    private partial void LogNoSeedAdmin();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "An Admin already exists; no account was created or modified.")]
    private partial void LogAdminExists();

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded the first Admin with a pre-confirmed email.")]
    private partial void LogAdminSeeded();
}
