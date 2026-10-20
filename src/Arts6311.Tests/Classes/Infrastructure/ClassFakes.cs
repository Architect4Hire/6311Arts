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

    public Task<List<ClassSummaryServiceModel>> ListAsync(
        ClassListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(new List<ClassSummaryServiceModel>());

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

    public Task<List<ClassSummaryServiceModel>> ListAsync(
        ClassListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(new List<ClassSummaryServiceModel>());

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

    /// <summary>One offering in the store, with one session per SKU given.</summary>
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
            StartingPriceCents = startingPriceCents,
            OfferingNumber = 3,
            Status = status,
            HeroMediaId = hero,
            PublishedAt = publishedAt,
            UpdatedBy = "editor-1",
            RowVersion = Guid.NewGuid(),
        };

        for (var index = 0; index < skus.Length; index++)
        {
            var session = new ClassSession
            {
                Id = Guid.NewGuid(),
                ClassOfferingId = offering.Id,
                StartsAt = new DateTimeOffset(2026, 10, 24, 19, 0, 0, TimeSpan.Zero).AddDays(7 * index),
                EndsAt = new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero).AddDays(7 * index),
                DisplayTimeZone = GalleryTimeZone.Id,
                Sku = skus[index],
                Capacity = 10,
            };
            offering.Sessions.Add(session);
            SkuOwners[session.Sku] = new SkuOwner(CatalogOwnerTypes.ClassSession, session.Id);
        }

        Rows[offering.Id] = offering;
        return offering;
    }
}

/// <summary>Business that records what the facade asked and answers what the test set.</summary>
internal sealed class RecordingClassesBusiness : IClassesBusiness
{
    public static readonly ClassServiceModel Offering = new(
        Guid.Parse("66666666-6666-6666-6666-666666666666"),
        "Intro to Screen Printing",
        "intro-to-screen-printing",
        "A class.",
        null,
        "Printmaking",
        "Beginner",
        "Gwen Schilling",
        "Saturdays",
        30000,
        CatalogPolicy.Currency,
        StartingPriceEditable: true,
        OfferingNumber: 3,
        HeroMediaId: null,
        ImageMediaIds: [],
        Sessions: [],
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

    public ClassMediaCheck? CreateMedia { get; private set; }

    public ClassMediaCheck? UpdateMedia { get; private set; }

    public (PublishCommand Command, ClassMediaCheck? Media)? LastTransition { get; private set; }

    public DateTimeOffset? LastScheduledFor { get; private set; }

    public ClassMediaCheck? ScheduleMedia { get; private set; }

    public ClassMediaCheck? ScheduledPublishMedia { get; private set; }

    public IReadOnlyList<Guid> Due { get; set; } = [];

    public ScheduledPublishOutcome ScheduledOutcome { get; set; } = ScheduledPublishOutcome.Published;

    public Task<OperationResult<CursorPage<ClassSummaryServiceModel>>> ListAsync(
        ClassListViewModel request, CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return Task.FromResult(OperationResult.Success(new CursorPage<ClassSummaryServiceModel>([], null)));
    }

    public Task<OperationResult<ClassServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Calls.Add("get");
        return Task.FromResult(id == Current.Id
            ? OperationResult.Success(Current)
            : OperationResult.Failure<ClassServiceModel>(OperationError.Of(ErrorCodes.NotFound, "No.")));
    }

    public Task<OperationResult<ClassServiceModel>> CreateAsync(
        ClassCreateViewModel create, ClassMediaCheck media, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add("create");
        CreateMedia = media;
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<ClassServiceModel>> UpdateAsync(
        Guid id,
        ClassUpdateViewModel update,
        string? ifMatch,
        ClassMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("update");
        UpdateMedia = media;
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<ClassServiceModel>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ClassMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add($"transition:{command}");
        LastTransition = (command, media);
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<ClassServiceModel>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        ClassMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("schedule");
        LastScheduledFor = scheduledFor;
        ScheduleMedia = media;
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<ClassServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add("unschedule");
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        Calls.Add("list-due");
        return Task.FromResult(Due);
    }

    public Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, ClassMediaCheck? media, CancellationToken cancellationToken)
    {
        Calls.Add("publish-scheduled");
        ScheduledPublishMedia = media;
        return Task.FromResult(ScheduledOutcome);
    }

    public Task<int> SeedAsync(IReadOnlyList<ClassSeed> seeds, string actor, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
