using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Forms.Business;
using Arts6311.Domain.Modules.Forms.Managers;
using Arts6311.Domain.Modules.Forms.Managers.ServiceModels;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Forms.Facade;

/// <summary>
/// Validates shape, keeps bots' honeypot hits out, and puts submitted pictures into Media (facade to facade) before
/// Business stores the submission. Nothing here is cached: these are staff's working lists.
/// </summary>
internal sealed partial class FormsFacade(
    IFormsBusiness business,
    IMediaFacade media,
    ILogger<FormsFacade> logger,
    IValidator<ArtistSubmissionViewModel> submissionValidator,
    IValidator<ContactMessageViewModel> contactValidator,
    IValidator<NewsletterSignupViewModel> signupValidator,
    IValidator<SubmissionListViewModel> submissionListValidator,
    IValidator<SubmissionUpdateViewModel> submissionUpdateValidator,
    IValidator<ContactListViewModel> contactListValidator,
    IValidator<ContactUpdateViewModel> contactUpdateValidator,
    IValidator<NewsletterListViewModel> newsletterListValidator) : IFormsFacade
{
    private const string SubmissionWhat = "this submission";
    private const string ContactWhat = "this message";

    public async Task<OperationResult> SubmitAsync(
        ArtistSubmissionViewModel submission,
        IAsyncEnumerable<SubmissionImageViewModel> images,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(images);

        // A bot: the same answer a person gets, and nothing read, stored or sent.
        if (IsHoneypotHit(submission.Company))
        {
            return OperationResult.Success();
        }

        if (await submissionValidator.ValidateOrErrorAsync(submission, cancellationToken) is { } error)
        {
            return OperationResult.Failure(error);
        }

        // Pictures land in Media as they stream in. Ones this submission created are removed again if it fails, so
        // a refused or broken submission leaves no hidden pictures behind; a picture Media already held is only linked.
        var imageIds = new List<Guid>();
        var created = new List<MediaSubmissionImageServiceModel>();
        try
        {
            await foreach (var image in images.WithCancellation(cancellationToken))
            {
                if (imageIds.Count == FormsPolicy.MaxImages)
                {
                    await RemoveAsync(created, cancellationToken);
                    return OperationResult.Failure(OperationError.Validation(
                        "One or more fields are not valid.",
                        [("images", $"Send at most {FormsPolicy.MaxImages} images.")]));
                }

                var upload = await media.UploadSubmissionImageAsync(
                    image.FileName, image.Content, FormsPolicy.AnonymousActor, cancellationToken);
                if (!upload.Succeeded)
                {
                    await RemoveAsync(created, cancellationToken);
                    return OperationResult.Failure(upload.Error!);
                }

                if (upload.Value!.Created)
                {
                    created.Add(upload.Value);
                }

                imageIds.Add(upload.Value.Id);
            }

            await business.ReceiveSubmissionAsync(submission, imageIds, cancellationToken);
            return OperationResult.Success();
        }
        catch
        {
            await RemoveAsync(created, CancellationToken.None);
            throw;
        }
    }

    public async Task<OperationResult> ContactAsync(ContactMessageViewModel message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (IsHoneypotHit(message.Company))
        {
            return OperationResult.Success();
        }

        if (await contactValidator.ValidateOrErrorAsync(message, cancellationToken) is { } error)
        {
            return OperationResult.Failure(error);
        }

        await business.ReceiveContactAsync(message, cancellationToken);
        return OperationResult.Success();
    }

    public async Task<OperationResult> SubscribeAsync(NewsletterSignupViewModel signup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signup);

        if (IsHoneypotHit(signup.Company))
        {
            return OperationResult.Success();
        }

        if (await signupValidator.ValidateOrErrorAsync(signup, cancellationToken) is { } error)
        {
            return OperationResult.Failure(error);
        }

        await business.SubscribeAsync(signup, cancellationToken);
        return OperationResult.Success();
    }

    public async Task<OperationResult<CursorPage<SubmissionSummaryServiceModel>>> ListSubmissionsAsync(
        SubmissionListViewModel request, CancellationToken cancellationToken)
    {
        if (await submissionListValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<SubmissionSummaryServiceModel>>(error);
        }

        return await business.ListSubmissionsAsync(request, cancellationToken);
    }

    public async Task<OperationResult<SubmissionServiceModel>> GetSubmissionAsync(Guid id, CancellationToken cancellationToken) =>
        await WithImagesAsync(await business.GetSubmissionAsync(id, cancellationToken), cancellationToken);

    public async Task<OperationResult<SubmissionServiceModel>> UpdateSubmissionAsync(
        Guid id, SubmissionUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, SubmissionWhat) is { } preconditionError)
        {
            return OperationResult.Failure<SubmissionServiceModel>(preconditionError);
        }

        if (await submissionUpdateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<SubmissionServiceModel>(error);
        }

        return await WithImagesAsync(
            await business.UpdateSubmissionAsync(id, update, ifMatch, actorUserId, cancellationToken), cancellationToken);
    }

    public async Task<OperationResult<CursorPage<ContactMessageServiceModel>>> ListContactsAsync(
        ContactListViewModel request, CancellationToken cancellationToken)
    {
        if (await contactListValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<ContactMessageServiceModel>>(error);
        }

        return await business.ListContactsAsync(request, cancellationToken);
    }

    public Task<OperationResult<ContactMessageServiceModel>> GetContactAsync(Guid id, CancellationToken cancellationToken) =>
        business.GetContactAsync(id, cancellationToken);

    public async Task<OperationResult<ContactMessageServiceModel>> UpdateContactAsync(
        Guid id, ContactUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, ContactWhat) is { } preconditionError)
        {
            return OperationResult.Failure<ContactMessageServiceModel>(preconditionError);
        }

        if (await contactUpdateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ContactMessageServiceModel>(error);
        }

        return await business.UpdateContactAsync(id, update, ifMatch, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<CursorPage<NewsletterSubscriberServiceModel>>> ListSubscribersAsync(
        NewsletterListViewModel request, CancellationToken cancellationToken)
    {
        if (await newsletterListValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<NewsletterSubscriberServiceModel>>(error);
        }

        return await business.ListSubscribersAsync(request, cancellationToken);
    }

    public Task<OperationResult> RemoveSubscriberAsync(Guid id, string actorUserId, CancellationToken cancellationToken) =>
        business.RemoveSubscriberAsync(id, actorUserId, cancellationToken);

    public Task<NewsletterExportServiceModel> ExportSubscribersAsync(string actorUserId, CancellationToken cancellationToken) =>
        business.ExportSubscribersAsync(actorUserId, cancellationToken);

    /// <summary>A honeypot field a person never sees, so anything in it means a script filled the form.</summary>
    private static bool IsHoneypotHit(string? company) => !string.IsNullOrWhiteSpace(company);

    /// <summary>The submission with its pictures, asked of Media one by one (six at most). One Media no longer has is left out.</summary>
    private async Task<OperationResult<SubmissionServiceModel>> WithImagesAsync(
        OperationResult<SubmissionRow> row, CancellationToken cancellationToken)
    {
        if (!row.Succeeded)
        {
            return OperationResult.Failure<SubmissionServiceModel>(row.Error!);
        }

        var images = new List<SubmissionImageServiceModel>();
        foreach (var id in row.Value!.ImageIds)
        {
            var asset = await media.GetAsync(id, cancellationToken);
            if (asset.Succeeded)
            {
                var thumbnail = asset.Value!.Renditions
                    .OrderBy(rendition => rendition.Width)
                    .ThenBy(rendition => rendition.MimeType == "image/webp" ? 0 : 1)
                    .FirstOrDefault();
                images.Add(new SubmissionImageServiceModel(
                    asset.Value.Id, asset.Value.FileName, asset.Value.Width, asset.Value.Height, asset.Value.OriginalUrl, thumbnail?.Url));
            }
        }

        return OperationResult.Success(row.Value.ToServiceModel(images));
    }

    /// <summary>
    /// Removes pictures a failed submission created. Best effort, one at a time: a delete that fails is logged by type
    /// and the rest still go, and nothing here replaces the error that made the submission fail. A leftover is a hidden
    /// draft, never a public file (ADR 0047 records the sweep that will collect them).
    /// </summary>
    private async Task RemoveAsync(List<MediaSubmissionImageServiceModel> created, CancellationToken cancellationToken)
    {
        foreach (var asset in created)
        {
            try
            {
                var removed = await media.DeleteAsync(asset.Id, asset.ETag, FormsPolicy.AnonymousActor, cancellationToken);
                if (!removed.Succeeded)
                {
                    LogPictureKept(asset.Id, removed.Error!.Code);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPictureKept(asset.Id, exception.GetType().Name);
            }
        }

        created.Clear();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A failed submission's picture {AssetId} was not removed ({Reason}).")]
    private partial void LogPictureKept(Guid assetId, string reason);
}
