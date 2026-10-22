using System.Globalization;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Csv;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Forms.Data;
using Arts6311.Domain.Modules.Forms.Data.Entities;
using Arts6311.Domain.Modules.Forms.Data.Repositories;
using Arts6311.Domain.Modules.Forms.Managers;
using Arts6311.Domain.Modules.Forms.Managers.ServiceModels;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Forms.Business;

/// <summary>
/// The forms' rules: what a submission keeps (only the panels for the goals the artist picked), how addresses are
/// normalised, how the inbox pages, and what staff may change. Input arrives validated for shape (the facade).
/// </summary>
internal sealed class FormsBusiness(IFormsDataLayer dataLayer, IClock clock) : IFormsBusiness
{
    private const string SubmissionWhat = "this submission";
    private const string ContactWhat = "this message";

    public async Task<Guid> ReceiveSubmissionAsync(
        ArtistSubmissionViewModel submission, IReadOnlyList<Guid> imageIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(imageIds);

        var artist = submission.Artist!;
        var work = submission.Work ?? new SubmissionWorkViewModel();
        var wants = FormsPolicy.Goals.Where(goal => submission.Wants!.Contains(goal, StringComparer.Ordinal)).ToList();
        var newsletter = submission.Consent?.Newsletter ?? false;

        // A panel is kept only for a goal the artist picked: the form hides the others, and a value left in a hidden
        // panel is not an answer.
        var answers = new SubmissionAnswers(
            Clean(artist.Pronouns),
            Clean(artist.Phone),
            Clean(artist.Neighborhood),
            Clean(artist.Zip),
            Clean(artist.Website),
            InstagramHandle(artist.Instagram),
            Clean(work.Medium),
            Clean(work.OtherMedia),
            work.Years,
            Clean(work.PriceBand),
            Clean(work.Bio),
            Clean(work.Statement),
            wants.Contains("sell") && submission.Sell is { } sell
                ? new SubmissionSellServiceModel(sell.PiecesReady, Clean(sell.Restock))
                : null,
            wants.Contains("exhibit") && submission.Exhibit is { } exhibit
                ? new SubmissionExhibitServiceModel(
                    Clean(exhibit.ShowTitle), exhibit.WorkCount, Clean(exhibit.Largest), Clean(exhibit.Window), Clean(exhibit.Gallery))
                : null,
            wants.Contains("teach") && submission.Teach is { } teach
                ? new SubmissionTeachServiceModel(
                    Clean(teach.ClassTitle), teach.MaxStudents, Clean(teach.Format), Clean(teach.Materials), teach.TaughtBefore)
                : null,
            Clean(submission.Portfolio),
            Clean(submission.Heard),
            newsletter);

        var now = clock.UtcNow;
        var entity = new ArtistSubmission
        {
            Id = Guid.CreateVersion7(now),
            Name = artist.Name!.Trim(),
            Email = artist.Email!.Trim(),
            Wants = string.Join(',', wants),
            AnswersJson = answers.Serialize(),
            Status = SubmissionStatus.New,
            ReceivedAt = now,
            UpdatedAt = now,
            RowVersion = Guid.NewGuid(),
            Images = [.. imageIds.Distinct().Select((id, index) => new ArtistSubmissionImage { MediaAssetId = id, Position = index })],
        };

        // The sign-up goes in the submission's own save: a failure after the submission had committed would tell the
        // artist it failed, and their retry would be a second submission.
        // One confirmation an hour per address (FormsPolicy.ConfirmationCooldown); staff hear about every submission.
        var confirm = !await dataLayer.HasSubmissionFromAsync(
            NormalizeEmail(artist.Email), now - FormsPolicy.ConfirmationCooldown, cancellationToken);
        await dataLayer.ReceiveSubmissionAsync(entity, newsletter ? NormalizeEmail(artist.Email) : null, confirm, cancellationToken);
        return entity.Id;
    }

    public async Task<Guid> ReceiveContactAsync(ContactMessageViewModel message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var now = clock.UtcNow;
        var entity = new ContactMessage
        {
            Id = Guid.CreateVersion7(now),
            Name = message.Name!.Trim(),
            Email = message.Email!.Trim(),
            Message = message.Message!.Trim(),
            ReceivedAt = now,
            UpdatedAt = now,
            RowVersion = Guid.NewGuid(),
        };

        // One confirmation an hour per address (FormsPolicy.ConfirmationCooldown); staff hear about every message.
        var confirm = !await dataLayer.HasContactFromAsync(
            NormalizeEmail(message.Email), now - FormsPolicy.ConfirmationCooldown, cancellationToken);
        await dataLayer.ReceiveContactAsync(entity, confirm, cancellationToken);
        return entity.Id;
    }

