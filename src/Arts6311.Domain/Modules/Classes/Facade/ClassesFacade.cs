using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Html;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Classes.Business;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Facade;
using FluentValidation;

namespace Arts6311.Domain.Modules.Classes.Facade;

/// <summary>
/// Validates shape, asks Media about the hero and the body's pictures, then calls Business. Nothing is cached:
/// these are CMS reads; the site's cached reads arrive in Phase 3.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Media is asked here, never in Business</strong> — cross-module traffic is facade to facade (backend.md).
/// It vouches for the hero, as it does for an artist's portrait (ADR 0031), and for each picture in the sanitized
/// body, as it does for a page's (ADR 0034). The body is sanitized here only to learn which pictures it shows;
/// Business sanitizes it again as the single writer of what is stored, and the sanitizer is idempotent.
/// </para>
/// <para>
/// <strong>Publish calls Business twice</strong>: once to learn which hero the offering has, then to publish with
/// Media's answer about it. The publish is checked against the editor's <c>If-Match</c>, so a hero changed in
/// between moves the row version and the publish is refused as a conflict (ADR 0031).
/// </para>
/// <para>
/// <strong>No Square call happens here</strong> (ADR 0032): the SKU, the capacity and the starting price are CMS
/// fields, and the push that creates the Square objects from them arrives in 5.x as an outbox message.
/// </para>
/// </remarks>
internal sealed class ClassesFacade(
    IClassesBusiness business,
    IMediaFacade media,
    IHtmlSanitizer sanitizer,
    IValidator<ClassCreateViewModel> createValidator,
    IValidator<ClassUpdateViewModel> updateValidator,
    IValidator<ClassListViewModel> listValidator,
    IValidator<ContentScheduleViewModel> scheduleValidator) : IClassesFacade
{
    private const string What = "this class";

    public async Task<OperationResult<CursorPage<ClassSummaryServiceModel>>> ListAsync(
        ClassListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<ClassSummaryServiceModel>>(error);
        }

        return await business.ListAsync(request, cancellationToken);
    }

    public Task<OperationResult<ClassServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        business.GetAsync(id, cancellationToken);

    public async Task<OperationResult<ClassServiceModel>> CreateAsync(
        ClassCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        if (await createValidator.ValidateOrErrorAsync(create, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ClassServiceModel>(error);
        }

        // A new offering is a Draft, so its hero need only exist; alt text can follow before publishing.
        var check = await AskBodyAsync(create.Body, create.HeroMediaId, cancellationToken);
        if (create.HeroMediaId is not null && !check.HeroExists)
        {
            return OperationResult.Failure<ClassServiceModel>(HeroMissing());
        }

        return await business.CreateAsync(create, check, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<ClassServiceModel>> UpdateAsync(
        Guid id, ClassUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ClassServiceModel>(preconditionError);
        }

        if (await updateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ClassServiceModel>(error);
        }

        // Business decides whether the answer matters: a published offering's new hero needs alt text too.
        ClassMediaCheck? check = null;
        if (update.HeroMediaId is not null || update.Body is not null)
        {
            check = await AskBodyAsync(update.Body, update.HeroMediaId, cancellationToken);
            if (update.HeroMediaId is not null && !check.HeroExists)
            {
                return OperationResult.Failure<ClassServiceModel>(HeroMissing());
            }
        }

        return await business.UpdateAsync(id, update, ifMatch, check, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<ClassServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        // A request that can never succeed is refused before Media is asked anything.
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ClassServiceModel>(preconditionError);
        }

        var hero = await HeroOfAsync(id, cancellationToken);
        if (!hero.Succeeded)
        {
            return OperationResult.Failure<ClassServiceModel>(hero.Error!);
        }

        return await business.TransitionAsync(
            id, PublishCommand.Publish, ifMatch, hero.Value, actorUserId, cancellationToken);
    }

    public Task<OperationResult<ClassServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Unpublish, ifMatch, actorUserId, cancellationToken);

    public Task<OperationResult<ClassServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Archive, ifMatch, actorUserId, cancellationToken);

    public async Task<OperationResult<ClassServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ClassServiceModel>(preconditionError);
        }

        if (await scheduleValidator.ValidateOrErrorAsync(schedule, cancellationToken) is { } error)
        {
            return OperationResult.Failure<ClassServiceModel>(error);
        }

        // Scheduling is a publish with a delay, so the hero is vouched for now as well: the same two Business calls,
        // safe for the same reason — the If-Match covers anything that changed in between (ADR 0031).
        var hero = await HeroOfAsync(id, cancellationToken);
        if (!hero.Succeeded)
        {
            return OperationResult.Failure<ClassServiceModel>(hero.Error!);
        }

        return await business.ScheduleAsync(
            id, schedule.ScheduledFor, ifMatch, hero.Value, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<ClassServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ClassServiceModel>(preconditionError);
        }

        return await business.UnscheduleAsync(id, ifMatch, actorUserId, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        return business.ListDueAsync(asOf, take, cancellationToken);
    }

    public async Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var hero = await HeroOfAsync(id, cancellationToken);
        if (hero.Succeeded)
        {
            return await business.PublishScheduledAsync(id, asOf, hero.Value, cancellationToken);
        }

        // Gone since the due list was read: nothing for this pass to do. Anything else — Media unreachable, say — is
        // a failure the pass logs and counts, not a quiet "nothing due" that would repeat for ever (ADR 0036).
        return hero.Error!.Code == ErrorCodes.NotFound
            ? ScheduledPublishOutcome.Skipped
            : throw new InvalidOperationException(
                $"Reading class {id} for its scheduled publish failed ({hero.Error.Code}).");
    }

    /// <summary>
    /// A command that asks no other module. Without <c>If-Match</c> it is a 428 before anything is read.
    /// </summary>
    private async Task<OperationResult<ClassServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<ClassServiceModel>(preconditionError);
        }

        return await business.TransitionAsync(id, command, ifMatch, media: null, actorUserId, cancellationToken);
    }

    /// <summary>
    /// What Media says about the offering's current hero, or <see cref="ClassMediaCheck.None"/> when there is none.
    /// Business checks the answer is about the hero the offering still has.
    /// </summary>
    private async Task<OperationResult<ClassMediaCheck>> HeroOfAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await business.GetAsync(id, cancellationToken);
        if (!current.Succeeded)
        {
            return OperationResult.Failure<ClassMediaCheck>(current.Error!);
        }

        return OperationResult.Success(current.Value!.HeroMediaId is { } heroId
            ? await AskHeroCheckAsync(heroId, cancellationToken)
            : ClassMediaCheck.None);
    }

    /// <summary>
    /// What Media says about a body's pictures and, when one is sent, the hero: one question for both, because
    /// <c>CheckAttachableAsync</c> takes them together.
    /// </summary>
    private async Task<ClassMediaCheck> AskBodyAsync(string? body, Guid? heroId, CancellationToken cancellationToken)
    {
        var bodyImages = sanitizer.Sanitize(body).MediaAssetIds;
        var asked = heroId is { } hero ? bodyImages.Append(hero).Distinct().ToList() : [.. bodyImages];
        if (asked.Count == 0)
        {
            return ClassMediaCheck.None;
        }

        var answer = await media.CheckAttachableAsync(asked, cancellationToken);
        return new ClassMediaCheck(
            heroId,
            HeroExists: heroId is null || !answer.Missing.Contains(heroId.Value),
            HeroHasAltText: heroId is null || !answer.MissingAltText.Contains(heroId.Value),
            AskedBodyImages: bodyImages.ToHashSet(),
            MissingBodyImages: answer.Missing.ToHashSet());
    }

    /// <summary>What Media says about one hero image, with no body in the question.</summary>
    private async Task<ClassMediaCheck> AskHeroCheckAsync(Guid heroId, CancellationToken cancellationToken)
    {
        var answer = await media.CheckAttachableAsync([heroId], cancellationToken);
        return new ClassMediaCheck(
            heroId,
            HeroExists: !answer.Missing.Contains(heroId),
            HeroHasAltText: !answer.MissingAltText.Contains(heroId),
            AskedBodyImages: new HashSet<Guid>(),
            MissingBodyImages: new HashSet<Guid>());
    }

    private static OperationError HeroMissing() => OperationError.Validation(
        "The hero image is not in the media library.",
        [("heroMediaId", "Choose an image from the media library.")]);
}
