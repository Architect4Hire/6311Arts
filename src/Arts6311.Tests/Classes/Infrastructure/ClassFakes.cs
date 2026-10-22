using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Classes.Business;
using Arts6311.Domain.Modules.Classes.Data;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Data.Repositories;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Arts6311.Tests.Artists.Infrastructure;
using Arts6311.Tests.Infrastructure;

namespace Arts6311.Tests.Classes.Infrastructure;

/// <summary>A repository over a list, whose save can be told to fail the way EF fails.</summary>
internal sealed class FakeClassesRepository(CallLog log) : IClassesRepository
{
    public Task<ClassOffering?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<ClassDeletedRow>> ListDeletedAsync(
        ClassListPosition? after, int take, string? search, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<string>> ListNamingInstructorAsync(Guid id, int take, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<global::Arts6311.Domain.Modules.Classes.Data.Entities.ClassOffering>> ListPublishedAsync(string? slug, DateTimeOffset? endingAfter, CancellationToken cancellationToken) => throw new NotSupportedException();

    public List<ClassOffering> Rows { get; } = [];

    /// <summary>Thrown by the next save, then cleared.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>What <see cref="SlugExistsAsync"/> answers when set; otherwise it looks at <see cref="Rows"/>.</summary>
    public bool? SlugExists { get; set; }

    public int NextOfferingNumber { get; set; } = 1;

    public void Add(ClassOffering offering)
    {
        log.Add("add");
        Rows.Add(offering);
    }

    public Task<ClassOffering?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken)
    {
        log.Add("slug-exists");
        return Task.FromResult(SlugExists ?? Rows.Any(row => row.Slug == slug && row.Id != exceptId));
    }

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Select(row => row.Slug).ToHashSet(StringComparer.Ordinal));

    public Task<int> NextOfferingNumberAsync(CancellationToken cancellationToken) =>
        Task.FromResult(NextOfferingNumber);

    public Task<List<ClassListRow>> ListAsync(
        ClassListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(new List<ClassListRow>());

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        log.Add("list-due");
        return Task.FromResult<IReadOnlyList<Guid>>(
        [
            .. Rows
                .Where(row => row.Status == PublishStatus.Scheduled && row.ScheduledFor is { } when && when <= asOf)
                .OrderBy(row => row.ScheduledFor)
                .Take(take)
                .Select(row => row.Id),
        ]);
    }

    public void DiscardChanges() => log.Add("discard");

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        log.Add("save");
        if (SaveFailure is { } failure)
        {
            SaveFailure = null;
            throw failure;
        }

        return Task.CompletedTask;
    }
}

