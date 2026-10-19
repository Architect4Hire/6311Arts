using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using Arts6311.Tests.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Identity;

/// <summary>
/// "Log out everywhere" on the API's side (<c>auth.md</c>): rotating the security stamp, which is what every
/// live ticket is checked against.
/// </summary>
public sealed class SessionRevocationTests
{
    private const string Passphrase = "a-perfectly-fine-passphrase";

    [Fact]
    public async Task RevokingMovesTheStampSoALiveTicketStopsValidating()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("e@6311arts.test", Passphrase, emailConfirmed: true, IdentityRoles.Editor);
        var stampOnTheTicket = user.SecurityStamp!;

        var result = await RevokeAsync(host, user.Id);

        Assert.True(result.Succeeded);

        using var scope = host.CreateScope();
        var validation = await IdentityTestHost.Facade(scope)
            .ValidateSessionAsync(new SessionValidationViewModel(user.Id, stampOnTheTicket), Ct);

        Assert.False(validation.Value!.Valid);
    }

    [Fact]
    public async Task ItRecordsAnAuditRow()
    {
        // Sessions ending everywhere is the kind of thing somebody asks about afterwards, and with no 2FA the
        // audit trail is the only account of what happened (ADR 0018).
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("e@6311arts.test", Passphrase, emailConfirmed: true, IdentityRoles.Admin);

        await RevokeAsync(host, user.Id);

        using var scope = host.CreateScope();
        var context = IdentityTestHost.Resolve<ArtsDbContext>(scope);
        var entry = await context.AuditLogs.SingleAsync(log => log.Action == IdentityAudit.SessionsRevoked, Ct);

        Assert.Equal(IdentityAudit.UserResourceType, entry.ResourceType);
        Assert.Equal(user.Id, entry.ResourceId);
        Assert.Equal(user.Id, entry.ActorUserId);
    }

    [Fact]
    public async Task RevokingTwiceIsFineAndMovesTheStampAgain()
    {
        // The gateway may well retry: the call is a rotation, not a toggle, and a second one is harmless.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("e@6311arts.test", Passphrase, emailConfirmed: true, IdentityRoles.Editor);

        await RevokeAsync(host, user.Id);
        var afterFirst = await StampAsync(host, user.Id);

        await RevokeAsync(host, user.Id);
        var afterSecond = await StampAsync(host, user.Id);

        Assert.NotEqual(user.SecurityStamp, afterFirst);
        Assert.NotEqual(afterFirst, afterSecond);
    }

    [Fact]
    public async Task AnUnknownUserIsNotFoundRatherThanASilentSuccess()
    {
        // The caller is the gateway, holding a live session for this person: no such row means the account went
        // away underneath it, which is worth saying. No enumeration concern — a browser cannot reach this route
        // and cannot choose the user id (ADR 0022).
        await using var host = await IdentityTestHost.CreateAsync(Ct);

        var result = await RevokeAsync(host, "no-such-user");

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task AnEmptyRequestIsAValidationFailure()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);

        var result = await RevokeAsync(host, string.Empty);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
    }

    private static async Task<OperationResult> RevokeAsync(IdentityTestHost host, string userId)
    {
        using var scope = host.CreateScope();
        return await IdentityTestHost.Facade(scope)
            .RevokeSessionsAsync(new SessionRevocationViewModel(userId), Ct);
    }

    private static async Task<string?> StampAsync(IdentityTestHost host, string userId)
    {
        using var scope = host.CreateScope();
        var context = IdentityTestHost.Resolve<ArtsDbContext>(scope);

        return await context.Users
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp)
            .SingleAsync(Ct);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
