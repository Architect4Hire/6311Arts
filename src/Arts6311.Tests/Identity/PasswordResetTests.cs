using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using Arts6311.Tests.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Identity;

/// <summary>
/// Setting a new password with an emailed link (auth.md, ADR 0028), against real Identity: the token, the staff
/// rule, the bundled list and the security stamp are the behaviour under test.
/// </summary>
public sealed class PasswordResetTests
{
    private const string Passphrase = "a-perfectly-fine-passphrase";
    private const string NewPassphrase = "another-perfectly-fine-one";

    [Fact]
    public async Task AValidLinkSetsTheNewPasswordOnce()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Passphrase, emailConfirmed: true, IdentityRoles.Member);
        var token = await ResetTokenAsync(host, user.Id);

        var first = await ResetAsync(host, user.Id, token, NewPassphrase);
        Assert.True(first.Succeeded, first.Error?.Code);

        Assert.True(await SignsInAsync(host, "m@6311arts.test", NewPassphrase));
        Assert.False(await SignsInAsync(host, "m@6311arts.test", Passphrase));

        // The stamp the first use rotated is inside the token, so the same link cannot verify a second time.
        var second = await ResetAsync(host, user.Id, token, "yet-another-perfectly-fine-one");
        Assert.Equal(IdentityErrorCodes.TokenInvalid, second.Error!.Code);
        Assert.True(await SignsInAsync(host, "m@6311arts.test", NewPassphrase));
    }

    [Fact]
    public async Task AResetEndsEveryLiveSessionAtItsNextRevalidation()
    {
        // The API's half of "a reset ends a second live session": the stamp every ticket holds stops validating.
        // The gateway's half — that a failed revalidation ends the session — is BffAccountTests'.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("e@6311arts.test", Passphrase, emailConfirmed: true, IdentityRoles.Editor);
        var stampOnALiveTicket = user.SecurityStamp!;

        Assert.True(await StampValidatesAsync(host, user.Id, stampOnALiveTicket));

        var result = await ResetAsync(host, user.Id, await ResetTokenAsync(host, user.Id), NewPassphrase);
        Assert.True(result.Succeeded, result.Error?.Code);

        Assert.False(await StampValidatesAsync(host, user.Id, stampOnALiveTicket));
    }

    [Fact]
    public async Task AnExpiredLinkIsRefused()
    {
        // DataProtectorTokenProvider reads the system clock, not TimeProvider (DataProtectionTokenProviderOptions
        // has only Name and TokenLifespan in .NET 10), so a fake clock cannot age a token — the same limit auth.md
        // records for lockout. A lifetime shorter than the test instead.
        await using var host = await IdentityTestHost.CreateAsync(Ct, configureServices: services =>
            services.Configure<DataProtectionTokenProviderOptions>(
                options => options.TokenLifespan = TimeSpan.FromMilliseconds(1)));
        var user = await host.CreateUserAsync("m@6311arts.test", Passphrase, emailConfirmed: true, IdentityRoles.Member);
        var token = await ResetTokenAsync(host, user.Id);

        await Task.Delay(TimeSpan.FromMilliseconds(50), Ct);

        var result = await ResetAsync(host, user.Id, token, NewPassphrase);

        Assert.Equal(IdentityErrorCodes.TokenInvalid, result.Error!.Code);
        Assert.True(await SignsInAsync(host, "m@6311arts.test", Passphrase));
    }

    [Theory]
    [InlineData(IdentityRoles.Admin)]
    [InlineData(IdentityRoles.Editor)]
    public async Task AShortStaffPasswordIsRefusedAndTheLinkStillWorks(string role)
    {
        // Identity's own floor is 8, so without the staff rule here a staff reset could set eight characters
        // (ADR 0023). Thirteen is one short.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("s@6311arts.test", Passphrase, emailConfirmed: true, role);
        var token = await ResetTokenAsync(host, user.Id);

        var refused = await ResetAsync(host, user.Id, token, "thirteen-char");

        Assert.Equal(IdentityErrorCodes.PasswordRejected, refused.Error!.Code);
        Assert.Contains(
            "14 characters", Assert.Single(refused.Error.FieldErrors["newPassword"]), StringComparison.Ordinal);
        Assert.True(await SignsInAsync(host, "s@6311arts.test", Passphrase));

        // A refused password spends nothing: the person picks a longer one with the same link.
        var accepted = await ResetAsync(host, user.Id, token, NewPassphrase);
        Assert.True(accepted.Succeeded, accepted.Error?.Code);
    }

    [Fact]
    public async Task AMemberMayChooseEightCharactersButNotAListedOne()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Passphrase, emailConfirmed: true, IdentityRoles.Member);
        var token = await ResetTokenAsync(host, user.Id);

        // The bundled list applies to everyone (ADR 0023), and members are where it does the work.
        var listed = await ResetAsync(host, user.Id, token, "password");
        Assert.Equal(IdentityErrorCodes.PasswordRejected, listed.Error!.Code);
        Assert.Contains(
            "commonly used", Assert.Single(listed.Error.FieldErrors["newPassword"]), StringComparison.Ordinal);

        var tooShort = await ResetAsync(host, user.Id, token, "q7v!mz2");
        Assert.Equal(IdentityErrorCodes.PasswordRejected, tooShort.Error!.Code);

        var eight = await ResetAsync(host, user.Id, token, "q7v!mz2k");
        Assert.True(eight.Succeeded, eight.Error?.Code);
    }

    [Fact]
    public async Task EveryUnusableLinkGetsTheSameAnswer()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Passphrase, emailConfirmed: false, IdentityRoles.Member);
        var other = await host.CreateUserAsync("o@6311arts.test", Passphrase, emailConfirmed: true, IdentityRoles.Member);

        OperationResult[] refusals =
        [
            await ResetAsync(host, user.Id, "not-a-token", NewPassphrase),
            await ResetAsync(host, "no-such-user", await ResetTokenAsync(host, user.Id), NewPassphrase),
            await ResetAsync(host, user.Id, await ResetTokenAsync(host, other.Id), NewPassphrase),

            // A confirmation token carries a different purpose, so it is not a reset token.
            await ResetAsync(host, user.Id, await ConfirmationTokenAsync(host, user.Id), NewPassphrase),
        ];

        Assert.All(refusals, refusal =>
        {
            Assert.Equal(IdentityErrorCodes.TokenInvalid, refusal.Error!.Code);
            Assert.Equal(refusals[0].Error!.Message, refusal.Error.Message);
            Assert.Empty(refusal.Error.FieldErrors);
        });
    }

    [Fact]
    public async Task WithoutAValidLinkAShortPasswordSaysNothingAboutTheAccount()
    {
        // The token is checked first. The other way round, this would be auth.password_rejected for staff and
        // auth.token_invalid for a member, and a user id alone would reveal which the account is.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var editor = await host.CreateUserAsync("e@6311arts.test", Passphrase, emailConfirmed: true, IdentityRoles.Editor);
        var member = await host.CreateUserAsync("m@6311arts.test", Passphrase, emailConfirmed: true, IdentityRoles.Member);

        var forEditor = await ResetAsync(host, editor.Id, "not-a-token", "short");
        var forMember = await ResetAsync(host, member.Id, "not-a-token", "short");

        Assert.Equal(IdentityErrorCodes.TokenInvalid, forEditor.Error!.Code);
        Assert.Equal(forEditor.Error, forMember.Error);
    }

    [Fact]
    public async Task TheResetAndARefusalAreAudited()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("a@6311arts.test", Passphrase, emailConfirmed: true, IdentityRoles.Admin);

        await ResetAsync(host, user.Id, "not-a-token", NewPassphrase);
        await ResetAsync(host, user.Id, await ResetTokenAsync(host, user.Id), NewPassphrase);

        using var scope = host.CreateScope();
        var context = IdentityTestHost.Resolve<ArtsDbContext>(scope);
        var actions = await context.AuditLogs
            .Where(log => log.ResourceId == user.Id)
            .Select(log => new { log.Action, log.Summary })
            .ToListAsync(Ct);

        // As a set: both rows carry the fake clock's one instant and a random id, so neither orders them.
        Assert.Equal(
            new[] { IdentityAudit.PasswordResetCompleted, IdentityAudit.PasswordResetRefused }.Order(StringComparer.Ordinal),
            actions.Select(entry => entry.Action).Order(StringComparer.Ordinal));

        // The two-save departure ADR 0023 allows rests on this: nothing in a reset stages a message.
        Assert.Empty(await context.OutboxMessages.ToListAsync(Ct));

        // No token and no password in a row every Admin can read.
        Assert.All(actions, entry =>
        {
            Assert.DoesNotContain(NewPassphrase, entry.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain("not-a-token", entry.Summary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AnUnknownUserLeavesNoAuditRow()
    {
        // There is no account to point it at, and an unauthenticated caller could otherwise grow the table.
        await using var host = await IdentityTestHost.CreateAsync(Ct);

        await ResetAsync(host, "no-such-user", "not-a-token", NewPassphrase);

        using var scope = host.CreateScope();
        Assert.Empty(await IdentityTestHost.Resolve<ArtsDbContext>(scope).AuditLogs.ToListAsync(Ct));
    }

    [Fact]
    public async Task AResetRequestForAnUnknownAddressWritesNothing()
    {
        // The same success as a registered address, and no row of any kind: an unauthenticated caller must not be
        // able to grow the audit or outbox tables by asking about addresses we do not hold (ADR 0023, 0028).
        await using var host = await IdentityTestHost.CreateAsync(Ct);

        using (var scope = host.CreateScope())
        {
            var result = await IdentityTestHost.Facade(scope)
                .RequestPasswordResetAsync(new PasswordResetRequestViewModel("nobody@6311arts.test"), Ct);
            Assert.True(result.Succeeded);
        }

        using var reader = host.CreateScope();
        var context = IdentityTestHost.Resolve<ArtsDbContext>(reader);
        Assert.Empty(await context.AuditLogs.ToListAsync(Ct));
        Assert.Empty(await context.OutboxMessages.ToListAsync(Ct));
    }

    [Theory]
    [InlineData("", "token", NewPassphrase)]
    [InlineData("user", "", NewPassphrase)]
    [InlineData("user", "token", "")]
    public async Task AMissingFieldIsAValidationFailure(string userId, string token, string password)
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);

        var result = await ResetAsync(host, userId, token, password);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
    }

    [Fact]
    public async Task AnOversizedTokenIsRefusedBeforeItIsUnprotected()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);

        var result = await ResetAsync(host, "user", new string('a', 2049), NewPassphrase);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
    }

    internal static async Task<string> ResetTokenAsync(IdentityTestHost host, string userId)
    {
        using var scope = host.CreateScope();
        return (await IdentityTestHost.Facade(scope).GetPasswordResetTokenAsync(userId, Ct))!.Token;
    }

    internal static async Task<string> ConfirmationTokenAsync(IdentityTestHost host, string userId)
    {
        // Minted directly: the facade returns null for an already-confirmed address, and some tests need one.
        using var scope = host.CreateScope();
        var users = IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope);

        return await users.GenerateEmailConfirmationTokenAsync((await users.FindByIdAsync(userId))!);
    }

    private static async Task<OperationResult> ResetAsync(
        IdentityTestHost host, string userId, string token, string newPassword)
    {
        using var scope = host.CreateScope();
        return await IdentityTestHost.Facade(scope)
            .ResetPasswordAsync(new PasswordResetCompletionViewModel(userId, token, newPassword), Ct);
    }

    internal static async Task<bool> SignsInAsync(IdentityTestHost host, string email, string password)
    {
        using var scope = host.CreateScope();
        return (await IdentityTestHost.Facade(scope)
            .CheckCredentialsAsync(new CredentialCheckViewModel(email, password), Ct)).Succeeded;
    }

    internal static async Task<bool> StampValidatesAsync(IdentityTestHost host, string userId, string stamp)
    {
        using var scope = host.CreateScope();
        return (await IdentityTestHost.Facade(scope)
            .ValidateSessionAsync(new SessionValidationViewModel(userId, stamp), Ct)).Value!.Valid;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
