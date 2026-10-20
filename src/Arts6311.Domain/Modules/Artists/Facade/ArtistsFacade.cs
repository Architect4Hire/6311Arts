using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Artists.Business;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Facade;
using FluentValidation;

namespace Arts6311.Domain.Modules.Artists.Facade;

/// <summary>
/// Validates shape, asks Media about the portrait, then calls Business. Nothing is cached: these are CMS reads of
/// one indexed row or one page; the site's cached reads arrive in 3.1.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The portrait is Media's to vouch for, so the facade asks it</strong> — cross-module traffic is facade to
/// facade (backend.md), and Business calls only its DataLayer. On create and edit a portrait must exist in the
/// library; alt text may follow. On publish it must also have alt text (media.md).
/// </para>
/// <para>
/// <strong>Publish calls Business twice</strong>: once to learn which portrait the artist has, then to publish with
/// Media's answer about it. That is safe because the publish is checked against the editor's <c>If-Match</c>: if
/// the portrait changed in between, so did the row version, and the publish is refused as a conflict (ADR 0031).
/// </para>
/// </remarks>
internal sealed class ArtistsFacade(
    IArtistsBusiness business,
    IMediaFacade media,
    IValidator<ArtistCreateViewModel> createValidator,
    IValidator<ArtistUpdateViewModel> updateValidator,
    IValidator<ArtistListViewModel> listValidator) : IArtistsFacade
{
    private const string What = "this artist";

    public async Task<OperationResult<CursorPage<ArtistSummaryServiceModel>>> ListAsync(
        ArtistListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<ArtistSummaryServiceModel>>(error);
        }

        return await business.ListAsync(request, cancellationToken);
    }

    public Task<OperationResult<ArtistServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        business.GetAsync(id, cancellationToken);

    public async Task<OperationResult<ArtistServiceModel>> CreateAsync(
        ArtistCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        if (await createValidator.ValidateOrErrorAsync(create, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ArtistServiceModel>(error);
        }

        // A new artist is a Draft, so its portrait need only exist; alt text can follow before publishing.
        if (create.PortraitMediaId is { } portraitId && !(await AskMediaAsync(portraitId, cancellationToken)).Exists)
        {
            return OperationResult.Failure<ArtistServiceModel>(PortraitMissing());
        }

        return await business.CreateAsync(create, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<ArtistServiceModel>> UpdateAsync(
        Guid id, ArtistUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ArtistServiceModel>(preconditionError);
        }

        if (await updateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ArtistServiceModel>(error);
        }

        // Business decides whether the answer matters: a published artist's new portrait needs alt text too.
        ArtistPortraitCheck? portrait = null;
        if (update.PortraitMediaId is { } portraitId)
        {
            portrait = await AskMediaAsync(portraitId, cancellationToken);
            if (!portrait.Exists)
            {
                return OperationResult.Failure<ArtistServiceModel>(PortraitMissing());
            }
        }

        return await business.UpdateAsync(id, update, ifMatch, portrait, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<ArtistServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        // A request that can never succeed is refused before Media is asked anything.
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ArtistServiceModel>(preconditionError);
        }

        var current = await business.GetAsync(id, cancellationToken);
        if (!current.Succeeded)
        {
            return current;
        }

        var portrait = current.Value!.PortraitMediaId is { } portraitId
            ? await AskMediaAsync(portraitId, cancellationToken)
            : ArtistPortraitCheck.None;

        return await business.TransitionAsync(id, PublishCommand.Publish, ifMatch, portrait, actorUserId, cancellationToken);
    }

    public Task<OperationResult<ArtistServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        business.TransitionAsync(id, PublishCommand.Unpublish, ifMatch, portrait: null, actorUserId, cancellationToken);

    public Task<OperationResult<ArtistServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        business.TransitionAsync(id, PublishCommand.Archive, ifMatch, portrait: null, actorUserId, cancellationToken);

    /// <summary>What Media says about one image: whether it is an original in the library, and whether it has alt text.</summary>
    private async Task<ArtistPortraitCheck> AskMediaAsync(Guid portraitId, CancellationToken cancellationToken)
    {
        var answer = await media.CheckAttachableAsync([portraitId], cancellationToken);
        return new ArtistPortraitCheck(
            portraitId, Exists: !answer.Missing.Contains(portraitId), HasAltText: !answer.MissingAltText.Contains(portraitId));
    }

    private static OperationError PortraitMissing() => OperationError.Validation(
        "The portrait is not in the media library.",
        [("portraitMediaId", "Choose an image from the media library.")]);
}
