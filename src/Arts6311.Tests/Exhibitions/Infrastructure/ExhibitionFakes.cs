using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Domain.Modules.Exhibitions.Business;
using Arts6311.Domain.Modules.Exhibitions.Data;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Data.Repositories;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;
using Arts6311.Tests.Artists.Infrastructure;

namespace Arts6311.Tests.Exhibitions.Infrastructure;

/// <summary>A repository over a list, whose save can be told to fail the way EF fails.</summary>
internal sealed class FakeExhibitionsRepository(CallLog log) : IExhibitionsRepository
{
    public List<Exhibition> Rows { get; } = [];

    /// <summary>Thrown by the next save, then cleared.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>What <see cref="SlugExistsAsync"/> answers when set; otherwise it looks at <see cref="Rows"/>.</summary>
    public bool? SlugExists { get; set; }

    public void Add(Exhibition exhibition)
    {
        log.Add("add");
        Rows.Add(exhibition);
    }

    public Task<Exhibition?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken)
    {
        log.Add("slug-exists");
        return Task.FromResult(SlugExists ?? Rows.Any(row => row.Slug == slug && row.Id != exceptId));
    }

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Select(row => row.Slug).ToHashSet(StringComparer.Ordinal));

    public Task<List<ExhibitionSummaryServiceModel>> ListAsync(
        ExhibitionListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
        Task.FromResult(new List<ExhibitionSummaryServiceModel>());

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
internal sealed class FakeExhibitionsDataLayer : IExhibitionsDataLayer
{
    public Dictionary<Guid, Exhibition> Rows { get; } = [];

    public ExhibitionWriteOutcome Outcome { get; set; } = ExhibitionWriteOutcome.Saved;

    public bool SlugExists { get; set; }

    public List<string> Writes { get; } = [];

    public (PublishCommand Command, PublishStatus From)? LastTransition { get; private set; }

    public List<Exhibition> Seeded { get; } = [];

    public Task<Exhibition?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.GetValueOrDefault(id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(SlugExists);

    public Task<List<ExhibitionSummaryServiceModel>> ListAsync(
        ExhibitionListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
        Task.FromResult(new List<ExhibitionSummaryServiceModel>());

    public Task<ExhibitionWriteOutcome> CreateAsync(Exhibition exhibition, string actorUserId, CancellationToken cancellationToken)
    {
        Writes.Add("create");
        if (Outcome == ExhibitionWriteOutcome.Saved)
        {
            exhibition.RowVersion = Guid.NewGuid();
            Rows[exhibition.Id] = exhibition;
        }

        return Task.FromResult(Outcome);
    }

    public Task<ExhibitionWriteOutcome> UpdateAsync(
        Exhibition exhibition, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken)
    {
        Writes.Add($"update:{summary}");
        return Task.FromResult(Outcome);
    }

    public Task<ExhibitionWriteOutcome> TransitionAsync(
        Exhibition exhibition, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
    {
        Writes.Add($"transition:{command}");
        LastTransition = (command, from);
        return Task.FromResult(Outcome);
    }

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Values.Select(row => row.Slug).ToHashSet(StringComparer.Ordinal));

    public Task<int> AddSeedAsync(IReadOnlyList<Exhibition> exhibitions, string actor, CancellationToken cancellationToken)
    {
        Seeded.AddRange(exhibitions);
        return Task.FromResult(exhibitions.Count);
    }

    public Exhibition Add(
        PublishStatus status = PublishStatus.Draft,
        Guid? hero = null,
        DateTimeOffset? publishedAt = null,
        string? summary = "A show.",
        params Guid[] artistIds)
    {
        var exhibition = new Exhibition
        {
            Id = Guid.NewGuid(),
            Title = "Boats & Pianos",
            Slug = "boats-pianos",
            Summary = summary,
            StartDate = new DateOnly(2026, 10, 24),
            EndDate = new DateOnly(2026, 12, 5),
            Status = status,
            HeroMediaId = hero,
            PublishedAt = publishedAt,
            UpdatedBy = "editor-1",
            RowVersion = Guid.NewGuid(),
        };
        exhibition.Artists = [.. artistIds.Select((id, position) => new ExhibitionArtist { ExhibitionId = exhibition.Id, ArtistId = id, Position = position })];
        Rows[exhibition.Id] = exhibition;
        return exhibition;
    }
}

/// <summary>Business that records what the facade asked and answers what the test set.</summary>
internal sealed class RecordingExhibitionsBusiness : IExhibitionsBusiness
{
    public static readonly ExhibitionDetail Exhibition = new(
        Guid.Parse("55555555-5555-5555-5555-555555555555"), "Boats & Pianos", "boats-pianos", "A show.", null,
        new DateOnly(2026, 10, 24), new DateOnly(2026, 12, 5), null, null, [], PublishStatus.Draft, null, null, null,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "editor-1", EntityTag.Format(Guid.Parse("0123456789abcdef0123456789abcdef")));

    public List<string> Calls { get; } = [];

    public ExhibitionDetail Current { get; set; } = Exhibition;

    public ExhibitionHeroCheck? UpdateHero { get; private set; }

    public (PublishCommand Command, ExhibitionHeroCheck? Hero)? LastTransition { get; private set; }

    public Task<OperationResult<CursorPage<ExhibitionSummaryServiceModel>>> ListAsync(
        ExhibitionListViewModel request, CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return Task.FromResult(OperationResult.Success(new CursorPage<ExhibitionSummaryServiceModel>([], null)));
    }

    public Task<OperationResult<ExhibitionDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Calls.Add("get");
        return Task.FromResult(id == Current.Id
            ? OperationResult.Success(Current)
            : OperationResult.Failure<ExhibitionDetail>(OperationError.Of(ErrorCodes.NotFound, "No.")));
    }

    public Task<OperationResult<ExhibitionDetail>> CreateAsync(
        ExhibitionCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add("create");
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<ExhibitionDetail>> UpdateAsync(
        Guid id,
        ExhibitionUpdateViewModel update,
        string? ifMatch,
        ExhibitionHeroCheck? hero,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("update");
        UpdateHero = hero;
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<ExhibitionDetail>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ExhibitionHeroCheck? hero,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add($"transition:{command}");
        LastTransition = (command, hero);
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<int> SeedAsync(IReadOnlyList<ExhibitionSeed> seeds, string actor, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}

/// <summary>Artists as far as Exhibitions uses it: the lookups. The CMS routes are out of scope here.</summary>
internal sealed class FakeArtistsFacade : IArtistsFacade
{
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

    public Task<OperationResult<ArtistServiceModel>> UnpublishAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<ArtistServiceModel>> ArchiveAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
