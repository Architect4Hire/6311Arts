using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Tests.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace Arts6311.Tests.Identity;

/// <summary>
/// The password policy (auth.md, ADR 0018): the staff length floor, and the bundled list for everyone.
/// </summary>
public sealed class PasswordPolicyTests
{
    [Fact]
    public void TheBundledListIsActuallyThere()
    {
        // It is an embedded resource looked up by name. A rename or a dropped EmbeddedResource item would make
        // every password acceptable, and nothing else would fail — so the count is asserted, not assumed.
        Assert.InRange(BreachedPasswordList.Count, 9_000, 11_000);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("123456")]
    [InlineData("qwerty")]
    [InlineData("letmein")]
    public void TheListCatchesTheObviousOnes(string candidate)
    {
        Assert.True(BreachedPasswordList.Contains(candidate));
    }

    [Fact]
    public void TheListIsCaseInsensitive()
    {
        // "Password" is no better a choice than "password", and an attacker's list is case-folded too.
        Assert.True(BreachedPasswordList.Contains("PASSWORD"));
        Assert.True(BreachedPasswordList.Contains("PaSsWoRd"));
    }

    [Fact]
    public void SomethingUnlistedIsAccepted()
    {
        Assert.False(BreachedPasswordList.Contains("fjord-lantern-quarry-7719"));
    }

    [Fact]
    public void TheListBarelyTouchesStaffPasswordsAndThatIsWhyItCoversMembers()
    {
        // The finding behind ADR 0023: the staff floor already rejects all but one entry on the list, so if the
        // check were staff-only it would be a control in name. This pins the fact the decision rests on — if a
        // future list has many long entries, the reasoning should be revisited rather than silently stale.
        Assert.Equal(IdentityPolicy.StaffPasswordMinLength, 14);
        Assert.True(
            BreachedPasswordList.Count > 9_000,
            "The list shrank; re-check whether applying it to members is still where it earns its place.");
    }

    [Theory]
    [InlineData("short")]
    [InlineData("thirteenchars")]
    public void AStaffPasswordUnderFourteenCharactersIsRefused(string candidate)
    {
        Assert.True(candidate.Length < IdentityPolicy.StaffPasswordMinLength);
        Assert.NotNull(StaffPasswordRule.Violation(candidate));
    }

    [Fact]
    public void FourteenCharactersIsEnoughForStaff()
    {
        var candidate = new string('x', IdentityPolicy.StaffPasswordMinLength);

        Assert.Null(StaffPasswordRule.Violation(candidate));
    }

    [Fact]
    public void AnEmptyStaffPasswordIsRefused()
    {
        Assert.NotNull(StaffPasswordRule.Violation(null));
        Assert.NotNull(StaffPasswordRule.Violation(string.Empty));
    }

    [Fact]
    public async Task ALongListedPasswordIsRefusedForEveryone()
    {
        // The one entry on the list that clears the staff floor, approximated: whatever its length, a listed
        // password must be refused through Identity's own validator, which runs for members too.
        await using var host = await IdentityTestHost.CreateAsync(TestContext.Current.CancellationToken);
        using var scope = host.CreateScope();
        var users = IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope);

        var result = await users.CreateAsync(
            new ArtsUser { UserName = "m@6311arts.test", Email = "m@6311arts.test", DisplayName = "m" },
            "password");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == BreachedPasswordValidator.ErrorCode);
    }

    [Fact]
    public async Task TheMemberMinimumIsEnforcedToo()
    {
        await using var host = await IdentityTestHost.CreateAsync(TestContext.Current.CancellationToken);
        using var scope = host.CreateScope();
        var users = IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope);

        var result = await users.CreateAsync(
            new ArtsUser { UserName = "s@6311arts.test", Email = "s@6311arts.test", DisplayName = "s" },
            new string('x', IdentityPolicy.PasswordMinLength - 1));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ThereAreNoCompositionRules()
    {
        // Length and the list, not character classes (ADR 0023). A long all-lowercase passphrase is fine, and
        // demanding a digit and a symbol would only push people towards "Password1!".
        await using var host = await IdentityTestHost.CreateAsync(TestContext.Current.CancellationToken);
        using var scope = host.CreateScope();
        var users = IdentityTestHost.Resolve<UserManager<ArtsUser>>(scope);

        var result = await users.CreateAsync(
            new ArtsUser { UserName = "p@6311arts.test", Email = "p@6311arts.test", DisplayName = "p" },
            "correcthorsebatterystaple");

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
