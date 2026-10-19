using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using Arts6311.Tests.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace Arts6311.Tests.Identity;

/// <summary>
/// Security-stamp revalidation (auth.md "Revalidation"). This is the mechanism behind "a password reset ends
/// sessions" and "log out everywhere": both move the stamp, and every live ticket fails its next check.
/// </summary>
public sealed class SessionValidationTests
{
    private const string Correct = "a-perfectly-fine-passphrase";

    [Fact]
    public async Task AMatchingStampIsValidAndReturnsCurrentRoles()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("e@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Editor);

        var result = await ValidateAsync(host, user.Id, user.SecurityStamp!);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.Valid);
        Assert.Equal([IdentityRoles.Editor], result.Value.Roles);
    }

    [Fact]
    public async Task AStaleStampIsInvalid()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("e@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Editor);
        var oldStamp = user.SecurityStamp!;

        using (var scope = host.CreateScope())
        {
            // What a password reset does under the covers.
            var users = IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope);
            var tracked = await users.FindByIdAsync(user.Id);
            await users.UpdateSecurityStampAsync(tracked!);
        }

        var result = await ValidateAsync(host, user.Id, oldStamp);

        Assert.False(result.Value!.Valid);
        Assert.Empty(result.Value.Roles);
    }

    [Fact]
    public async Task ChangingThePassphraseInvalidatesALiveSession()
    {
        // The property that matters, stated end to end rather than as "the stamp moved": after a password change
        // the ticket the gateway is holding stops working.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("e@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        var stampOnTheTicket = user.SecurityStamp!;

        using (var scope = host.CreateScope())
        {
            var users = IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope);
            var tracked = await users.FindByIdAsync(user.Id);
            var changed = await users.ChangePasswordAsync(tracked!, Correct, "another-fine-long-passphrase");
            Assert.True(changed.Succeeded, string.Join("; ", changed.Errors.Select(error => error.Description)));
        }

        Assert.False((await ValidateAsync(host, user.Id, stampOnTheTicket)).Value!.Valid);
    }

    [Fact]
    public async Task ALockedAccountIsInvalidEvenWithTheRightStamp()
    {
        // A lockout has to reach sessions that are already signed in, not just new sign-ins.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("e@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Editor);

        using (var scope = host.CreateScope())
        {
            var users = IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope);
            var tracked = await users.FindByIdAsync(user.Id);

            // Real time, not host.Time: UserManager.IsLockedOutAsync compares LockoutEnd against the system
            // clock, not the TimeProvider in DI. A lockout set on the fake clock's timeline simply reads as
            // already expired — which also means lockout *expiry* cannot be tested by advancing a fake clock,
            // so there is deliberately no test for it here.
            await users.SetLockoutEndDateAsync(tracked!, DateTimeOffset.UtcNow.AddHours(1));
        }

        Assert.False((await ValidateAsync(host, user.Id, user.SecurityStamp!)).Value!.Valid);
    }

    [Fact]
    public async Task AnUnknownUserIsInvalidRatherThanAnError()
    {
        // A deleted account's ticket must expire quietly; the gateway deletes it and the browser is signed out.
        await using var host = await IdentityTestHost.CreateAsync(Ct);

        var result = await ValidateAsync(host, "no-such-user", "no-such-stamp");

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.Valid);
    }

    [Fact]
    public async Task AnEmptyRequestIsAValidationFailure()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);

        var result = await ValidateAsync(host, string.Empty, string.Empty);

        Assert.Equal(Domain.Managers.Results.ErrorCodes.ValidationFailed, result.Error!.Code);
    }

    private static async Task<Domain.Managers.Results.OperationResult<
        Domain.Modules.Identity.Managers.ServiceModels.SessionValidationServiceModel>> ValidateAsync(
        IdentityTestHost host, string userId, string stamp)
    {
        using var scope = host.CreateScope();
        return await IdentityTestHost.Facade(scope)
            .ValidateSessionAsync(new SessionValidationViewModel(userId, stamp), Ct);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
