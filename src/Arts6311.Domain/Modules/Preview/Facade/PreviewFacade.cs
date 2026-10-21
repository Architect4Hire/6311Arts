using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Facade;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Facade;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Memberships.Facade;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
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
    IInstructorsFacade instructors,
    IExhibitionsFacade exhibitions,
    IPagesFacade pages,
    IClassesFacade classes,
    IShopFacade shop,
    IMembershipsFacade memberships,
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
                return Preview(
                    await artists.GetForPreviewAsync(entityId, cancellationToken),
                    entityType, entityId, record => SitePaths.AddressOf(prefix, record.Slug),
                    record => new() { Artist = record });

            case "instructor":
                return Preview(
                    await instructors.GetForPreviewAsync(entityId, cancellationToken),
                    entityType, entityId, record => SitePaths.AddressOf(prefix, record.Slug),
                    record => new() { Instructor = record });

            case "exhibition":
                return Preview(
                    await exhibitions.GetForPreviewAsync(entityId, cancellationToken),
                    entityType, entityId, record => SitePaths.AddressOf(prefix, record.Slug),
                    record => new() { Exhibition = record });

            case "page":
                return Preview(
                    await pages.GetForPreviewAsync(entityId, cancellationToken),
                    entityType, entityId, record => SitePaths.AddressOf(prefix, record.Slug),
                    record => new() { Page = record });

            case "class-offering":
                return Preview(
                    await classes.GetForPreviewAsync(entityId, cancellationToken),
                    entityType, entityId, record => SitePaths.AddressOf(prefix, record.Slug),
                    record => new() { ClassOffering = record });

            case "shop-item":
                return Preview(
                    await shop.GetForPreviewAsync(entityId, cancellationToken),
                    entityType, entityId, record => SitePaths.AddressOf(prefix, record.Slug),
                    record => new() { ShopItem = record });

            case "membership-plan":
                // The prefix is the whole address here: every tier is shown on the memberships page, so there is
                // no slug to append (ADR 0040).
                return Preview(
                    await memberships.GetForPreviewAsync(entityId, cancellationToken),
                    entityType, entityId, _ => prefix,
                    record => new() { MembershipPlan = record });

            default:
                // As above: a type with a prefix but no branch here.
                throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "No preview is built for this content type.");
        }
    }

    /// <summary>
    /// The module's answer as a preview: the site's own model for the record (ADR 0042), so a preview shows exactly
    /// what the published page would, and carries none of the CMS's bookkeeping — <c>updatedBy</c> is a staff user id,
    /// and the <c>ETag</c> and the legacy id say nothing about what the page looks like (ADR 0036).
    /// </summary>
    /// <param name="member">Which one of the preview's named members the record goes in.</param>
    private static OperationResult<ContentPreviewServiceModel> Preview<T>(
        OperationResult<PreviewRecord<T>> found,
        string entityType,
        Guid entityId,
        Func<T, string> pathOf,
        Func<T, PreviewMember> member)
        where T : class
    {
        if (!found.Succeeded)
        {
            return OperationResult.Failure<ContentPreviewServiceModel>(found.Error!);
        }

        var (record, status) = found.Value!;
        var set = member(record);
        return OperationResult.Success(new ContentPreviewServiceModel(
            entityType,
            entityId,
            pathOf(record),
            status,
            NoIndex: true,
            set.Artist,
            set.Exhibition,
            set.Page,
            set.ClassOffering,
            set.ShopItem,
            set.MembershipPlan,
            set.Instructor));
    }

    /// <summary>The one member a preview sets, named so <see cref="Preview{T}"/> stays one method for every type.</summary>
    private sealed record PreviewMember
    {
        public SiteArtistServiceModel? Artist { get; init; }

        public SiteExhibitionServiceModel? Exhibition { get; init; }

        public SitePageServiceModel? Page { get; init; }

        public SiteClassServiceModel? ClassOffering { get; init; }

        public SiteShopItemServiceModel? ShopItem { get; init; }

        public SiteMembershipPlanServiceModel? MembershipPlan { get; init; }

        public SiteInstructorServiceModel? Instructor { get; init; }
    }

    private static OperationResult<ContentPreviewServiceModel> NotFound() =>
        OperationResult.Failure<ContentPreviewServiceModel>(
            OperationError.Of(ErrorCodes.NotFound, "This preview link is not valid any more."));
}
