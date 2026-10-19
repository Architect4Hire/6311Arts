using Arts6311.Gateway.Session;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// The startup checks on <c>Gateway:Session</c>. Each one is a misconfiguration whose only symptom would
/// otherwise be a security property quietly not holding, so each stops the host instead.
/// </summary>
public sealed class BrowserSessionOptionsTests
{
    [Fact]
    public void TheShippedConfigurationIsValid()
    {
        // The defaults in code are the ones appsettings.json repeats; if they could not pass their own checks,
        // every other test here would be testing a shape nothing ships.
        Valid().Validate();
    }

    [Theory]
    [InlineData("6311.session")]
    [InlineData("__Secure-6311.session")]
    public void ACookieWithoutTheHostPrefixIsRefused(string cookieName)
    {
        // The prefix is the security property: without it a sibling subdomain can set a cookie for us.
        var options = Valid();
        options.CookieName = cookieName;

        Assert.Contains("__Host-", Refusal(options), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/admin/login")]
    [InlineData("/admin")]
    [InlineData("/api/v1/cms/login")]
    public void ALoginPathBehindTheGateIsRefused(string loginPath)
    {
        // A login page inside a gated prefix redirects to itself forever, and cannot load its own scripts
        // (ADR 0024).
        var options = Valid();
        options.LoginPath = loginPath;

        Assert.Contains("behind the session gate", Refusal(options), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://evil.example/signin")]
    [InlineData("//evil.example/signin")]
    [InlineData("/\\evil.example/signin")]
    [InlineData("signin")]
    public void ALoginPathThatLeavesTheOriginIsRefused(string loginPath)
    {
        // It is written straight into a redirect. A protocol-relative value begins with '/' and still sends the
        // browser somewhere else entirely.
        var options = Valid();
        options.LoginPath = loginPath;

        Assert.Contains("local path", Refusal(options), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsoluteLimitShorterThanTheIdleWindowIsRefused()
    {
        var options = Valid();
        options.Staff.AbsoluteTimeout = TimeSpan.FromMinutes(5);

        Assert.Contains("AbsoluteTimeout", Refusal(options), StringComparison.Ordinal);
    }

    [Fact]
    public void ARevalidationIntervalOfZeroIsRefused()
    {
        // A session that is never revalidated outlives a password reset.
        var options = Valid();
        options.RevalidationInterval = TimeSpan.Zero;

        Assert.Contains("RevalidationInterval", Refusal(options), StringComparison.Ordinal);
    }

    [Fact]
    public void StaffLifetimesWinWheneverARoleListHasOne()
    {
        var options = Valid();

        Assert.Equal(options.Staff, options.LifetimeFor(["Admin"]));
        Assert.Equal(options.Staff, options.LifetimeFor(["Member", "Editor"]));
        Assert.Equal(options.Member, options.LifetimeFor(["Member"]));
        Assert.Equal(options.Member, options.LifetimeFor([]));
    }

    private static BrowserSessionOptions Valid()
    {
        var options = new BrowserSessionOptions();

        // The shipped numbers (appsettings.json), set explicitly so a changed default cannot make these pass.
        options.Staff.IdleTimeout = TimeSpan.FromMinutes(30);
        options.Staff.AbsoluteTimeout = TimeSpan.FromHours(8);
        options.Member.IdleTimeout = TimeSpan.FromDays(7);
        options.Member.AbsoluteTimeout = TimeSpan.FromDays(30);

        return options;
    }

    private static string Refusal(BrowserSessionOptions options) =>
        Assert.Throws<InvalidOperationException>(options.Validate).Message;
}
