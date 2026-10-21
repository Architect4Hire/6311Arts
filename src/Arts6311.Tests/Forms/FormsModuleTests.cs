using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Forms.Managers;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Tests.Forms.Infrastructure;
using Arts6311.Tests.Media.Infrastructure;

namespace Arts6311.Tests.Forms;

/// <summary>The Forms module end to end on SQLite, Media included (ADR 0047).</summary>
public sealed class FormsModuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASubmissionKeepsItsAnswersAndPicturesAndStagesBothEmails()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);

        var result = await host.SubmitAsync(
            FormsSamples.Submission(wants: ["teach", "sell"]),
            ("lake.jpg", ImageFixtures.Jpeg(1200, 800)),
            ("print.png", ImageFixtures.Png(900, 900)));

        Assert.True(result.Succeeded);
        var stored = Assert.Single(await host.SubmissionsAsync());
        Assert.Equal(("Jane Rivera", "jane@example.org", SubmissionStatus.New), (stored.Name, stored.Email, stored.Status));

        // Goals in the form's order, and only the panels for goals the artist picked: the gallery panel was filled
        // in but "exhibit" wasn't chosen, so it is not an answer.
        Assert.Equal("sell,teach", stored.Wants);
        var answers = SubmissionAnswers.Parse(stored.AnswersJson);
        Assert.NotNull(answers.Sell);
        Assert.NotNull(answers.Teach);
        Assert.Null(answers.Exhibit);
        Assert.Equal("janerivera", answers.Instagram);

        // Both pictures are hidden submission drafts, in the order they were sent.
        var originals = await host.OriginalsAsync();
        Assert.Equal(2, originals.Count);
        Assert.All(originals, asset => Assert.Equal((MediaAssetKind.Submission, MediaVisibility.Draft), (asset.Kind, asset.Visibility)));
        Assert.All(originals, asset => Assert.Equal(FormsPolicy.AnonymousActor, asset.UploadedBy));
        Assert.Equal(
            originals.OrderBy(asset => asset.FileName == "print.png").Select(asset => asset.Id),
            stored.Images.OrderBy(image => image.Position).Select(image => image.MediaAssetId));

        // The emails name only the record (outbox.md): the composers look up who to write to.
        Assert.Equal(
            [(FormsEmailKinds.SubmissionReceived, stored.Id.ToString()), (FormsEmailKinds.SubmissionStaff, stored.Id.ToString())],
            (await host.EmailsAsync()).Order());
    }

    [Fact]
    public async Task AHoneypotHitSucceedsAndKeepsNothing()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);

        var submitted = await host.SubmitAsync(FormsSamples.Submission(company: "Acme"), ("lake.jpg", ImageFixtures.Jpeg(800, 600)));
        var contacted = await host.WithFacadeAsync(facade => facade.ContactAsync(new ContactMessageViewModel("Bot", "bot@example.org", "Buy now", "Acme"), Ct));
        var subscribed = await host.WithFacadeAsync(facade => facade.SubscribeAsync(new NewsletterSignupViewModel("bot@example.org", "Acme"), Ct));

        // The same answer a person gets, and nothing read, stored or sent — not even the picture.
        Assert.True(submitted.Succeeded && contacted.Succeeded && subscribed.Succeeded);
        Assert.Empty(await host.SubmissionsAsync());
        Assert.Empty(await host.ContactsAsync());
        Assert.Empty(await host.SubscribersAsync());
        Assert.Empty(await host.OriginalsAsync());
        Assert.Empty(await host.OutboxAsync());
    }

    [Fact]
    public async Task SixPicturesAreTakenAndASeventhRefusesTheWholeSubmissionLeavingNoPicturesBehind()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        var six = Enumerable.Range(1, 6).Select(i => ($"p{i}.jpg", ImageFixtures.Jpeg(400 + i, 300))).ToArray();

        Assert.True((await host.SubmitAsync(FormsSamples.Submission(), six)).Succeeded);
        Assert.Equal(6, Assert.Single(await host.SubmissionsAsync()).Images.Count);

        await using var second = await FormsTestHost.CreateAsync(Ct);
        var seven = Enumerable.Range(1, 7).Select(i => ($"p{i}.jpg", ImageFixtures.Jpeg(400 + i, 300))).ToArray();
        var refused = await second.SubmitAsync(FormsSamples.Submission(), seven);

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Contains("images", refused.Error.FieldErrors.Keys);
        Assert.Empty(await second.SubmissionsAsync());
        Assert.Empty(await second.OriginalsAsync());
        Assert.Empty(await second.EmailsAsync());
    }

    [Theory]
    [InlineData("forged.jpg", MediaErrorCodes.ExtensionMismatch)]
    [InlineData("drawing.svg", MediaErrorCodes.TypeUnsupported)]
    public async Task AForgedOrUnsupportedPictureRefusesTheSubmissionAndUndoesTheOnesBefore(string fileName, string code)
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        var bad = fileName.EndsWith(".svg", StringComparison.Ordinal) ? ImageFixtures.Svg() : ImageFixtures.Png(300, 300);

        var refused = await host.SubmitAsync(FormsSamples.Submission(), ("fine.jpg", ImageFixtures.Jpeg(500, 400)), (fileName, bad));

        Assert.Equal(code, refused.Error!.Code);
        Assert.Empty(await host.SubmissionsAsync());

        // The good picture that came first was stored, then removed when the bad one refused the submission.
        Assert.Empty(await host.OriginalsAsync());
    }

    [Fact]
    public async Task APictureLargerThanTheSubmissionCapIsRefused()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        var big = new byte[MediaPolicy.MaxSubmissionImageBytes + 1];
        ImageFixtures.Jpeg(400, 300).CopyTo(big, 0);

        var refused = await host.SubmitAsync(FormsSamples.Submission(), ("huge.jpg", big));

        Assert.Equal(MediaErrorCodes.TooLarge, refused.Error!.Code);
        Assert.Contains("10 MB", refused.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePictureSentTwiceIsLinkedOnceAndAStaffUploadOfItIsNotRefused()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        var picture = ImageFixtures.Jpeg(640, 480);

        Assert.True((await host.SubmitAsync(FormsSamples.Submission(), ("a.jpg", picture), ("again.jpg", picture))).Succeeded);
        Assert.Single(Assert.Single(await host.SubmissionsAsync()).Images);
        Assert.Single(await host.OriginalsAsync());

        // A submitted picture is hidden from the library, so it must not be why staff can't add the same one.
        var upload = await UploadAsStaffAsync(host, picture);
        Assert.True(upload.Succeeded);
        Assert.Equal(2, (await host.OriginalsAsync()).Count);

        // And the other way round: a visitor sending a library picture's bytes gets the submitted copy linked, never
        // the library asset — which their submission would otherwise pin against deletion (security review).
        Assert.True((await host.SubmitAsync(FormsSamples.Submission(name: "Bo"), ("same.jpg", picture))).Succeeded);
        var linked = (await host.SubmissionsAsync()).Single(submission => submission.Name == "Bo").Images.Single().MediaAssetId;
        Assert.NotEqual(upload.Value!.Id, linked);
        Assert.Equal(MediaAssetKind.Submission, (await host.OriginalsAsync()).Single(asset => asset.Id == linked).Kind);
    }

    [Fact]
    public async Task AnAddressGetsOneConfirmationAnHourWhileStaffHearAboutEveryMessage()
    {
        // Anyone can type anyone's address: the cooldown is what stops many senders flooding one stranger's inbox.
        await using var host = await FormsTestHost.CreateAsync(Ct);
        Task<OperationResult> Send() => host.WithFacadeAsync(facade => facade.ContactAsync(
            new ContactMessageViewModel("Sam", "Victim@Example.org", "Hi"), Ct));

        await Send();
        host.Time.Advance(TimeSpan.FromMinutes(30));
        await Send();
        var withinTheHour = await host.EmailsAsync();
        // An hour after the address last wrote, not after its last confirmation: a steady trickle stays at one.
        host.Time.Advance(TimeSpan.FromMinutes(61));
        await Send();
        var after = await host.EmailsAsync();

        Assert.Equal(1, withinTheHour.Count(email => email.Kind == FormsEmailKinds.ContactReceived));
        Assert.Equal(2, withinTheHour.Count(email => email.Kind == FormsEmailKinds.ContactStaff));
        Assert.Equal(2, after.Count(email => email.Kind == FormsEmailKinds.ContactReceived));
        Assert.Equal(3, after.Count(email => email.Kind == FormsEmailKinds.ContactStaff));
    }

    [Fact]
    public async Task ASecondSubmissionWithinTheHourIsStoredAndToldToStaffButNotConfirmedAgain()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);

        Assert.True((await host.SubmitAsync(FormsSamples.Submission())).Succeeded);
        Assert.True((await host.SubmitAsync(FormsSamples.Submission(email: "JANE@example.org"))).Succeeded);

        Assert.Equal(2, (await host.SubmissionsAsync()).Count);
        var emails = await host.EmailsAsync();
        Assert.Equal(1, emails.Count(email => email.Kind == FormsEmailKinds.SubmissionReceived));
        Assert.Equal(2, emails.Count(email => email.Kind == FormsEmailKinds.SubmissionStaff));
    }

    [Fact]
    public async Task TheLibraryListsNoSubmittedPictures()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        Assert.True((await host.SubmitAsync(FormsSamples.Submission(), ("a.jpg", ImageFixtures.Jpeg(640, 480)))).Succeeded);
        Assert.True((await UploadAsStaffAsync(host, ImageFixtures.Jpeg(700, 500))).Succeeded);

        var library = await host.WithMediaAsync(media => media.ListAsync(new Arts6311.Domain.Modules.Media.Managers.ViewModels.MediaListViewModel(), Ct));

        Assert.Single(library.Value!.Items);
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheSubmissionNorItsEmails()
    {
        var failure = new FailInsertInto("ArtistSubmissions");
        await using var host = await FormsTestHost.CreateAsync(Ct, failure);

        await Assert.ThrowsAnyAsync<Exception>(() => host.SubmitAsync(FormsSamples.Submission(), ("a.jpg", ImageFixtures.Jpeg(640, 480))));

        Assert.True(failure.Fired);
        Assert.Empty(await host.SubmissionsAsync());
        Assert.Empty(await host.EmailsAsync());

        // And the picture it had already stored is removed again.
        Assert.Empty(await host.OriginalsAsync());
    }

    [Fact]
    public async Task TheEmailMessagesCarryIdsOnlyAndTheRequestsCorrelationId()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        Assert.True((await host.WithFacadeAsync(facade => facade.ContactAsync(
            new ContactMessageViewModel("Sam Private", "sam.private@example.org", "PRIVATE-DETAIL"), Ct))).Succeeded);
        var id = Assert.Single(await host.ContactsAsync()).Id;

        var messages = await host.OutboxAsync();

        Assert.Equal(2, messages.Count);
        Assert.All(messages, message =>
        {
            Assert.Equal("email.send-requested", message.Type);
            Assert.NotEqual(Guid.Empty, message.CorrelationId);
            Assert.DoesNotContain("Private", message.PayloadJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PRIVATE-DETAIL", message.PayloadJson, StringComparison.Ordinal);
            Assert.Contains(id.ToString(), message.PayloadJson, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AFailedContactSaveLeavesNeitherTheMessageNorItsEmails()
    {
        var failure = new FailInsertInto("ContactMessages");
        await using var host = await FormsTestHost.CreateAsync(Ct, failure);

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.ContactAsync(
            new ContactMessageViewModel("Sam", "sam@example.org", "Hi"), Ct)));

        Assert.True(failure.Fired);
        Assert.Empty(await host.ContactsAsync());
        Assert.Empty(await host.OutboxAsync());
    }

    [Fact]
    public async Task ASubmissionsNewsletterSignUpCommitsWithItOrNotAtAll()
    {
        // In one save with the submission: a failure after the submission had committed would tell the artist it
        // failed, and their retry would be a second submission (outbox review).
        var failure = new FailInsertInto("ArtistSubmissions");
        await using var host = await FormsTestHost.CreateAsync(Ct, failure);

        await Assert.ThrowsAnyAsync<Exception>(() => host.SubmitAsync(FormsSamples.Submission(newsletter: true)));

        Assert.Empty(await host.SubmissionsAsync());
        Assert.Empty(await host.SubscribersAsync());
    }

    [Fact]
    public async Task AContactMessageIsKeptWithBothEmailsStaged()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);

        var result = await host.WithFacadeAsync(facade => facade.ContactAsync(
            new ContactMessageViewModel("  Sam  ", "sam@example.org", "Are you open Monday?"), Ct));

        Assert.True(result.Succeeded);
        var message = Assert.Single(await host.ContactsAsync());
        Assert.Equal(("Sam", "Are you open Monday?", false), (message.Name, message.Message, message.Handled));
        Assert.Equal(
            [(FormsEmailKinds.ContactReceived, message.Id.ToString()), (FormsEmailKinds.ContactStaff, message.Id.ToString())],
            (await host.EmailsAsync()).Order());
    }

    [Fact]
    public async Task SigningUpTwiceIsOneRowWithTheLaterConsentAndTheSameAnswer()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);

        var first = await host.WithFacadeAsync(facade => facade.SubscribeAsync(new NewsletterSignupViewModel("Ana@Example.org "), Ct));
        host.Time.Advance(TimeSpan.FromDays(3));
        var second = await host.WithFacadeAsync(facade => facade.SubscribeAsync(new NewsletterSignupViewModel("ana@example.org"), Ct));

        Assert.True(first.Succeeded && second.Succeeded);
        var subscriber = Assert.Single(await host.SubscribersAsync());
        Assert.Equal("ana@example.org", subscriber.Email);
        Assert.Equal(host.Time.GetUtcNow(), subscriber.ConsentedAt);
        Assert.Equal(FormsPolicy.NewsletterConsentVersion, subscriber.ConsentVersion);
        Assert.Equal(NewsletterSource.Signup, subscriber.Source);

        // No email: the list is consent only until the newsletter vendor brings double opt-in (ADR 0047).
        Assert.Empty(await host.EmailsAsync());
    }

    [Fact]
    public async Task TickingTheNewsletterBoxOnASubmissionJoinsTheList()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);

        Assert.True((await host.SubmitAsync(FormsSamples.Submission(email: "Jane@Example.org", newsletter: true))).Succeeded);

        var subscriber = Assert.Single(await host.SubscribersAsync());
        Assert.Equal(("jane@example.org", NewsletterSource.Submission), (subscriber.Email, subscriber.Source));
    }

    [Fact]
    public async Task TheInboxPagesNewestFirstAndFiltersByStatus()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        foreach (var name in new[] { "Ann", "Bo", "Cy" })
        {
            Assert.True((await host.SubmitAsync(FormsSamples.Submission(name: name))).Succeeded);
            host.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var first = await host.WithFacadeAsync(facade => facade.ListSubmissionsAsync(new SubmissionListViewModel(Limit: 2), Ct));
        var second = await host.WithFacadeAsync(facade => facade.ListSubmissionsAsync(new SubmissionListViewModel(first.Value!.NextCursor, 2), Ct));

        Assert.Equal(["Cy", "Bo"], first.Value!.Items.Select(item => item.Name));
        Assert.Equal(["Ann"], second.Value!.Items.Select(item => item.Name));
        Assert.Null(second.Value.NextCursor);

        var refused = await host.WithFacadeAsync(facade => facade.ListSubmissionsAsync(
            new SubmissionListViewModel(first.Value.NextCursor, 2, SubmissionStatus.Accepted), Ct));
        Assert.Equal(ErrorCodes.CursorInvalid, refused.Error!.Code);
    }

    [Fact]
    public async Task StaffMoveASubmissionUnderItsETagAndTheAuditSaysWhatChangedButNotTheNotes()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        Assert.True((await host.SubmitAsync(FormsSamples.Submission())).Succeeded);
        var id = Assert.Single(await host.SubmissionsAsync()).Id;
        var read = await host.WithFacadeAsync(facade => facade.GetSubmissionAsync(id, Ct));

        var updated = await host.WithFacadeAsync(facade => facade.UpdateSubmissionAsync(
            id, new SubmissionUpdateViewModel(SubmissionStatus.Accepted, "Lovely prints; ask about the spring show."),
            read.Value!.ETag, FormsTestHost.EditorId, Ct));
        var stale = await host.WithFacadeAsync(facade => facade.UpdateSubmissionAsync(
            id, new SubmissionUpdateViewModel(SubmissionStatus.Declined), read.Value!.ETag, FormsTestHost.EditorId, Ct));
        var missing = await host.WithFacadeAsync(facade => facade.UpdateSubmissionAsync(
            id, new SubmissionUpdateViewModel(SubmissionStatus.Declined), null, FormsTestHost.EditorId, Ct));

        Assert.Equal((SubmissionStatus.Accepted, FormsTestHost.EditorId), (updated.Value!.Status, updated.Value.UpdatedBy));
        Assert.NotEqual(read.Value!.ETag, updated.Value.ETag);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, missing.Error!.Code);

        var entry = Assert.Single(await host.AuditAsync(), entry => entry.Action == "artist-submission.updated");
        Assert.Equal("Status New → Accepted. Notes edited.", entry.Summary);
        Assert.DoesNotContain("spring show", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASubmissionsPageShowsItsPicturesForStaff()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        Assert.True((await host.SubmitAsync(FormsSamples.Submission(), ("lake.jpg", ImageFixtures.Jpeg(1200, 800)))).Succeeded);
        var id = Assert.Single(await host.SubmissionsAsync()).Id;

        var read = await host.WithFacadeAsync(facade => facade.GetSubmissionAsync(id, Ct));

        var image = Assert.Single(read.Value!.Images);
        Assert.Equal(("lake.jpg", 1200, 800), (image.FileName, image.Width, image.Height));
        Assert.StartsWith($"/media/{image.Id}/", image.OriginalUrl, StringComparison.Ordinal);
        Assert.Equal("Intro to drypoint", read.Value.Answers.Teach!.ClassTitle);
    }

    [Fact]
    public async Task StaffMarkAMessageHandled()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        Assert.True((await host.WithFacadeAsync(facade => facade.ContactAsync(new ContactMessageViewModel("Sam", "sam@example.org", "Hi"), Ct))).Succeeded);
        var id = Assert.Single(await host.ContactsAsync()).Id;
        var read = await host.WithFacadeAsync(facade => facade.GetContactAsync(id, Ct));

        var handled = await host.WithFacadeAsync(facade => facade.UpdateContactAsync(id, new ContactUpdateViewModel(true), read.Value!.ETag, FormsTestHost.EditorId, Ct));
        var unhandled = await host.WithFacadeAsync(facade => facade.ListContactsAsync(new ContactListViewModel(Handled: false), Ct));

        Assert.True(handled.Value!.Handled);
        Assert.Empty(unhandled.Value!.Items);
        Assert.Contains(await host.AuditAsync(), entry => entry.Action == "contact-message.updated");
    }

    [Fact]
    public async Task TheExportIsSafeToOpenInASpreadsheetAndIsAudited()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        await host.WithFacadeAsync(facade => facade.SubscribeAsync(new NewsletterSignupViewModel("ana@example.org"), Ct));
        host.Time.Advance(TimeSpan.FromMinutes(1));
        await host.WithFacadeAsync(facade => facade.SubscribeAsync(new NewsletterSignupViewModel("=cmd@example.org"), Ct));

        var export = await host.WithFacadeAsync(facade => facade.ExportSubscribersAsync(FormsTestHost.AdminId, Ct));

        Assert.Equal(2, export.Count);
        Assert.Equal("6311arts-newsletter-2026-10-21.csv", export.FileName);
        var lines = export.Csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("\"email\",\"consented_at\",\"consent_version\",\"source\",\"created_at\"", lines[0]);
        Assert.StartsWith("\"ana@example.org\",", lines[1], StringComparison.Ordinal);

        // A spreadsheet would run "=cmd…" as a formula; the apostrophe makes it text.
        Assert.StartsWith("\"'=cmd@example.org\",", lines[2], StringComparison.Ordinal);

        var entry = Assert.Single(await host.AuditAsync(), entry => entry.Action == "newsletter.exported");
        Assert.Contains("2 addresses", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovingAnAddressAuditsTheRowNotTheAddress()
    {
        await using var host = await FormsTestHost.CreateAsync(Ct);
        await host.WithFacadeAsync(facade => facade.SubscribeAsync(new NewsletterSignupViewModel("ana@example.org"), Ct));
        var id = Assert.Single(await host.SubscribersAsync()).Id;

        var removed = await host.WithFacadeAsync(facade => facade.RemoveSubscriberAsync(id, FormsTestHost.AdminId, Ct));
        var again = await host.WithFacadeAsync(facade => facade.RemoveSubscriberAsync(id, FormsTestHost.AdminId, Ct));

        Assert.True(removed.Succeeded);
        Assert.Equal(ErrorCodes.NotFound, again.Error!.Code);
        Assert.Empty(await host.SubscribersAsync());
        var entry = Assert.Single(await host.AuditAsync(), entry => entry.Action == "newsletter-subscriber.removed");
        Assert.Equal(id.ToString(), entry.ResourceId);
        Assert.DoesNotContain("ana@", entry.Summary, StringComparison.Ordinal);
    }

    private static Task<OperationResult<Arts6311.Domain.Modules.Media.Managers.ServiceModels.MediaAssetServiceModel>> UploadAsStaffAsync(
        FormsTestHost host, byte[] bytes) =>
        host.WithMediaAsync(media => media.UploadAsync(
            new Arts6311.Domain.Modules.Media.Managers.ViewModels.MediaUploadViewModel("staff.jpg", new MemoryStream(bytes), null, null, null),
            FormsTestHost.EditorId,
            Ct));
}
