using Arts6311.Worker.Email;

namespace Arts6311.Tests.Email;

/// <summary>
/// Reading the <c>mail</c> connection string.
/// </summary>
/// <remarks>
/// These exist because the first version of this parser only accepted a bare URI, while MailPit actually supplies
/// <c>Endpoint=smtp://host:port</c> — its own documentation says otherwise. The mismatch surfaced as five poisoned
/// delivery attempts with a sanitised error that named only the exception type, which is exactly the kind of
/// failure a unit test should have caught first.
/// </remarks>
public sealed class SmtpOptionsTests
{
    [Fact]
    public void TheKeyedFormMailPitActuallySuppliesIsRead()
    {
        var options = new SmtpOptions { ConnectionString = "Endpoint=smtp://localhost:65080" };

        Assert.Equal(("localhost", 65080), options.Endpoint());
    }

    [Fact]
    public void ABareUriIsReadToo()
    {
        // In case a value is set by hand, or the integration changes to match its documentation.
        var options = new SmtpOptions { ConnectionString = "smtp://mail.example:2525" };

        Assert.Equal(("mail.example", 2525), options.Endpoint());
    }

    [Fact]
    public void OtherKeysAreIgnored()
    {
        var options = new SmtpOptions { ConnectionString = "Endpoint=smtp://host:25;Something=else" };

        Assert.Equal(("host", 25), options.Endpoint());
    }

    [Fact]
    public void AMissingPortFallsBackToTwentyFive()
    {
        var options = new SmtpOptions { ConnectionString = "smtp://host" };

        Assert.Equal(("host", 25), options.Endpoint());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a uri")]
    [InlineData("Endpoint=")]
    [InlineData("Endpoint=not a uri")]
    public void AnUnreadableValueIsRefused(string connectionString)
    {
        // And refused at startup, not at send time: Validate() is wired to ValidateOnStart so the Worker does not
        // come up able to accept email it cannot deliver.
        var options = new SmtpOptions { ConnectionString = connectionString };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
