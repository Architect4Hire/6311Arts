using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Memberships.Business;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Memberships.Facade;

/// <summary>
/// Validates shape, then calls Business. Nothing is cached: these are CMS reads; the site's cached reads arrive in
/// Phase 3.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No other module is asked anything</strong>: a plan credits nobody and shows no library image, so there
/// is no cross-module question to put (backend.md). The facade is the validation and precondition boundary, and
/// nothing more.
/// </para>
/// <para>
/// <strong>No Square call happens here</strong> (ADR 0005): the starting price and cadence are CMS fields, and the
/// push that creates the Square subscription plan from them arrives in 5.x as an outbox message.
/// </para>
/// </remarks>
internal sealed class MembershipsFacade(
    IMembershipsBusiness business,
    IValidator<MembershipPlanCreateViewModel> createValidator,
    IValidator<MembershipPlanUpdateViewModel> updateValidator,
    IValidator<MembershipPlanListViewModel> listValidator,
    IValidator<ContentScheduleViewModel> scheduleValidator) : IMembershipsFacade
{
    private const string What = "this plan";

    public async Task<OperationResult<CursorPage<MembershipPlanSummaryServiceModel>>> ListAsync(
        MembershipPlanListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<MembershipPlanSummaryServiceModel>>(error);
        }

        return await business.ListAsync(request, cancellationToken);
    }

    public Task<OperationResult<MembershipPlanServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        business.GetAsync(id, cancellationToken);

    public async Task<OperationResult<MembershipPlanServiceModel>> CreateAsync(
        MembershipPlanCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        if (await createValidator.ValidateOrErrorAsync(create, cancellationToken) is { } error)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(error);
        }

        return await business.CreateAsync(create, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<MembershipPlanServiceModel>> UpdateAsync(
        Guid id,
        MembershipPlanUpdateViewModel update,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(preconditionError);
        }

        if (await updateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(error);
        }

        return await business.UpdateAsync(id, update, ifMatch, actorUserId, cancellationToken);
    }

    public Task<OperationResult<MembershipPlanServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Publish, ifMatch, actorUserId, cancellationToken);

    public Task<OperationResult<MembershipPlanServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Unpublish, ifMatch, actorUserId, cancellationToken);

    public Task<OperationResult<MembershipPlanServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Archive, ifMatch, actorUserId, cancellationToken);

    public async Task<OperationResult<MembershipPlanServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(preconditionError);
        }

        if (await scheduleValidator.ValidateOrErrorAsync(schedule, cancellationToken) is { } error)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(error);
        }

        return await business.ScheduleAsync(id, schedule.ScheduledFor, ifMatch, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<MembershipPlanServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(preconditionError);
        }

        return await business.UnscheduleAsync(id, ifMatch, actorUserId, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        return business.ListDueAsync(asOf, take, cancellationToken);
    }

    public Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        business.PublishScheduledAsync(id, asOf, cancellationToken);

    /// <summary>A status command. Without <c>If-Match</c> it is a 428 before anything is read.</summary>
    private async Task<OperationResult<MembershipPlanServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<MembershipPlanServiceModel>(preconditionError);
        }

        return await business.TransitionAsync(id, command, ifMatch, actorUserId, cancellationToken);
    }
}
