using Arts6311.Domain.Managers.Caching;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Redirects.Business;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Redirects.Facade;

/// <summary>
/// Validates shape and calls Business; the site's list is cached. Writes don't clear the cache: invalidation comes from
/// <c>site.revalidate-requested</c> handlers, never inline after a save (outbox.md), and that arrives in 2.8. Until then
/// the entry expires on its own (ADR 0035).
/// </summary>
internal sealed class RedirectsFacade(
    IRedirectsBusiness business,
    IAppCache cache,
    IValidator<RedirectCreateViewModel> createValidator,
    IValidator<RedirectUpdateViewModel> updateValidator,
    IValidator<RedirectListViewModel> listValidator,
    IValidator<RedirectSlugChangeViewModel> slugChangeValidator) : IRedirectsFacade
{
    private const string What = "this redirect";

    /// <summary>The site's whole list, under one key; tagged so 2.8's revalidation can clear it with the type.</summary>
    internal static readonly string SiteListKey = CacheKeys.List("redirect", "site");

    internal static readonly string[] SiteListTags = [CacheTags.Type("redirect")];

    public async Task<OperationResult<CursorPage<RedirectServiceModel>>> ListAsync(
        RedirectListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<RedirectServiceModel>>(error);
        }

        return await business.ListAsync(request, cancellationToken);
    }

    public async Task<OperationResult<IReadOnlyList<SiteRedirectServiceModel>>> ListForSiteAsync(CancellationToken cancellationToken)
    {
        var redirects = await cache.GetOrCreateAsync<IReadOnlyList<SiteRedirectServiceModel>>(
            SiteListKey,
            async token => await business.ListForSiteAsync(token),
            SiteListTags,
            RedirectPolicy.SiteCacheLifetime,
            cancellationToken);

        return OperationResult.Success(redirects);
    }

    public Task<OperationResult<RedirectServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        business.GetAsync(id, cancellationToken);

    public async Task<OperationResult<RedirectServiceModel>> CreateAsync(
        RedirectCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        if (await createValidator.ValidateOrErrorAsync(create, cancellationToken) is { } error)
        {
            return OperationResult.Failure<RedirectServiceModel>(error);
        }

        return await business.CreateAsync(create, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<RedirectServiceModel>> UpdateAsync(
        Guid id, RedirectUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<RedirectServiceModel>(preconditionError);
        }

        if (await updateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<RedirectServiceModel>(error);
        }

        return await business.UpdateAsync(id, update, ifMatch, actorUserId, cancellationToken);
    }

    public async Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure(preconditionError);
        }

        return await business.DeleteAsync(id, ifMatch, actorUserId, cancellationToken);
    }

    public async Task<OperationResult> ApplySlugChangeAsync(RedirectSlugChangeViewModel change, CancellationToken cancellationToken)
    {
        if (await slugChangeValidator.ValidateOrErrorAsync(change, cancellationToken) is { } error)
        {
            return OperationResult.Failure(error);
        }

        return await business.ApplySlugChangeAsync(change, cancellationToken);
    }
}
