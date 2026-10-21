using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Forms.Managers;
using Arts6311.Domain.Modules.Forms.Managers.Validation;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;
using Arts6311.Tests.Forms.Infrastructure;

namespace Arts6311.Tests.Forms;

/// <summary>The public forms' shape checks: what the site's form offers is accepted, and nothing else (ADR 0047).</summary>
public sealed class FormsValidatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheFormsOwnAnswersPass()
    {
        Assert.Null(await new ArtistSubmissionValidator().ValidateOrErrorAsync(FormsSamples.Submission(), Ct));

        // Only the required fields: a name, an address, a goal and the own-work box.
        var minimal = new ArtistSubmissionViewModel(
            new SubmissionArtistViewModel("Jo", "jo@example.org"), null, ["hello"], Consent: new SubmissionConsentViewModel(true));
        Assert.Null(await new ArtistSubmissionValidator().ValidateOrErrorAsync(minimal, Ct));
    }

    public static TheoryData<string, ArtistSubmissionViewModel> Refusals()
    {
        var ok = FormsSamples.Submission();
        return new TheoryData<string, ArtistSubmissionViewModel>
        {
            { "artist", ok with { Artist = null } },
            { "artist.name", ok with { Artist = ok.Artist! with { Name = "  " } } },
            { "artist.email", ok with { Artist = ok.Artist! with { Email = "jane at example" } } },
            { "artist.email", ok with { Artist = ok.Artist! with { Email = "a<b@example.org" } } },
            { "artist.email", ok with { Artist = ok.Artist! with { Email = "a,b@example.org" } } },
            { "artist.email", ok with { Artist = ok.Artist! with { Email = "\"a\"@example.org" } } },
            { "artist.zip", ok with { Artist = ok.Artist! with { Zip = "6062" } } },
            { "artist.website", ok with { Artist = ok.Artist! with { Website = "javascript:alert(1)" } } },
            { "artist.instagram", ok with { Artist = ok.Artist! with { Instagram = "not a handle" } } },
            { "wants", ok with { Wants = [] } },
            { "wants", ok with { Wants = ["sell", "buy"] } },
            { "work.medium", ok with { Work = ok.Work! with { Medium = "Glitter" } } },
            { "work.years", ok with { Work = ok.Work! with { Years = 81 } } },
            { "work.bio", ok with { Work = ok.Work! with { Bio = new string('b', FormsPolicy.BioMaxLength + 1) } } },
            { "sell.restock", ok with { Sell = new SubmissionSellViewModel(Restock: "always") } },
            { "exhibit.gallery", ok with { Exhibit = new SubmissionExhibitViewModel(Gallery: "North gallery") } },
            { "teach.maxStudents", ok with { Teach = new SubmissionTeachViewModel(MaxStudents: 25) } },
            { "teach.format", ok with { Teach = new SubmissionTeachViewModel(Format: "Weekend retreat") } },
            { "portfolio", ok with { Portfolio = "ftp://files.example.org" } },
            { "heard", ok with { Heard = "TV" } },
            { "consent.ownWork", ok with { Consent = new SubmissionConsentViewModel(false) } },
        };
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task AnAnswerTheFormDoesNotOfferIsRefusedOnItsField(string field, ArtistSubmissionViewModel submission)
    {
        var error = await new ArtistSubmissionValidator().ValidateOrErrorAsync(submission, Ct);

        Assert.NotNull(error);
        Assert.Contains(field, error.FieldErrors.Keys);
    }

    [Theory]
    [InlineData(null, "sam@example.org", "Hi", "name")]
    [InlineData("Sam", "sam@", "Hi", "email")]
    [InlineData("Sam", "sam@example.org", " ", "message")]
    public async Task AContactMessageNeedsANameAnAddressAndWords(string? name, string email, string message, string field)
    {
        var error = await new ContactMessageValidator().ValidateOrErrorAsync(new ContactMessageViewModel(name, email, message), Ct);

        Assert.Contains(field, error!.FieldErrors.Keys);
    }

    [Fact]
    public async Task ANewsletterSignUpNeedsAnAddress()
    {
        Assert.Null(await new NewsletterSignupValidator().ValidateOrErrorAsync(new NewsletterSignupViewModel("ana@example.org"), Ct));
        Assert.Contains("email", (await new NewsletterSignupValidator().ValidateOrErrorAsync(new NewsletterSignupViewModel(""), Ct))!.FieldErrors.Keys);
    }

    [Fact]
    public async Task AStaffChangeNeedsSomethingToChange()
    {
        Assert.NotNull(await new SubmissionUpdateValidator().ValidateOrErrorAsync(new SubmissionUpdateViewModel(), Ct));
        Assert.Null(await new SubmissionUpdateValidator().ValidateOrErrorAsync(new SubmissionUpdateViewModel(Notes: ""), Ct));
        Assert.NotNull(await new ContactUpdateValidator().ValidateOrErrorAsync(new ContactUpdateViewModel(null), Ct));
    }
}
