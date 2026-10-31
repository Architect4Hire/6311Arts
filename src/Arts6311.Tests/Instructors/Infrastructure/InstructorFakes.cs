using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Instructors.Facade;
using Arts6311.Domain.Modules.Instructors.Business;
using Arts6311.Domain.Modules.Instructors.Data;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Data.Repositories;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;

namespace Arts6311.Tests.Instructors.Infrastructure;

/// <summary>One ordered log of calls, shared by the fakes of one test, so call order is what a test asserts.</summary>
internal sealed class CallLog
{
    public List<string> Calls { get; } = [];

    public void Add(string call) => Calls.Add(call);
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

internal sealed class FixedCorrelation : ICorrelationContext
{
    public Guid Current { get; } = Guid.Parse("0123456789abcdef0123456789abcdef");
}

/// <summary>An outbox that records what was staged, in the shared call order (outbox.md: staged before the save).</summary>
internal sealed class RecordingOutboxWriter(CallLog log) : Arts6311.Domain.Managers.Outbox.IOutboxWriter
{
    public List<(string Type, string Payload)> Staged { get; } = [];

    public void Enqueue(string messageType, string payloadJson, Guid correlationId)
    {
        log.Add($"outbox:{messageType}");
        Staged.Add((messageType, payloadJson));
    }
}

internal sealed class RecordingAuditWriter(CallLog log) : IAuditWriter
{
    public List<AuditEntry> Entries { get; } = [];

    public void Record(AuditEntry entry)
    {
        log.Add($"audit:{entry.Action}");
        Entries.Add(entry);
    }
}

/// <summary>A repository over a list, whose save can be told to fail the way EF fails.</summary>
internal sealed class FakeInstructorsRepository(CallLog log) : IInstructorsRepository
{
    public Task<Instructor?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<InstructorDeletedRow>> ListDeletedAsync(
        InstructorListPosition? after, int take, string? search, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<string>> ListNamingArtistAsync(Guid id, int take, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<global::Arts6311.Domain.Modules.Instructors.Managers.InstructorSiteRow>> ListPublishedAsync(string? slug, CancellationToken cancellationToken) => throw new NotSupportedException();

    public List<Instructor> Rows { get; } = [];

    /// <summary>Thrown by the next save, then cleared.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>What <see cref="SlugExistsAsync"/> answers when set; otherwise it looks at <see cref="Rows"/>.</summary>
    public bool? SlugExists { get; set; }

    public void Add(Instructor instructor)
    {
        log.Add("add");
        Rows.Add(instructor);
    }

    public Task<Instructor?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken)
    {
        log.Add("slug-exists");
        return Task.FromResult(SlugExists ?? Rows.Any(row => row.Slug == slug && row.Id != exceptId));
    }

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Select(row => row.Slug).ToHashSet(StringComparer.Ordinal));

    public Task<List<InstructorSummaryServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
        Task.FromResult(new List<InstructorSummaryServiceModel>());

    public Task<List<InstructorSummaryServiceModel>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Where(row => ids.Contains(row.Id)).Select(Summary).ToList());

