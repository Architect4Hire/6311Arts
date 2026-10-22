using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Business;
using Arts6311.Domain.Modules.Artists.Data;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Data.Repositories;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;

namespace Arts6311.Tests.Artists.Infrastructure;

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
internal sealed class FakeArtistsRepository(CallLog log) : IArtistsRepository
{
    public Task<Artist?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<ArtistDeletedRow>> ListDeletedAsync(
        ArtistListPosition? after, int take, string? search, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<global::Arts6311.Domain.Modules.Artists.Managers.ArtistSiteRow>> ListPublishedAsync(string? slug, CancellationToken cancellationToken) => throw new NotSupportedException();

    public List<Artist> Rows { get; } = [];

    /// <summary>Thrown by the next save, then cleared.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>What <see cref="SlugExistsAsync"/> answers when set; otherwise it looks at <see cref="Rows"/>.</summary>
    public bool? SlugExists { get; set; }

    public void Add(Artist artist)
    {
        log.Add("add");
        Rows.Add(artist);
    }

    public Task<Artist?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken)
    {
        log.Add("slug-exists");
        return Task.FromResult(SlugExists ?? Rows.Any(row => row.Slug == slug && row.Id != exceptId));
    }

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Select(row => row.Slug).ToHashSet(StringComparer.Ordinal));

    public Task<List<ArtistSummaryServiceModel>> ListAsync(
        ArtistListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
        Task.FromResult(new List<ArtistSummaryServiceModel>());

    public Task<List<ArtistSummaryServiceModel>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Where(row => ids.Contains(row.Id)).Select(Summary).ToList());

    public Task<List<ArtistSummaryServiceModel>> ListBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
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

    internal static ArtistSummaryServiceModel Summary(Artist row) =>
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
internal sealed class FakeArtistsDataLayer : IArtistsDataLayer
{
    public Task<Artist?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<ArtistDeletedRow>> ListDeletedAsync(
        ArtistListPosition? after, int take, string? search, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ArtistWriteOutcome> DeleteAsync(
        Artist entity, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ArtistWriteOutcome> RestoreAsync(
        Artist entity, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<global::Arts6311.Domain.Modules.Artists.Managers.ArtistSiteRow>> ListPublishedAsync(string? slug, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Dictionary<Guid, Artist> Rows { get; } = [];

    public ArtistWriteOutcome Outcome { get; set; } = ArtistWriteOutcome.Saved;

    public bool SlugExists { get; set; }

    public List<string> Writes { get; } = [];

    public (PublishCommand Command, PublishStatus From)? LastTransition { get; private set; }

    public Task<Artist?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.GetValueOrDefault(id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(SlugExists);

    public Task<List<ArtistSummaryServiceModel>> ListAsync(
        ArtistListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
        Task.FromResult(new List<ArtistSummaryServiceModel>());

    public List<IReadOnlyCollection<Guid>> SummaryLookups { get; } = [];

    public Task<List<ArtistSummaryServiceModel>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        SummaryLookups.Add(ids);
        return Task.FromResult(Rows.Values.Where(row => ids.Contains(row.Id)).Select(FakeArtistsRepository.Summary).ToList());
    }

    public Task<List<ArtistSummaryServiceModel>> FindBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Values.Where(row => slugs.Contains(row.Slug)).Select(FakeArtistsRepository.Summary).ToList());

    public Task<ArtistWriteOutcome> CreateAsync(Artist artist, string actorUserId, CancellationToken cancellationToken)
    {
        Writes.Add("create");
        if (Outcome == ArtistWriteOutcome.Saved)
        {
            artist.RowVersion = Guid.NewGuid();
            Rows[artist.Id] = artist;
        }

        return Task.FromResult(Outcome);
    }

    public PublishedPathChange? LastPathChange { get; private set; }

    public Task<ArtistWriteOutcome> UpdateAsync(
        Artist artist, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken)
    {
        Writes.Add($"update:{summary}");
        LastPathChange = pathChange;
        return Task.FromResult(Outcome);
    }

    public Task<ArtistWriteOutcome> TransitionAsync(
        Artist artist, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
    {
        Writes.Add($"transition:{command}");
        LastTransition = (command, from);
        return Task.FromResult(Outcome);
    }

    public (PublishStatus From, string Reason)? LastAbandon { get; private set; }

    public Task<ArtistWriteOutcome> AbandonScheduleAsync(
        Artist artist, PublishStatus from, string reason, CancellationToken cancellationToken)
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

    public List<Artist> Seeded { get; } = [];

    public Task<int> AddSeedAsync(IReadOnlyList<Artist> artists, string actor, CancellationToken cancellationToken)
    {
        Seeded.AddRange(artists);
        return Task.FromResult(artists.Count);
    }

    public Artist Add(PublishStatus status = PublishStatus.Draft, Guid? portrait = null, DateTimeOffset? publishedAt = null)
    {
        var artist = new Artist
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
        Rows[artist.Id] = artist;
        return artist;
    }
}

/// <summary>Business that records what the facade asked and answers what the test set.</summary>
internal sealed class RecordingArtistsBusiness : IArtistsBusiness
{
    public Task<OperationResult<CursorPage<ArtistDeletedServiceModel>>> ListDeletedAsync(
        ArtistListViewModel request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult> DeleteAsync(
        Guid id, ContentReferenceCheck references, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<ArtistServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<global::Arts6311.Domain.Modules.Artists.Managers.ArtistSiteRow>> ListForSiteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<global::Arts6311.Domain.Modules.Artists.Managers.ArtistSiteRow?> GetForSiteAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Managers.Publishing.PreviewRecord<global::Arts6311.Domain.Modules.Artists.Managers.ArtistSiteRow>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<Guid, global::Arts6311.Domain.Modules.Artists.Managers.ServiceModels.SiteArtistCreditServiceModel>> DescribeCreditsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<global::Arts6311.Domain.Managers.Publishing.SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public static readonly ArtistServiceModel Artist = new(
        Guid.Parse("44444444-4444-4444-4444-444444444444"), "Jane Doe", "jane-doe", null, null, true, null,
        PublishStatus.Draft, null, null, false, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "editor-1",
        "\"0123456789abcdef0123456789abcdef\"");

    public List<string> Calls { get; } = [];

    public ArtistServiceModel Current { get; set; } = Artist;

    public (PublishCommand Command, ArtistPortraitCheck? Portrait)? LastTransition { get; private set; }

    public Task<OperationResult<CursorPage<ArtistSummaryServiceModel>>> ListAsync(ArtistListViewModel request, CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return Task.FromResult(OperationResult.Success(new CursorPage<ArtistSummaryServiceModel>([], null)));
    }

    public Task<OperationResult<ArtistServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Calls.Add("get");
        return Task.FromResult(id == Current.Id
            ? OperationResult.Success(Current)
            : OperationResult.Failure<ArtistServiceModel>(OperationError.Of(ErrorCodes.NotFound, "No.")));
    }

    public Task<IReadOnlyList<ArtistSummaryServiceModel>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        Calls.Add("summaries");
        return Task.FromResult<IReadOnlyList<ArtistSummaryServiceModel>>([]);
    }

    public Task<IReadOnlyList<ArtistSummaryServiceModel>> FindBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        Calls.Add("by-slugs");
        return Task.FromResult<IReadOnlyList<ArtistSummaryServiceModel>>([]);
    }

    public Task<OperationResult<ArtistServiceModel>> CreateAsync(ArtistCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add("create");
        return Task.FromResult(OperationResult.Success(Current));
    }

    public ArtistPortraitCheck? UpdatePortrait { get; private set; }

    public Task<OperationResult<ArtistServiceModel>> UpdateAsync(
        Guid id,
        ArtistUpdateViewModel update,
        string? ifMatch,
        ArtistPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("update");
        UpdatePortrait = portrait;
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<int> SeedAsync(IReadOnlyList<(string Name, string? Medium)> roster, string actor, CancellationToken cancellationToken) =>
        Task.FromResult(0);

    public Task<OperationResult<ArtistServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, ArtistPortraitCheck? portrait, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add($"transition:{command}");
        LastTransition = (command, portrait);
        return Task.FromResult(OperationResult.Success(Current));
    }

    public DateTimeOffset? LastScheduledFor { get; private set; }

    public ArtistPortraitCheck? SchedulePortrait { get; private set; }

    public Task<OperationResult<ArtistServiceModel>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        ArtistPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("schedule");
        LastScheduledFor = scheduledFor;
        SchedulePortrait = portrait;
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<ArtistServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add("unschedule");
        return Task.FromResult(OperationResult.Success(Current));
    }

    public IReadOnlyList<Guid> Due { get; set; } = [];

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        Calls.Add("list-due");
        return Task.FromResult(Due);
    }

    public ScheduledPublishOutcome ScheduledOutcome { get; set; } = ScheduledPublishOutcome.Published;

    public ArtistPortraitCheck? ScheduledPublishPortrait { get; private set; }

    public Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, ArtistPortraitCheck? portrait, CancellationToken cancellationToken)
    {
        Calls.Add("publish-scheduled");
        ScheduledPublishPortrait = portrait;
        return Task.FromResult(ScheduledOutcome);
    }
}

/// <summary>Media as far as Artists uses it: the attachability question. Everything else is out of scope here.</summary>
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
/// Artists as far as another module uses it: the lookups that Exhibitions, Shop and the seeders ask for. The CMS
/// routes are out of scope here.
/// </summary>
internal sealed class FakeArtistsFacade : IArtistsFacade
{
    public Task<OperationResult<CursorPage<ArtistDeletedServiceModel>>> ListDeletedAsync(
        ArtistListViewModel request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult> DeleteAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<ArtistServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<global::Arts6311.Domain.Modules.Artists.Managers.ServiceModels.SiteArtistServiceModel>> ListForSiteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<global::Arts6311.Domain.Modules.Artists.Managers.ServiceModels.SiteArtistServiceModel?> GetForSiteAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Managers.Publishing.PreviewRecord<global::Arts6311.Domain.Modules.Artists.Managers.ServiceModels.SiteArtistServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<Guid, global::Arts6311.Domain.Modules.Artists.Managers.ServiceModels.SiteArtistCreditServiceModel>> DescribeCreditsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        Lookups++;
        return Task.FromResult<IReadOnlyDictionary<Guid, global::Arts6311.Domain.Modules.Artists.Managers.ServiceModels.SiteArtistCreditServiceModel>>(ids
            .Distinct()
            .Where(Roster.ContainsKey)
            .ToDictionary(
                id => id,
                id => new global::Arts6311.Domain.Modules.Artists.Managers.ServiceModels.SiteArtistCreditServiceModel(
                    Roster[id].Name, Roster[id].Status == PublishStatus.Published ? Roster[id].Slug : null)));
    }