    public Task SubscribeAsync(NewsletterSignupViewModel signup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signup);
        return dataLayer.SubscribeAsync(NormalizeEmail(signup.Email), NewsletterSource.Signup, cancellationToken);
    }

    public async Task<OperationResult<CursorPage<SubmissionSummaryServiceModel>>> ListSubmissionsAsync(
        SubmissionListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = string.Create(CultureInfo.InvariantCulture, $"forms-submissions|status={request.Status}");
        if (!TryResume(request.Cursor, scope, out var after))
        {
            return OperationResult.Failure<CursorPage<SubmissionSummaryServiceModel>>(InvalidCursor());
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListSubmissionsAsync(after, limit + 1, request.Status, cancellationToken);
        var next = NextCursor(rows, limit, scope, row => (row.ReceivedAt, row.Id));

        return OperationResult.Success(new CursorPage<SubmissionSummaryServiceModel>(
            [.. rows.Select(row => new SubmissionSummaryServiceModel(
                row.Id, row.Name, row.Email, WantsOf(row), row.Status, row.Images.Count, row.ReceivedAt))],
            next));
    }

    public async Task<OperationResult<SubmissionRow>> GetSubmissionAsync(Guid id, CancellationToken cancellationToken)
    {
        var submission = await dataLayer.FindSubmissionAsync(id, cancellationToken);
        return submission is null ? SubmissionNotFound() : OperationResult.Success(ToRow(submission));
    }

    public async Task<OperationResult<SubmissionRow>> UpdateSubmissionAsync(
        Guid id, SubmissionUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var submission = await dataLayer.FindSubmissionAsync(id, cancellationToken);
        if (submission is null)
        {
            return SubmissionNotFound();
        }

        if (EntityTag.Verify(submission.RowVersion, ifMatch, SubmissionWhat) is { } versionError)
        {
            return OperationResult.Failure<SubmissionRow>(versionError);
        }

        var status = update.Status ?? submission.Status;
        var notes = update.Notes is null ? submission.Notes : Clean(update.Notes);
        if (status == submission.Status && notes == submission.Notes)
        {
            return OperationResult.Success(ToRow(submission));
        }

        // What changed, never what the notes say: they are staff's words about a person, and the audit log is not
        // where those belong.
        var summary = status != submission.Status
            ? string.Create(CultureInfo.InvariantCulture, $"Status {submission.Status} → {status}.{(notes != submission.Notes ? " Notes edited." : string.Empty)}")
            : "Notes edited.";
        submission.Status = status;
        submission.Notes = notes;

        return await dataLayer.UpdateSubmissionAsync(submission, actorUserId, summary, cancellationToken) switch
        {
            FormsWriteOutcome.Saved => OperationResult.Success(ToRow(submission)),
            _ => OperationResult.Failure<SubmissionRow>(EntityTag.Conflict(SubmissionWhat)),
        };
    }

    public async Task<OperationResult<CursorPage<ContactMessageServiceModel>>> ListContactsAsync(
        ContactListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = string.Create(CultureInfo.InvariantCulture, $"forms-contacts|handled={request.Handled}");
        if (!TryResume(request.Cursor, scope, out var after))
        {
            return OperationResult.Failure<CursorPage<ContactMessageServiceModel>>(InvalidCursor());
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListContactsAsync(after, limit + 1, request.Handled, cancellationToken);
        var next = NextCursor(rows, limit, scope, row => (row.ReceivedAt, row.Id));

        return OperationResult.Success(new CursorPage<ContactMessageServiceModel>([.. rows.Select(ToServiceModel)], next));
    }

    public async Task<OperationResult<ContactMessageServiceModel>> GetContactAsync(Guid id, CancellationToken cancellationToken)
    {
        var message = await dataLayer.FindContactAsync(id, cancellationToken);
        return message is null ? ContactNotFound() : OperationResult.Success(ToServiceModel(message));
    }

    public async Task<OperationResult<ContactMessageServiceModel>> UpdateContactAsync(
        Guid id, ContactUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var message = await dataLayer.FindContactAsync(id, cancellationToken);
        if (message is null)
        {
            return ContactNotFound();
        }

        if (EntityTag.Verify(message.RowVersion, ifMatch, ContactWhat) is { } versionError)
        {
            return OperationResult.Failure<ContactMessageServiceModel>(versionError);
        }

        if (update.Handled == message.Handled)
        {
            return OperationResult.Success(ToServiceModel(message));
        }

        message.Handled = update.Handled!.Value;
        return await dataLayer.UpdateContactAsync(message, actorUserId, cancellationToken) switch
        {
            FormsWriteOutcome.Saved => OperationResult.Success(ToServiceModel(message)),
            _ => OperationResult.Failure<ContactMessageServiceModel>(EntityTag.Conflict(ContactWhat)),
        };
    }

    public async Task<OperationResult<CursorPage<NewsletterSubscriberServiceModel>>> ListSubscribersAsync(
        NewsletterListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim().ToLowerInvariant();
        var scope = string.Create(CultureInfo.InvariantCulture, $"forms-newsletter|search={search}");
        if (!TryResume(request.Cursor, scope, out var after))
        {
            return OperationResult.Failure<CursorPage<NewsletterSubscriberServiceModel>>(InvalidCursor());
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListSubscribersAsync(after, limit + 1, search, cancellationToken);
        var next = NextCursor(rows, limit, scope, row => (row.CreatedAt, row.Id));

        return OperationResult.Success(new CursorPage<NewsletterSubscriberServiceModel>([.. rows.Select(ToServiceModel)], next));
    }

    public async Task<OperationResult> RemoveSubscriberAsync(Guid id, string actorUserId, CancellationToken cancellationToken)
    {
        var subscriber = await dataLayer.FindSubscriberAsync(id, cancellationToken);
        if (subscriber is null)
        {
            return OperationResult.Failure(OperationError.Of(ErrorCodes.NotFound, "There is no such address on the list."));
        }

        await dataLayer.RemoveSubscriberAsync(subscriber, actorUserId, cancellationToken);
        return OperationResult.Success();
    }

    public async Task<NewsletterExportServiceModel> ExportSubscribersAsync(string actorUserId, CancellationToken cancellationToken)
    {
        var subscribers = await dataLayer.ExportSubscribersAsync(actorUserId, cancellationToken);
        var csv = CsvFormat.Write(
            ["email", "consented_at", "consent_version", "source", "created_at"],
            subscribers.Select(subscriber => (IReadOnlyList<string>)
            [
                subscriber.Email,
                subscriber.ConsentedAt.ToString("O", CultureInfo.InvariantCulture),
                subscriber.ConsentVersion,
                subscriber.Source.ToString(),
                subscriber.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            ]));

        var fileName = string.Create(CultureInfo.InvariantCulture, $"6311arts-newsletter-{clock.UtcNow:yyyy-MM-dd}.csv");
        return new NewsletterExportServiceModel(fileName, csv, subscribers.Count);
    }

    /// <summary>An address as the list keeps it: trimmed and lower-cased, so one person is one row.</summary>
    internal static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? InstagramHandle(string? value) => Clean(value)?.TrimStart('@');

    private static string[] WantsOf(ArtistSubmission submission) =>
        submission.Wants.Length == 0 ? [] : submission.Wants.Split(',');

    private static SubmissionRow ToRow(ArtistSubmission submission) => new(
        submission.Id,
        submission.Name,
        submission.Email,
        WantsOf(submission),
        SubmissionAnswers.Parse(submission.AnswersJson).ToServiceModel(),
        [.. submission.Images.OrderBy(image => image.Position).Select(image => image.MediaAssetId)],
        submission.Status,
        submission.Notes,
        submission.ReceivedAt,
        submission.UpdatedAt,
        submission.UpdatedBy,
        EntityTag.Format(submission.RowVersion));

    private static ContactMessageServiceModel ToServiceModel(ContactMessage message) => new(
        message.Id, message.Name, message.Email, message.Message, message.Handled, message.ReceivedAt, message.UpdatedAt,
        message.UpdatedBy, EntityTag.Format(message.RowVersion));

    private static NewsletterSubscriberServiceModel ToServiceModel(NewsletterSubscriber subscriber) => new(
        subscriber.Id, subscriber.Email, subscriber.ConsentedAt, subscriber.ConsentVersion, subscriber.Source, subscriber.CreatedAt);

    /// <summary>Where a newest-first list resumes, from its cursor; <c>false</c> for one that isn't this list's.</summary>
    private static bool TryResume(string? cursor, string scope, out FormsListPosition? after)
    {
        after = null;
        if (cursor is null)
        {
            return true;
        }

        var decoded = PageCursor.Decode(cursor, scope);
        if (!decoded.Succeeded
            || !DateTimeOffset.TryParseExact(decoded.Value!.SortValue, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            || !Guid.TryParseExact(decoded.Value.TieBreaker, "N", out var id))
        {
            return false;
        }

        after = new FormsListPosition(at, id);
        return true;
    }

    /// <summary>Trims the look-ahead row and returns the cursor after the last kept one, or <c>null</c> at the end.</summary>
    private static string? NextCursor<T>(List<T> rows, int limit, string scope, Func<T, (DateTimeOffset At, Guid Id)> key)
    {
        if (rows.Count <= limit)
        {
            return null;
        }

        rows.RemoveAt(limit);
        var (at, id) = key(rows[^1]);
        return new PageCursor(at.ToString("O", CultureInfo.InvariantCulture), id.ToString("N")).Encode(scope);
    }

    private static OperationError InvalidCursor() =>
        OperationError.Of(ErrorCodes.CursorInvalid, "The cursor is not valid for this list.");

    private static OperationResult<SubmissionRow> SubmissionNotFound() =>
        OperationResult.Failure<SubmissionRow>(OperationError.Of(ErrorCodes.NotFound, "There is no such submission."));

    private static OperationResult<ContactMessageServiceModel> ContactNotFound() =>
        OperationResult.Failure<ContactMessageServiceModel>(OperationError.Of(ErrorCodes.NotFound, "There is no such message."));
}