    public Task<List<InstructorSummaryServiceModel>> ListBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Where(row => slugs.Contains(row.Slug)).Select(Summary).ToList());

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

    internal static InstructorSummaryServiceModel Summary(Instructor row) =>
        new(row.Id, row.Name, row.Slug, row.Medium, row.Active, row.Status, row.PortraitMediaId, row.UpdatedAt);

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
internal sealed class FakeInstructorsDataLayer : IInstructorsDataLayer
{
    public Task<List<string>> ListNamingArtistAsync(Guid artistId, int take, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Instructor?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<InstructorDeletedRow>> ListDeletedAsync(
        InstructorListPosition? after, int take, string? search, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstructorWriteOutcome> DeleteAsync(
        Instructor entity, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstructorWriteOutcome> RestoreAsync(
        Instructor entity, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<global::Arts6311.Domain.Modules.Instructors.Managers.InstructorSiteRow>> ListPublishedAsync(string? slug, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Dictionary<Guid, Instructor> Rows { get; } = [];

    public InstructorWriteOutcome Outcome { get; set; } = InstructorWriteOutcome.Saved;

    public bool SlugExists { get; set; }

    public List<string> Writes { get; } = [];

    public (PublishCommand Command, PublishStatus From)? LastTransition { get; private set; }

    public Task<Instructor?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.GetValueOrDefault(id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(SlugExists);

    public Task<List<InstructorSummaryServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
        Task.FromResult(new List<InstructorSummaryServiceModel>());

    public List<IReadOnlyCollection<Guid>> SummaryLookups { get; } = [];

    public Task<List<InstructorSummaryServiceModel>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        SummaryLookups.Add(ids);
        return Task.FromResult(Rows.Values.Where(row => ids.Contains(row.Id)).Select(FakeInstructorsRepository.Summary).ToList());
    }

    public Task<List<InstructorSummaryServiceModel>> FindBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Values.Where(row => slugs.Contains(row.Slug)).Select(FakeInstructorsRepository.Summary).ToList());

    public Task<InstructorWriteOutcome> CreateAsync(Instructor instructor, string actorUserId, CancellationToken cancellationToken)
    {
        Writes.Add("create");
        if (Outcome == InstructorWriteOutcome.Saved)
        {
            instructor.RowVersion = Guid.NewGuid();
            Rows[instructor.Id] = instructor;
        }

        return Task.FromResult(Outcome);
    }

    public PublishedPathChange? LastPathChange { get; private set; }

    public Task<InstructorWriteOutcome> UpdateAsync(
        Instructor instructor, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken)
    {
        Writes.Add($"update:{summary}");
        LastPathChange = pathChange;
        return Task.FromResult(Outcome);
    }

    public Task<InstructorWriteOutcome> TransitionAsync(
        Instructor instructor, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
    {
        Writes.Add($"transition:{command}");
        LastTransition = (command, from);
        return Task.FromResult(Outcome);
    }

    public (PublishStatus From, string Reason)? LastAbandon { get; private set; }

    public Task<InstructorWriteOutcome> AbandonScheduleAsync(
        Instructor instructor, PublishStatus from, string reason, CancellationToken cancellationToken)
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

    public List<Instructor> Seeded { get; } = [];

    public Task<int> AddSeedAsync(IReadOnlyList<Instructor> instructors, string actor, CancellationToken cancellationToken)
    {
        Seeded.AddRange(instructors);
        return Task.FromResult(instructors.Count);
    }

    public Instructor Add(PublishStatus status = PublishStatus.Draft, Guid? portrait = null, DateTimeOffset? publishedAt = null)
    {
        var instructor = new Instructor
        {
            Id = Guid.NewGuid(),
            Name = "Jane Doe",
            Slug = "jane-doe",
            Status = status,
            PortraitMediaId = portrait,
            PublishedAt = publishedAt,
            UpdatedBy = "editor-1",
            RowVersion = Guid.NewGuid(),
        };
        Rows[instructor.Id] = instructor;
        return instructor;
    }
}

/// <summary>Business that records what the facade asked and answers what the test set.</summary>
internal sealed class RecordingInstructorsBusiness : IInstructorsBusiness
{
    public Task<OperationResult<CursorPage<InstructorDeletedServiceModel>>> ListDeletedAsync(
        InstructorListViewModel request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult> DeleteAsync(
        Guid id, ContentReferenceCheck references, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<InstructorDetail>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<global::Arts6311.Domain.Modules.Instructors.Managers.InstructorSiteRow>> ListForSiteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<global::Arts6311.Domain.Modules.Instructors.Managers.InstructorSiteRow?> GetForSiteAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Managers.Publishing.PreviewRecord<global::Arts6311.Domain.Modules.Instructors.Managers.InstructorSiteRow>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<Guid, global::Arts6311.Domain.Modules.Instructors.Managers.ServiceModels.SiteInstructorCreditServiceModel>> DescribeCreditsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<global::Arts6311.Domain.Managers.Publishing.SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public static readonly InstructorServiceModel Instructor = new(
        Guid.Parse("44444444-4444-4444-4444-444444444444"), "Jane Doe", "jane-doe", null, null, true, null, null, null, null,
        PublishStatus.Draft, null, null, false, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "editor-1",
        "\"0123456789abcdef0123456789abcdef\"");

    public List<string> Calls { get; } = [];

    public InstructorServiceModel Current { get; set; } = Instructor;

    /// <summary>The artist link Business hands back with <see cref="Current"/>, for the facade to describe.</summary>
    public Guid? CurrentArtistId { get; set; }

    private InstructorDetail Detail => new(Current, CurrentArtistId);

    public (PublishCommand Command, InstructorPortraitCheck? Portrait)? LastTransition { get; private set; }

    public Task<OperationResult<CursorPage<InstructorSummaryServiceModel>>> ListAsync(InstructorListViewModel request, CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return Task.FromResult(OperationResult.Success(new CursorPage<InstructorSummaryServiceModel>([], null)));
    }

    public Task<OperationResult<InstructorDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Calls.Add("get");
        return Task.FromResult(id == Current.Id
            ? OperationResult.Success(Detail)
            : OperationResult.Failure<InstructorDetail>(OperationError.Of(ErrorCodes.NotFound, "No.")));
    }

    public Task<IReadOnlyList<InstructorSummaryServiceModel>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        Calls.Add("summaries");
        return Task.FromResult<IReadOnlyList<InstructorSummaryServiceModel>>([]);
    }

    public Task<IReadOnlyList<InstructorSummaryServiceModel>> FindBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        Calls.Add("by-slugs");
        return Task.FromResult<IReadOnlyList<InstructorSummaryServiceModel>>([]);
    }

    public Task<OperationResult<InstructorDetail>> CreateAsync(InstructorCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add("create");
        return Task.FromResult(OperationResult.Success(Detail));
    }

    public InstructorPortraitCheck? UpdatePortrait { get; private set; }

    public Task<OperationResult<InstructorDetail>> UpdateAsync(
        Guid id,
        InstructorUpdateViewModel update,
        string? ifMatch,
        InstructorPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("update");
        UpdatePortrait = portrait;
        return Task.FromResult(OperationResult.Success(Detail));
    }

    public Task<int> SeedAsync(IReadOnlyList<InstructorSeed> roster, string actor, CancellationToken cancellationToken) =>
        Task.FromResult(0);

    public Task<OperationResult<InstructorDetail>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, InstructorPortraitCheck? portrait, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add($"transition:{command}");
        LastTransition = (command, portrait);
        return Task.FromResult(OperationResult.Success(Detail));
    }

    public DateTimeOffset? LastScheduledFor { get; private set; }

    public InstructorPortraitCheck? SchedulePortrait { get; private set; }

    public Task<OperationResult<InstructorDetail>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        InstructorPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("schedule");
        LastScheduledFor = scheduledFor;
        SchedulePortrait = portrait;
        return Task.FromResult(OperationResult.Success(Detail));
    }

    public Task<OperationResult<InstructorDetail>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add("unschedule");
        return Task.FromResult(OperationResult.Success(Detail));
    }

    public IReadOnlyList<Guid> Due { get; set; } = [];

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        Calls.Add("list-due");
        return Task.FromResult(Due);
    }