    public Task<IReadOnlyList<global::Arts6311.Domain.Managers.Publishing.SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Dictionary<Guid, ArtistSummaryServiceModel> Roster { get; } = [];

    public int Lookups { get; private set; }

    public ArtistSummaryServiceModel Add(string name, PublishStatus status = PublishStatus.Published)
    {
        var artist = new ArtistSummaryServiceModel(
            Guid.NewGuid(), name, name.ToLowerInvariant().Replace(' ', '-'), null, true, status, null, DateTimeOffset.UnixEpoch);
        Roster[artist.Id] = artist;
        return artist;
    }

    public Task<IReadOnlyList<ArtistSummaryServiceModel>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        Lookups++;
        return Task.FromResult<IReadOnlyList<ArtistSummaryServiceModel>>([.. ids.Distinct().Where(Roster.ContainsKey).Select(id => Roster[id])]);
    }

    public Task<IReadOnlyList<ArtistSummaryServiceModel>> FindBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        Lookups++;
        return Task.FromResult<IReadOnlyList<ArtistSummaryServiceModel>>([.. Roster.Values.Where(artist => slugs.Contains(artist.Slug))]);
    }

    public Task<OperationResult<CursorPage<ArtistSummaryServiceModel>>> ListAsync(ArtistListViewModel request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<ArtistServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<ArtistServiceModel>> CreateAsync(ArtistCreateViewModel create, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<ArtistServiceModel>> UpdateAsync(
        Guid id, ArtistUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<ArtistServiceModel>> PublishAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<ArtistServiceModel>> ScheduleAsync(
        Guid id, ContentScheduleViewModel schedule, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<ArtistServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<ArtistServiceModel>> UnpublishAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<ArtistServiceModel>> ArchiveAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