/// <summary>A DataLayer over a dictionary that records what it was asked to write and answers a chosen outcome.</summary>
internal sealed class FakeClassesDataLayer : IClassesDataLayer
{
    public Task<List<string>> ListNamingInstructorAsync(Guid instructorId, int take, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ClassOffering?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<ClassDeletedRow>> ListDeletedAsync(
        ClassListPosition? after, int take, string? search, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ClassWriteOutcome> DeleteAsync(
        ClassOffering entity, SkuChanges skuChanges, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ClassWriteOutcome> RestoreAsync(
        ClassOffering entity, SkuChanges skuChanges, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<global::Arts6311.Domain.Modules.Classes.Data.Entities.ClassOffering>> ListPublishedAsync(string? slug, DateTimeOffset? endingAfter, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Dictionary<Guid, ClassOffering> Rows { get; } = [];

    public Dictionary<string, SkuOwner> SkuOwners { get; } = new(StringComparer.Ordinal);

    public ClassWriteOutcome Outcome { get; set; } = ClassWriteOutcome.Saved;

    public bool SlugExists { get; set; }

    public int NextOfferingNumber { get; set; } = 4;

    public List<string> Writes { get; } = [];

    public SkuChanges? LastSkuChanges { get; private set; }

    public (PublishCommand Command, PublishStatus From)? LastTransition { get; private set; }

    public (PublishStatus From, string Reason)? LastAbandon { get; private set; }

    public List<ClassOffering> Seeded { get; } = [];

    public Task<ClassOffering?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.GetValueOrDefault(id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(SlugExists);

    public Task<IReadOnlyDictionary<string, SkuOwner>> SkuOwnersAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, SkuOwner>>(
            skus.Where(SkuOwners.ContainsKey).Distinct(StringComparer.Ordinal)
                .ToDictionary(sku => sku, sku => SkuOwners[sku], StringComparer.Ordinal));

    public Task<int> NextOfferingNumberAsync(CancellationToken cancellationToken) =>
        Task.FromResult(NextOfferingNumber);

    public Task<List<ClassListRow>> ListAsync(
        ClassListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(new List<ClassListRow>());

    public Task<ClassWriteOutcome> CreateAsync(
        ClassOffering offering, SkuChanges skus, string actorUserId, string summary, CancellationToken cancellationToken)
    {
        Writes.Add($"create:{summary}");
        LastSkuChanges = skus;
        if (Outcome == ClassWriteOutcome.Saved)
        {
            offering.RowVersion = Guid.NewGuid();
            Rows[offering.Id] = offering;
        }

        return Task.FromResult(Outcome);
    }

    public Task<ClassWriteOutcome> UpdateAsync(
        ClassOffering offering,
        SkuChanges skus,
        string actorUserId,
        string summary,
        PublishedPathChange? pathChange,
        CancellationToken cancellationToken)
    {
        Writes.Add($"update:{summary}");
        LastSkuChanges = skus;
        return Task.FromResult(Outcome);
    }

    public Task<ClassWriteOutcome> TransitionAsync(
        ClassOffering offering, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
    {
        Writes.Add($"transition:{command}");
        LastTransition = (command, from);
        return Task.FromResult(Outcome);
    }

    public Task<ClassWriteOutcome> AbandonScheduleAsync(
        ClassOffering offering, PublishStatus from, string reason, CancellationToken cancellationToken)
    {
        Writes.Add("abandon-schedule");
        LastAbandon = (from, reason);
        return Task.FromResult(Outcome);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(
        [
            .. Rows.Values
                .Where(row => row.Status == PublishStatus.Scheduled && row.ScheduledFor is { } when && when <= asOf)
                .OrderBy(row => row.ScheduledFor)
                .Take(take)
                .Select(row => row.Id),
        ]);

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Values.Select(row => row.Slug).ToHashSet(StringComparer.Ordinal));

    public Task<int> AddSeedAsync(
        IReadOnlyList<ClassOffering> offerings, SkuChanges skus, string actor, CancellationToken cancellationToken)
    {
        Seeded.AddRange(offerings);
        LastSkuChanges = skus;
        return Task.FromResult(offerings.Count);
    }

    /// <summary>The instructor <see cref="Add"/> gives every run it makes.</summary>
    public static readonly Guid InstructorId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    /// <summary>
    /// One offering in the store, with one run per SKU given, each taught by <see cref="InstructorId"/>, priced at
    /// <paramref name="startingPriceCents"/>, with one session a week apart from the last.
    /// </summary>
    public ClassOffering Add(
        PublishStatus status = PublishStatus.Draft,
        Guid? hero = null,
        DateTimeOffset? publishedAt = null,
        string? summary = "A class.",
        long? startingPriceCents = 3000,
        params string[] skus)
    {
        var offering = new ClassOffering
        {
            Id = Guid.NewGuid(),
            Title = "Intro to Screen Printing",
            Slug = "intro-to-screen-printing",
            Summary = summary,
            OfferingNumber = 3,
            Status = status,
            HeroMediaId = hero,
            PublishedAt = publishedAt,
            UpdatedBy = "editor-1",
            RowVersion = Guid.NewGuid(),
        };

        for (var index = 0; index < skus.Length; index++)
        {
            var run = new ClassRun
            {
                Id = Guid.NewGuid(),
                ClassOfferingId = offering.Id,
                InstructorId = InstructorId,
                DisplayTimeZone = GalleryTimeZone.Id,
                Sku = skus[index],
                Capacity = 10,
                StartingPriceCents = startingPriceCents,
            };
            run.Sessions.Add(new ClassSession
            {
                Id = Guid.NewGuid(),
                ClassRunId = run.Id,
                StartsAt = new DateTimeOffset(2026, 10, 24, 19, 0, 0, TimeSpan.Zero).AddDays(7 * index),
                EndsAt = new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero).AddDays(7 * index),
            });
            offering.Runs.Add(run);
            SkuOwners[run.Sku] = new SkuOwner(CatalogOwnerTypes.ClassRun, run.Id);
        }

        Rows[offering.Id] = offering;
        return offering;
    }
}

/// <summary>Business that records what the facade asked and answers what the test set.</summary>
internal sealed class RecordingClassesBusiness : IClassesBusiness
{
    public Task<OperationResult<CursorPage<ClassDeletedServiceModel>>> ListDeletedAsync(
        ClassListViewModel request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult> DeleteAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<ClassDetail>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<global::Arts6311.Domain.Modules.Classes.Managers.ClassSiteRow>> ListForSiteAsync(Guid? instructorId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<global::Arts6311.Domain.Modules.Classes.Managers.ClassSiteRow?> GetForSiteAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Managers.Publishing.PreviewRecord<global::Arts6311.Domain.Modules.Classes.Managers.ClassSiteRow>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<global::Arts6311.Domain.Managers.Publishing.SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public static readonly ClassServiceModel Offering = new(
        Guid.Parse("66666666-6666-6666-6666-666666666666"),
        "Intro to Screen Printing",
        "intro-to-screen-printing",
        "A class.",
        null,
        "Printmaking",
        "Beginner",
        OfferingNumber: 3,
        HeroMediaId: null,
        ImageMediaIds: [],
        Runs: [],
        PublishStatus.Draft,
        null,
        null,
        SlugLocked: false,
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        "editor-1",
        EntityTag.Format(Guid.Parse("0123456789abcdef0123456789abcdef")));

    public List<string> Calls { get; } = [];

    public ClassServiceModel Current { get; set; } = Offering;

    /// <summary>Which instructor each of <see cref="Current"/>'s runs names, handed back for the facade to describe.</summary>
    public Dictionary<Guid, Guid> RunInstructors { get; } = [];

    private ClassDetail Detail => new(Current, RunInstructors);

    public ClassMediaCheck? CreateMedia { get; private set; }

    public ClassMediaCheck? UpdateMedia { get; private set; }

    public ClassInstructorCheck? CreateInstructors { get; private set; }

    public ClassInstructorCheck? UpdateInstructors { get; private set; }

    /// <summary>The rows <see cref="ListAsync"/> answers with.</summary>
    public List<ClassListRow> ListRows { get; } = [];

    public (PublishCommand Command, ClassMediaCheck? Media)? LastTransition { get; private set; }

    public DateTimeOffset? LastScheduledFor { get; private set; }

    public ClassMediaCheck? ScheduleMedia { get; private set; }

    public ClassMediaCheck? ScheduledPublishMedia { get; private set; }

    public IReadOnlyList<Guid> Due { get; set; } = [];

    public ScheduledPublishOutcome ScheduledOutcome { get; set; } = ScheduledPublishOutcome.Published;

    public Task<OperationResult<CursorPage<ClassListRow>>> ListAsync(
        ClassListViewModel request, CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return Task.FromResult(OperationResult.Success(new CursorPage<ClassListRow>([.. ListRows], null)));
    }

    public Task<OperationResult<ClassDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Calls.Add("get");
        return Task.FromResult(id == Current.Id
            ? OperationResult.Success(Detail)
            : OperationResult.Failure<ClassDetail>(OperationError.Of(ErrorCodes.NotFound, "No.")));
    }

    public Task<OperationResult<ClassDetail>> CreateAsync(
        ClassCreateViewModel create,
        ClassMediaCheck media,
        ClassInstructorCheck instructors,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("create");
        CreateMedia = media;
        CreateInstructors = instructors;
        return Task.FromResult(OperationResult.Success(Detail));
    }

    public Task<OperationResult<ClassDetail>> UpdateAsync(
        Guid id,
        ClassUpdateViewModel update,
        string? ifMatch,
        ClassMediaCheck? media,
        ClassInstructorCheck? instructors,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("update");
        UpdateMedia = media;
        UpdateInstructors = instructors;
        return Task.FromResult(OperationResult.Success(Detail));
    }

    /// <summary>What the facade said about the instructors the runs name, on each path that asks (ADR 0052).</summary>
    public ClassInstructorCheck? TransitionInstructors { get; private set; }

    public ClassInstructorCheck? ScheduleInstructors { get; private set; }

    public ClassInstructorCheck? ScheduledPublishInstructors { get; private set; }

    public Task<OperationResult<ClassDetail>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ClassMediaCheck? media,
        ClassInstructorCheck? instructors,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add($"transition:{command}");
        LastTransition = (command, media);
        TransitionInstructors = instructors;
        return Task.FromResult(OperationResult.Success(Detail));
    }

    public Task<OperationResult<ClassDetail>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        ClassMediaCheck? media,
        ClassInstructorCheck? instructors,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("schedule");
        LastScheduledFor = scheduledFor;
        ScheduleMedia = media;
        ScheduleInstructors = instructors;
        return Task.FromResult(OperationResult.Success(Detail));
    }

    public Task<OperationResult<ClassDetail>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add("unschedule");
        return Task.FromResult(OperationResult.Success(Detail));
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        Calls.Add("list-due");
        return Task.FromResult(Due);
    }

    public Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, ClassMediaCheck? media, ClassInstructorCheck? instructors,
        CancellationToken cancellationToken)
    {
        Calls.Add("publish-scheduled");
        ScheduledPublishMedia = media;
        ScheduledPublishInstructors = instructors;
        return Task.FromResult(ScheduledOutcome);
    }

    public Task<int> SeedAsync(IReadOnlyList<ClassSeed> seeds, string actor, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