    public ScheduledPublishOutcome ScheduledOutcome { get; set; } = ScheduledPublishOutcome.Published;

    public InstructorPortraitCheck? ScheduledPublishPortrait { get; private set; }

    public Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, InstructorPortraitCheck? portrait, CancellationToken cancellationToken)
    {
        Calls.Add("publish-scheduled");
        ScheduledPublishPortrait = portrait;
        return Task.FromResult(ScheduledOutcome);
    }
}

/// <summary>Media as far as Instructors uses it: the attachability question. Everything else is out of scope here.</summary>
internal sealed class FakeMediaFacade : IMediaFacade
{
    public Task<IReadOnlyDictionary<Guid, global::Arts6311.Domain.Modules.Media.Managers.ServiceModels.SiteImageServiceModel>> DescribeForSiteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, global::Arts6311.Domain.Modules.Media.Managers.ServiceModels.SiteImageServiceModel>>(new Dictionary<Guid, global::Arts6311.Domain.Modules.Media.Managers.ServiceModels.SiteImageServiceModel>());

    public HashSet<Guid> Missing { get; } = [];

    public HashSet<Guid> MissingAltText { get; } = [];

    public int Checks { get; private set; }

    public Task<MediaAttachabilityServiceModel> CheckAttachableAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        Checks++;
        return Task.FromResult(new MediaAttachabilityServiceModel(
            [.. ids.Where(Missing.Contains)], [.. ids.Where(MissingAltText.Contains)]));
    }

    public Task<OperationResult<MediaAssetServiceModel>> UploadAsync(MediaUploadViewModel upload, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<MediaAssetServiceModel>> EnsureSiteImageAsync(Guid id, string fileName, Func<Stream> open, string altText, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<MediaSubmissionImageServiceModel>> UploadSubmissionImageAsync(string fileName, Stream content, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<CursorPage<MediaAssetSummaryServiceModel>>> ListAsync(MediaListViewModel request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<MediaAssetServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<MediaAssetServiceModel>> UpdateAsync(
        Guid id, MediaAssetUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<MediaContentServiceModel>> OpenAsync(
        MediaDeliveryViewModel request, MediaViewer viewer, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult> ProduceRenditionsAsync(Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task PurgeAsync(Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>
/// Instructors as far as another module uses it: the lookups that Classes and its seeder ask for, and how a run names
/// its instructor on the site. The CMS routes are out of scope here.
/// </summary>
internal sealed class FakeInstructorsFacade : IInstructorsFacade
{
    public Task<OperationResult<CursorPage<InstructorDeletedServiceModel>>> ListDeletedAsync(
        InstructorListViewModel request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult> DeleteAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<InstructorServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<IReadOnlyList<SiteInstructorServiceModel>>> ListForSiteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<SiteInstructorServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Managers.Publishing.PreviewRecord<global::Arts6311.Domain.Modules.Instructors.Managers.ServiceModels.SiteInstructorServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<Guid, SiteInstructorCreditServiceModel>> DescribeCreditsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        Lookups++;
        return Task.FromResult<IReadOnlyDictionary<Guid, SiteInstructorCreditServiceModel>>(ids
            .Distinct()
            .Where(Roster.ContainsKey)
            .ToDictionary(
                id => id,
                id => new SiteInstructorCreditServiceModel(
                    Roster[id].Name, Roster[id].Status == PublishStatus.Published ? Roster[id].Slug : null)));
    }

    public Task<IReadOnlyList<global::Arts6311.Domain.Managers.Publishing.SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Dictionary<Guid, InstructorSummaryServiceModel> Roster { get; } = [];

    public int Lookups { get; private set; }

    public InstructorSummaryServiceModel Add(string name, PublishStatus status = PublishStatus.Published)
    {
        var instructor = new InstructorSummaryServiceModel(
            Guid.NewGuid(), name, name.ToLowerInvariant().Replace(' ', '-'), null, true, status, null, DateTimeOffset.UnixEpoch);
        Roster[instructor.Id] = instructor;
        return instructor;
    }

    public Task<IReadOnlyList<InstructorSummaryServiceModel>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        Lookups++;
        return Task.FromResult<IReadOnlyList<InstructorSummaryServiceModel>>([.. ids.Distinct().Where(Roster.ContainsKey).Select(id => Roster[id])]);
    }

    public Task<IReadOnlyList<InstructorSummaryServiceModel>> FindBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        Lookups++;
        return Task.FromResult<IReadOnlyList<InstructorSummaryServiceModel>>([.. Roster.Values.Where(instructor => slugs.Contains(instructor.Slug))]);
    }

    public Task<OperationResult<CursorPage<InstructorSummaryServiceModel>>> ListAsync(InstructorListViewModel request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<InstructorServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<InstructorServiceModel>> CreateAsync(InstructorCreateViewModel create, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<InstructorServiceModel>> UpdateAsync(
        Guid id, InstructorUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<InstructorServiceModel>> PublishAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<InstructorServiceModel>> ScheduleAsync(
        Guid id, ContentScheduleViewModel schedule, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<InstructorServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<InstructorServiceModel>> UnpublishAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<InstructorServiceModel>> ArchiveAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
