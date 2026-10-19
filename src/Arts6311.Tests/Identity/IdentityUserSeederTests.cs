using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Tests.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Identity;

/// <summary>
/// The first Admin and the three roles (auth.md). Run by <c>MigrationWorker</c> after every migration, so it
/// runs again on every restart and must be safe to.
/// </summary>
public sealed class IdentityUserSeederTests
{
    private static readonly SeedAdminOptions Seed = new()
    {
        Email = "admin@6311arts.test",
        Password = "a-long-enough-seed-passphrase",
        DisplayName = "6311arts Admin",
    };

    [Fact]
    public async Task ItCreatesTheThreeRolesAndTheAdmin()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct, Seed);

        await SeedAsync(host);

        using var scope = host.CreateScope();
        var roles = IdentityTestHost.Resolve<RoleManager<ArtsRole>>(scope);
        foreach (var role in IdentityRoles.All)
        {
            Assert.True(await roles.RoleExistsAsync(role), role);
        }

        var users = IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope);
        var admin = await users.FindByEmailAsync(Seed.Email);
        Assert.NotNull(admin);
        Assert.Equal([IdentityRoles.Admin], await users.GetRolesAsync(admin));
    }

    [Fact]
    public async Task TheSeededAdminIsPreConfirmedSoItCanActuallySignIn()
    {
        // The documented exception (ADR 0023). An unconfirmed seed would be refused by the staff rule, and the
        // confirmation link points at a browser route that does not exist until 1.10 — so the only account in the
        // system would be locked out of it.
        await using var host = await IdentityTestHost.CreateAsync(Ct, Seed);

        await SeedAsync(host);

        using var scope = host.CreateScope();
        var admin = await IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope).FindByEmailAsync(Seed.Email);

        Assert.True(admin!.EmailConfirmed);
    }

    [Fact]
    public async Task RunningItTwiceChangesNothing()
    {
        // IDataSeeder requires idempotence, and this one runs on every `aspire run`.
        await using var host = await IdentityTestHost.CreateAsync(Ct, Seed);

        await SeedAsync(host);
        await SeedAsync(host);

        using var scope = host.CreateScope();
        var context = IdentityTestHost.Resolve<ArtsDbContext>(scope);

        Assert.Equal(1, await context.Users.CountAsync(Ct));
        Assert.Equal(IdentityRoles.All.Length, await context.Roles.CountAsync(Ct));
        Assert.Equal(1, await context.UserRoles.CountAsync(Ct));
    }

    [Fact]
    public async Task ARerunNeverResetsAnExistingAdminsPassphrase()
    {
        // The dangerous version of "idempotent": re-applying the seed password would silently hand the account
        // back to whoever knows the original parameter, after someone had changed it.
        await using var host = await IdentityTestHost.CreateAsync(Ct, Seed);
        await SeedAsync(host);

        using (var scope = host.CreateScope())
        {
            var users = IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope);
            var admin = await users.FindByEmailAsync(Seed.Email);
            var changed = await users.ChangePasswordAsync(admin!, Seed.Password, "a-different-long-passphrase");
            Assert.True(changed.Succeeded);
        }

        await SeedAsync(host);

        using (var scope = host.CreateScope())
        {
            var users = IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope);
            var admin = await users.FindByEmailAsync(Seed.Email);

            Assert.False(await users.CheckPasswordAsync(admin!, Seed.Password));
            Assert.True(await users.CheckPasswordAsync(admin!, "a-different-long-passphrase"));
        }
    }

    [Fact]
    public async Task ItRecordsAnAuditRowSayingTheAdminWasSeeded()
    {
        // So "why is this account confirmed?" has an answer in the audit trail rather than only in an ADR.
        await using var host = await IdentityTestHost.CreateAsync(Ct, Seed);

        await SeedAsync(host);

        using var scope = host.CreateScope();
        var context = IdentityTestHost.Resolve<ArtsDbContext>(scope);
        var entry = await context.AuditLogs.SingleAsync(log => log.Action == IdentityAudit.AdminSeeded, Ct);

        Assert.Equal(IdentityAudit.UserResourceType, entry.ResourceType);
        Assert.DoesNotContain(Seed.Password, entry.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AWeakSeedPassphraseStopsTheRun()
    {
        // A seeded account exempt from the staff rule would be a hole. The migration run fails with a non-zero
        // exit, so the AppHost's WaitForCompletion stops everything downstream.
        await using var host = await IdentityTestHost.CreateAsync(
            Ct, new SeedAdminOptions { Email = Seed.Email, Password = "short" });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync(host));

        Assert.Contains("staff policy", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("short", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARolesOnlyRunIsFineWhenNoAdminIsConfigured()
    {
        // A deployment may manage its first admin another way; the roles still have to exist.
        await using var host = await IdentityTestHost.CreateAsync(Ct, seedAdmin: null);

        await SeedAsync(host);

        using var scope = host.CreateScope();
        var context = IdentityTestHost.Resolve<ArtsDbContext>(scope);

        Assert.Equal(IdentityRoles.All.Length, await context.Roles.CountAsync(Ct));
        Assert.Equal(0, await context.Users.CountAsync(Ct));
    }

    [Fact]
    public async Task ItDoesNotRecreateAnAdminUnderADifferentAddress()
    {
        // Keyed on "does any Admin exist?" rather than on the address. Otherwise changing seed-admin-email, or
        // renaming the admin, would re-assert an Admin with the configured known password on the next migration
        // run — handing back a standing credential somebody may have retired on purpose (ADR 0023).
        await using var host = await IdentityTestHost.CreateAsync(Ct, Seed);
        await SeedAsync(host);

        using (var scope = host.CreateScope())
        {
            var users = IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope);
            var admin = await users.FindByEmailAsync(Seed.Email);
            admin!.Email = "renamed@6311arts.test";
            admin.UserName = "renamed@6311arts.test";
            Assert.True((await users.UpdateAsync(admin)).Succeeded);
        }

        await SeedAsync(host);

        using (var scope = host.CreateScope())
        {
            var context = IdentityTestHost.Resolve<ArtsDbContext>(scope);

            // Still one account, and no new one under the configured address.
            Assert.Equal(1, await context.Users.CountAsync(Ct));
            Assert.Null(await IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope).FindByEmailAsync(Seed.Email));
        }
    }

    private static async Task SeedAsync(IdentityTestHost host)
    {
        using var scope = host.CreateScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>())
        {
            await seeder.SeedAsync(Ct);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
