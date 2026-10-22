using Arts6311.Domain.Managers.Csv;
using Arts6311.Domain.Modules.Forms.Managers;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;
using Arts6311.Tests.Forms.Infrastructure;

namespace Arts6311.Tests.Forms;

/// <summary>The four form emails (add-notification): who they go to, what they carry, and what they never do.</summary>
public sealed class FormsEmailTests
{
    private const string Spam = "Visit cheap-pills.example now";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheArtistsConfirmationGoesToThemAndRepeatsNothingTheyTyped()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        Assert.True((await host.SubmitAsync(FormsSamples.Submission(name: Spam, email: "victim@example.org"))).Succeeded);
        var id = Assert.Single(await host.SubmissionsAsync()).Id.ToString();

        var email = await host.ComposeAsync(FormsEmailKinds.SubmissionReceived, id);

        // Anyone can type any address into a public form: a confirmation that echoed the name would send a stranger's
        // words to someone else under our name.
        Assert.Equal("victim@example.org", email!.To);
        Assert.DoesNotContain("cheap-pills", email.Subject + email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaffAreToldWhoSentItAndGivenALinkIntoTheCms()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        Assert.True((await host.SubmitAsync(FormsSamples.Submission(name: "Jane\r\nBcc: x@example.org"))).Succeeded);
        var id = Assert.Single(await host.SubmissionsAsync()).Id;

        var email = await host.ComposeAsync(FormsEmailKinds.SubmissionStaff, id.ToString());

        Assert.Equal(FormsTestHost.StaffAddress, email!.To);
        Assert.Contains($"{FormsTestHost.SiteOrigin}/admin/forms/submissions/{id}", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Here for: sell, teach", email.TextBody, StringComparison.Ordinal);

        // A line break in a name can't start a new header line in the subject.
        Assert.DoesNotContain('\n', email.Subject);
    }

    [Fact]
    public async Task AContactMessageReachesStaffWhileTheSenderGetsFixedWords()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        Assert.True((await host.WithFacadeAsync(facade =>
            facade.ContactAsync(new ContactMessageViewModel("Sam", "sam@example.org", Spam), Ct))).Succeeded);
        var id = Assert.Single(await host.ContactsAsync()).Id.ToString();

        var toSender = await host.ComposeAsync(FormsEmailKinds.ContactReceived, id);
        var toStaff = await host.ComposeAsync(FormsEmailKinds.ContactStaff, id);

        Assert.Equal("sam@example.org", toSender!.To);
        Assert.DoesNotContain("cheap-pills", toSender.TextBody, StringComparison.Ordinal);
        Assert.Equal(FormsTestHost.StaffAddress, toStaff!.To);
        Assert.Contains(Spam, toStaff.TextBody, StringComparison.Ordinal);
        Assert.Contains("sam@example.org", toStaff.TextBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FormsEmailKinds.SubmissionReceived)]
    [InlineData(FormsEmailKinds.SubmissionStaff)]
    [InlineData(FormsEmailKinds.ContactReceived)]
    [InlineData(FormsEmailKinds.ContactStaff)]
    public async Task ARecordThatIsGoneHasNothingLeftToSend(string kind)
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);

        Assert.Null(await host.ComposeAsync(kind, Guid.NewGuid().ToString()));
        Assert.Null(await host.ComposeAsync(kind, "not-an-id"));
    }

    [Fact]
    public async Task StaffNotesNeverReachAnEmail()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        Assert.True((await host.SubmitAsync(FormsSamples.Submission())).Succeeded);
        var id = Assert.Single(await host.SubmissionsAsync()).Id;
        var read = await host.WithFacadeAsync(facade => facade.GetSubmissionAsync(id, Ct));
        await host.WithFacadeAsync(facade => facade.UpdateSubmissionAsync(
            id, new SubmissionUpdateViewModel(Notes: "PRIVATE-DETAIL"), read.Value!.ETag, FormsTestHost.EditorId, Ct));

        foreach (var kind in new[] { FormsEmailKinds.SubmissionReceived, FormsEmailKinds.SubmissionStaff })
        {
            var email = await host.ComposeAsync(kind, id.ToString());
            Assert.DoesNotContain("PRIVATE-DETAIL", email!.TextBody, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheStaffAddressIsRequiredWhereEmailIsSent()
    {
        Assert.Throws<InvalidOperationException>(() => new FormsOptions().Validate());
        Assert.Throws<InvalidOperationException>(() => new FormsOptions { StaffAddress = "staff" }.Validate());
        new FormsOptions { StaffAddress = "staff@6311arts.localhost" }.Validate();

        // In production the placeholder would lose every staff notification without a sound.
        Assert.Throws<InvalidOperationException>(() => new FormsOptions { StaffAddress = "staff@6311arts.localhost" }.Validate(production: true));
        new FormsOptions { StaffAddress = "info@example.org" }.Validate(production: true);
    }

    [Fact]
    public void CsvFieldsAreQuotedAndFormulasDefused()
    {
        Assert.Equal("\"a\"\"b\"", CsvFormat.Field("a\"b"));
        foreach (var start in new[] { "=", "+", "-", "@", "\t", "\r" })
        {
            Assert.Equal($"\"'{start}1+1\"", CsvFormat.Field($"{start}1+1"));
        }

        Assert.Equal("\"x\",\"y\"\r\n\"1\",\"2\"\r\n", CsvFormat.Write(["x", "y"], [["1", "2"]]));
    }
}
