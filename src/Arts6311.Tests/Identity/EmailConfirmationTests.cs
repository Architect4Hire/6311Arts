using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using Arts6311.Tests.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Identity;

/// <summary>
/// Confirming an address with an emailed link (auth.md, ADR 0028), against real Identity.
/// </summary>
public sealed class EmailConfirmationTests
{
    private const string Passphrase = "a-perfectly-fine-passphrase";

    [Fact]
    public async Task AValidLinkConfirmsTheAddressOnce()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Passphrase, emailConfirmed: false, IdentityRoles.Member);
        var token = await PasswordResetTests.ConfirmationTokenAsync(host, user.Id);

        var first = await ConfirmAsync(host, user.Id, token);
        Assert.True(first.Succeeded, first.Error?.Code);
        Assert.True(await ConfirmedAsync(host, user.Id));

        // Confirming does not rotate the stamp, so the token would still verify; the already-confirmed check is
        // what spends the link.
        var second = await ConfirmAsync(host, user.Id, token);
        Assert.Equal(IdentityErrorCodes.TokenInvalid, second.Error!.Code);
    }

    [Fact]
    public async Task ConfirmingDoesNotEndTheMembersSession()
    {
        // Why the stamp is left alone: a member who confirms while signed in would otherwise be signed out at the
        // next revalidation for doing what they were asked (ADR 0028).
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Passphrase, emailConfirmed: false, IdentityRoles.Member);

        await ConfirmAsync(host, user.Id, await PasswordResetTests.ConfirmationTokenAsync(host, user.Id));

        Assert.True(await PasswordResetTests.StampValidatesAsync(host, user.Id, user.SecurityStamp!));
    }

    [Fact]
    public async Task AConfirmedEditorCanSignIn()
    {
        // The point of the route for staff: an unconfirmed Editor is refused at sign-in until this has happened.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("e@6311arts.test", Passphrase, emailConfirmed: false, IdentityRoles.Editor);

        Assert.False(await PasswordResetTests.SignsInAsync(host, "e@6311arts.test", Passphrase));

        await ConfirmAsync(host, user.Id, await PasswordResetTests.ConfirmationTokenAsync(host, user.Id));

        Assert.True(await PasswordResetTests.SignsInAsync(host, "e@6311arts.test", Passphrase));
    }

    [Fact]
    public async Task EveryUnusableLinkGetsTheSameAnswer()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Passphrase, emailConfirmed: false, IdentityRoles.Member);
        var other = await host.CreateUserAsync("o@6311arts.test", Passphrase, emailConfirmed: false, IdentityRoles.Member);

        OperationResult[] refusals =
        [
            await ConfirmAsync(host, user.Id, "not-a-token"),
            await ConfirmAsync(host, "no-such-user", await PasswordResetTests.ConfirmationTokenAsync(host, user.Id)),
            await ConfirmAsync(host, user.Id, await PasswordResetTests.ConfirmationTokenAsync(host, other.Id)),

            // A reset token carries a different purpose.
            await ConfirmAsync(host, user.Id, await PasswordResetTests.ResetTokenAsync(host, user.Id)),
        ];

        Assert.All(refusals, refusal =>
        {
            Assert.Equal(IdentityErrorCodes.TokenInvalid, refusal.Error!.Code);
            Assert.Equal(refusals[0].Error!.Message, refusal.Error.Message);
        });
        Assert.False(await ConfirmedAsync(host, user.Id));
    }

    [Fact]
    public async Task AConfirmationAndARefusalAreAudited()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("e@6311arts.test", Passphrase, emailConfirmed: false, IdentityRoles.Editor);

        await ConfirmAsync(host, user.Id, "not-a-token");
        await ConfirmAsync(host, user.Id, await PasswordResetTests.ConfirmationTokenAsync(host, user.Id));

        using var scope = host.CreateScope();
        var actions = await IdentityTestHost.Resolve<ArtsDbContext>(scope).AuditLogs
            .Where(log => log.ResourceId == user.Id)
            .Select(log => log.Action)
            .ToListAsync(Ct);

        // As a set: both rows carry the fake clock's one instant and a random id, so neither orders them.
        Assert.Equal(
            new[] { IdentityAudit.EmailConfirmationRefused, IdentityAudit.EmailConfirmed }.Order(StringComparer.Ordinal),
            actions.Order(StringComparer.Ordinal));

        // Nothing in a confirmation stages a message, which is what the two-save shape relies on (ADR 0023).
        Assert.Empty(await IdentityTestHost.Resolve<ArtsDbContext>(scope).OutboxMessages.ToListAsync(Ct));
    }

    [Fact]
    public async Task AMissingTokenIsAValidationFailure()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);

        var result = await ConfirmAsync(host, "user", string.Empty);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
    }

    private static async Task<OperationResult> ConfirmAsync(IdentityTestHost host, string userId, string token)
    {
        using var scope = host.CreateScope();
        return await IdentityTestHost.Facade(scope)
            .ConfirmEmailAsync(new EmailConfirmationCompletionViewModel(userId, token), Ct);
    }

    private static async Task<bool> ConfirmedAsync(IdentityTestHost host, string userId)
    {
        using var scope = host.CreateScope();
        return await IdentityTestHost.Resolve<ArtsDbContext>(scope).Users
            .Where(user => user.Id == userId)
            .Select(user => user.EmailConfirmed)
            .SingleAsync(Ct);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
