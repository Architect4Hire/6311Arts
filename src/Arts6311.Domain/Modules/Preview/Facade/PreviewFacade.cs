using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Facade;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Facade;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Facade;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Preview.Managers;
using Arts6311.Domain.Modules.Preview.Managers.ServiceModels;
using Arts6311.Domain.Modules.Preview.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Preview.Facade;

/// <summary>
/// Mints preview links and reads them back, asking each content module's facade for the record itself.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a module of its own.</strong> One token has to resolve to any content type, so something must know
/// all of them. A module reaches another only through its facade and its ServiceModels (backend.md), which is all
/// this does; putting it inside Artists, or in the kernel, would mean one content type knowing the others, or the
/// kernel knowing modules at all.
/// </para>
/// <para>
/// <strong>It has no Business or Data layer</strong>, like Operations: there is no rule to own and nothing to
/// persist. A link is not audited either — it grants one read of a record its holder's colleague could already
/// open in the CMS, and minting one changes nothing (ADR 0036).
/// </para>
/// <para>
/// <strong>Read-only by construction.</strong> The token reaches exactly these two operations; no write path takes
/// one, and the route that reads it is anonymous and safe (ADR 0036).
/// </para>
/// </remarks>
internal sealed class PreviewFacade(
    IPreviewTokens tokens,
    IArtistsFacade artists,
    IExhibitionsFacade exhibitions,
    IPagesFacade pages,
    IClassesFacade classes,
    IShopFacade shop,
    IValidator<PreviewLinkRequestViewModel> requestValidator) : IPreviewFacade
{
    public async Task<OperationResult<PreviewLinkServiceModel>> CreateLinkAsync(
        PreviewLinkRequestViewModel request, CancellationToken cancellationToken)
    {
        if (await requestValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<PreviewLinkServiceModel>(error);
        }

        // Read it first, so a staff member asking for a link to something that is gone is told now rather than
        // handed a link that answers 404.
        var preview = await ReadRecordAsync(request.EntityType, request.EntityId, cancellationToken);
        if (!preview.Succeeded)
        {
            return OperationResult.Failure<PreviewLinkServiceModel>(preview.Error!);
        }

        var (token, expiresAt) = tokens.Create(request.EntityType, request.EntityId);
        return OperationResult.Success(new PreviewLinkServiceModel(token, PreviewPolicy.PathFor(token), expiresAt));
    }

    public async Task<OperationResult<ContentPreviewServiceModel>> ReadAsync(string? token, CancellationToken cancellationToken)
    {
        if (tokens.Read(token) is not { } subject)
        {
            return NotFound();
        }

        var preview = await ReadRecordAsync(subject.EntityType, subject.EntityId, cancellationToken);

        // A record that has gone since the link was made is the same answer as a bad token: a holder learns
        // nothing about what does or does not exist.
        return preview.Succeeded ? preview : NotFound();
    }

    private async Task<OperationResult<ContentPreviewServiceModel>> ReadRecordAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        // Unreachable for a type the validator and the token reader allow, so a type added to PreviewPolicy without
        // a prefix fails loudly in tests rather than quietly answering 404 to every link (ADR 0036).
        var prefix = PreviewPolicy.PathPrefixes.TryGetValue(entityType, out var found)
            ? found
            : throw new ArgumentOutOfRangeException(
                nameof(entityType), entityType, "No site address is mapped for this content type.");

        switch (entityType)
        {
            case "artist":
                var artist = await artists.GetAsync(entityId, cancellationToken);
                return artist.Succeeded
                    ? Preview(
                        entityType,
                        entityId,
                        SitePaths.AddressOf(prefix, artist.Value!.Slug),
                        artist.Value.Status,
                        artist: ForPreview(artist.Value))
                    : OperationResult.Failure<ContentPreviewServiceModel>(artist.Error!);

            case "exhibition":
                var exhibition = await exhibitions.GetAsync(entityId, cancellationToken);
                return exhibition.Succeeded
                    ? Preview(
                        entityType,
                        entityId,
                        SitePaths.AddressOf(prefix, exhibition.Value!.Slug),
                        exhibition.Value.Status,
                        exhibition: ForPreview(exhibition.Value))
                    : OperationResult.Failure<ContentPreviewServiceModel>(exhibition.Error!);

            case "page":
                var page = await pages.GetAsync(entityId, cancellationToken);
                return page.Succeeded
                    ? Preview(
                        entityType,
                        entityId,
                        SitePaths.AddressOf(prefix, page.Value!.Slug),
                        page.Value.Status,
                        page: ForPreview(page.Value))
                    : OperationResult.Failure<ContentPreviewServiceModel>(page.Error!);

            case "class-offering":
                var offering = await classes.GetAsync(entityId, cancellationToken);
                return offering.Succeeded
                    ? Preview(
                        entityType,
                        entityId,
                        SitePaths.AddressOf(prefix, offering.Value!.Slug),
                        offering.Value.Status,
                        classOffering: ForPreview(offering.Value))
                    : OperationResult.Failure<ContentPreviewServiceModel>(offering.Error!);

            case "shop-item":
                var piece = await shop.GetAsync(entityId, cancellationToken);
                return piece.Succeeded
                    ? Preview(
                        entityType,
                        entityId,
                        SitePaths.AddressOf(prefix, piece.Value!.Slug),
                        piece.Value.Status,
                        shopItem: ForPreview(piece.Value))
                    : OperationResult.Failure<ContentPreviewServiceModel>(piece.Error!);

            default:
                // As above: a type with a prefix but no branch here.
                throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "No preview is built for this content type.");
        }
    }

    /// <summary>
    /// The CMS record with the CMS's own bookkeeping emptied (ADR 0036): a preview is read by whoever holds the
    /// link, and <c>updatedBy</c> is a staff user id, while the <c>ETag</c> and the legacy id say nothing about
    /// what the page looks like. The fields stay in the shape, because the shape is the CMS ServiceModel until
    /// 3.1 defines the site's own.
    /// </summary>
    private static ArtistServiceModel ForPreview(ArtistServiceModel artist) =>
        artist with { UpdatedBy = string.Empty, ETag = string.Empty, LegacyWordPressId = null };

    /// <inheritdoc cref="ForPreview(ArtistServiceModel)"/>
    private static ExhibitionServiceModel ForPreview(ExhibitionServiceModel exhibition) =>
        exhibition with { UpdatedBy = string.Empty, ETag = string.Empty, LegacyWordPressId = null };

    /// <inheritdoc cref="ForPreview(ArtistServiceModel)"/>
    private static PageServiceModel ForPreview(PageServiceModel page) =>
        page with { UpdatedBy = string.Empty, ETag = string.Empty, LegacyWordPressId = null };

    /// <inheritdoc cref="ForPreview(ArtistServiceModel)"/>
    private static ClassServiceModel ForPreview(ClassServiceModel offering) =>
        offering with { UpdatedBy = string.Empty, ETag = string.Empty, LegacyWordPressId = null };

    /// <inheritdoc cref="ForPreview(ArtistServiceModel)"/>
    private static ShopItemServiceModel ForPreview(ShopItemServiceModel item) =>
        item with { UpdatedBy = string.Empty, ETag = string.Empty, LegacyWordPressId = null };

    private static OperationResult<ContentPreviewServiceModel> Preview(
        string entityType,
        Guid entityId,
        string path,
        PublishStatus status,
        ArtistServiceModel? artist = null,
        ExhibitionServiceModel? exhibition = null,
        PageServiceModel? page = null,
        ClassServiceModel? classOffering = null,
        ShopItemServiceModel? shopItem = null) =>
        OperationResult.Success(new ContentPreviewServiceModel(
            entityType, entityId, path, status, NoIndex: true, artist, exhibition, page, classOffering, shopItem));

    private static OperationResult<ContentPreviewServiceModel> NotFound() =>
        OperationResult.Failure<ContentPreviewServiceModel>(
            OperationError.Of(ErrorCodes.NotFound, "This preview link is not valid any more."));
}
