using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Identity.Managers.ServiceModels;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using Arts6311.Tests.Identity.Infrastructure;

namespace Arts6311.Tests.Identity;

/// <summary>
/// Signing in (auth.md, ADR 0018). With no second factor these rules are the whole defence, so each is asserted
/// against real Identity rather than a fake: the hashing, the lockout counters and the stamp are the behaviour.
/// </summary>
public sealed class CredentialCheckTests
{
    private const string Correct = "a-perfectly-fine-passphrase";

    [Fact]
    public async Task TheRightPassphraseReturnsTheSummaryTheGatewayNeeds()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("editor@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Editor);

        var result = await CheckAsync(host, "editor@6311arts.test", Correct);

        Assert.True(result.Succeeded, result.Error?.Code);
        Assert.Equal(user.Id, result.Value!.UserId);
        Assert.Equal("editor", result.Value.DisplayName);
        Assert.Equal([IdentityRoles.Editor], result.Value.Roles);

        // The gateway keeps this on the ticket and revalidates it, which is how a reset ends a live session.
        Assert.NotEmpty(result.Value.SecurityStamp);
    }

    [Fact]
    public async Task TheSummaryCarriesNoCredentialMaterial()
    {
        // What crosses the boundary is a display name and roles — no hash, no address, no token (ADR 0002).
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("member@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);

        var result = await CheckAsync(host, "member@6311arts.test", Correct);

        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain(Correct, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("member@6311arts.test", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AWrongPassphraseAndAnUnknownAddressFailIdentically()
    {
        // The whole point: a login response must not reveal whether an address is registered (auth.md "No user
        // enumeration"). Same code, same message, so the gateway has nothing to leak even if it wanted to.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("known@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);

        var wrong = await CheckAsync(host, "known@6311arts.test", "not-it");
        var unknown = await CheckAsync(host, "nobody@6311arts.test", Correct);

        Assert.Equal(IdentityErrorCodes.CredentialsInvalid, wrong.Error!.Code);
        Assert.Equal(IdentityErrorCodes.CredentialsInvalid, unknown.Error!.Code);
        Assert.Equal(wrong.Error.Message, unknown.Error.Message);
    }

    [Fact]
    public async Task EnoughFailuresLockTheAccount()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("target@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Editor);

        for (var attempt = 0; attempt < IdentityPolicy.MaxFailedAccessAttempts; attempt++)
        {
            var failure = await CheckAsync(host, "target@6311arts.test", "wrong");
            Assert.Equal(IdentityErrorCodes.CredentialsInvalid, failure.Error!.Code);
        }

        // And now the *correct* one is refused. Without this the threshold would be decoration: an attacker who
        // guessed right on attempt six would still be let in.
        var locked = await CheckAsync(host, "target@6311arts.test", Correct);

        Assert.Equal(IdentityErrorCodes.LockedOut, locked.Error!.Code);
    }

    [Fact]
    public async Task ASuccessClearsTheFailureCount()
    {
        // Otherwise someone who mistypes twice a week would eventually lock themselves out while typing it right.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("careless@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);

        for (var attempt = 0; attempt < IdentityPolicy.MaxFailedAccessAttempts - 1; attempt++)
        {
            await CheckAsync(host, "careless@6311arts.test", "wrong");
        }

        Assert.True((await CheckAsync(host, "careless@6311arts.test", Correct)).Succeeded);

        // Back to zero, so a fresh run of failures is needed to lock.
        for (var attempt = 0; attempt < IdentityPolicy.MaxFailedAccessAttempts - 1; attempt++)
        {
            await CheckAsync(host, "careless@6311arts.test", "wrong");
        }

        Assert.True((await CheckAsync(host, "careless@6311arts.test", Correct)).Succeeded);
    }

    [Theory]
    [InlineData("Editor")]
    [InlineData("Admin")]
    public async Task UnconfirmedStaffAreRefused(string role)
    {
        // Staff reach the CMS, and with no 2FA a confirmed address is one of the controls standing in for it.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("new-staff@6311arts.test", Correct, emailConfirmed: false, role);

        var result = await CheckAsync(host, "new-staff@6311arts.test", Correct);

        Assert.Equal(IdentityErrorCodes.EmailUnconfirmed, result.Error!.Code);
    }

    [Fact]
    public async Task AnUnconfirmedMemberIsLetIn()
    {
        // The counterpart. A member who never confirms simply never gets email; they are not shut out of the
        // member area, which is what auth.md's "staff" wording means.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("new-member@6311arts.test", Correct, emailConfirmed: false, IdentityRoles.Member);

        Assert.True((await CheckAsync(host, "new-member@6311arts.test", Correct)).Succeeded);
    }

    [Fact]
    public async Task AMissingAddressStillCostsAHash()
    {
        // Returning early for an unknown address would make it measurably faster than a wrong passphrase, which is
        // an enumeration oracle no amount of identical wording hides. Timing is too noisy to assert tightly, so
        // what is checked is that the work happens at all: an early return would be orders of magnitude faster.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("real@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);

        // Warm the hasher so neither measurement pays for first-use initialisation.
        await CheckAsync(host, "real@6311arts.test", "wrong");

        var wrong = await TimeAsync(() => CheckAsync(host, "real@6311arts.test", "wrong"));
        var unknown = await TimeAsync(() => CheckAsync(host, "ghost@6311arts.test", Correct));

        Assert.True(
            unknown > wrong / 10,
            $"An unknown address took {unknown}ms against {wrong}ms for a wrong passphrase, which suggests it "
            + "skipped the hash and is distinguishable by timing.");
    }

    [Fact]
    public async Task ALockedAccountStillCostsAHashToo()
    {
        // The subtler half of the enumeration problem: if the locked path returned without hashing, five bad
        // guesses at any address would make it answer measurably faster than an unknown one — and would keep
        // doing so after 1.10 collapses these codes into a single browser response.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("locked@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);

        for (var attempt = 0; attempt < IdentityPolicy.MaxFailedAccessAttempts; attempt++)
        {
            await CheckAsync(host, "locked@6311arts.test", "wrong");
        }

        Assert.Equal(
            IdentityErrorCodes.LockedOut,
            (await CheckAsync(host, "locked@6311arts.test", Correct)).Error!.Code);

        var locked = await TimeAsync(() => CheckAsync(host, "locked@6311arts.test", Correct));
        var unknown = await TimeAsync(() => CheckAsync(host, "ghost@6311arts.test", Correct));

        Assert.True(
            locked > unknown / 10,
            $"A locked account answered in {locked}ms against {unknown}ms for an unknown address, which suggests "
            + "it skipped the hash and is distinguishable by timing.");
    }

    [Fact]
    public async Task ParallelWrongGuessesAllCountTowardsTheLockout()
    {
        // Identity increments the failure count as a read-modify-write guarded by the concurrency stamp, so
        // simultaneous guesses collide. Without a retry some increments are lost — the threshold is softer than
        // it looks — and the loser surfaces as a 500 rather than a refusal.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("flood@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);

        var attempts = Enumerable
            .Range(0, IdentityPolicy.MaxFailedAccessAttempts)
            .Select(_ => CheckAsync(host, "flood@6311arts.test", "wrong"))
            .ToList();

        var results = await Task.WhenAll(attempts);

        // Every one is a clean refusal, not a concurrency failure.
        Assert.All(results, result => Assert.Equal(IdentityErrorCodes.CredentialsInvalid, result.Error!.Code));

        // And they all counted: the account is locked.
        Assert.Equal(
            IdentityErrorCodes.LockedOut,
            (await CheckAsync(host, "flood@6311arts.test", Correct)).Error!.Code);
    }

    [Fact]
    public async Task AnEmptyFieldIsAValidationFailureNotACredentialOne()
    {
        // Shape is the facade's business; whether it is correct is not. A blank field must not burn a lockout
        // attempt either.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("someone@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);

        var result = await CheckAsync(host, "someone@6311arts.test", string.Empty);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
    }

    [Fact]
    public async Task TheAddressIsMatchedCaseInsensitively()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("mixed@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);

        Assert.True((await CheckAsync(host, "MIXED@6311ARTS.TEST", Correct)).Succeeded);
    }

    private static async Task<OperationResult<UserSummaryServiceModel>> CheckAsync(
        IdentityTestHost host, string email, string secret)
    {
        using var scope = host.CreateScope();
        return await IdentityTestHost.Facade(scope)
            .CheckCredentialsAsync(new CredentialCheckViewModel(email, secret), Ct);
    }

    private static async Task<double> TimeAsync(Func<Task> work)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        await work();
        return started.Elapsed.TotalMilliseconds;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
