using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Shop.Business;
using Arts6311.Domain.Modules.Shop.Data;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Data.Repositories;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using Arts6311.Tests.Artists.Infrastructure;

namespace Arts6311.Tests.Shop.Infrastructure;

/// <summary>A repository over a list, whose save can be told to fail the way EF fails.</summary>
internal sealed class FakeShopRepository(CallLog log) : IShopRepository
{
    public List<ShopItem> Rows { get; } = [];

    /// <summary>Thrown by the next save, then cleared.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>What <see cref="SlugExistsAsync"/> answers when set; otherwise it looks at <see cref="Rows"/>.</summary>
    public bool? SlugExists { get; set; }

    public int NextItemNumber { get; set; } = 1;

    public void Add(ShopItem item)
    {
        log.Add("add");
        Rows.Add(item);
    }

    public Task<ShopItem?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken)
    {
        log.Add("slug-exists");
        return Task.FromResult(SlugExists ?? Rows.Any(row => row.Slug == slug && row.Id != exceptId));
    }

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Select(row => row.Slug).ToHashSet(StringComparer.Ordinal));

    public Task<int> NextItemNumberAsync(CancellationToken cancellationToken) => Task.FromResult(NextItemNumber);

    public Task<List<ShopItemListRow>> ListAsync(
        ShopItemListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        string? category,
        CancellationToken cancellationToken) =>
        Task.FromResult(new List<ShopItemListRow>());

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
internal sealed class FakeShopDataLayer : IShopDataLayer
{
    public Dictionary<Guid, ShopItem> Rows { get; } = [];

    public Dictionary<string, SkuOwner> SkuOwners { get; } = new(StringComparer.Ordinal);

    public ShopWriteOutcome Outcome { get; set; } = ShopWriteOutcome.Saved;

    public bool SlugExists { get; set; }

    public int NextItemNumber { get; set; } = 17;

    public List<string> Writes { get; } = [];

    public SkuChanges? LastSkuChanges { get; private set; }

    public (PublishCommand Command, PublishStatus From)? LastTransition { get; private set; }

    public (PublishStatus From, string Reason)? LastAbandon { get; private set; }

    public List<ShopItem> Seeded { get; } = [];

    public Task<ShopItem?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.GetValueOrDefault(id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(SlugExists);

    public Task<IReadOnlyDictionary<string, SkuOwner>> SkuOwnersAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, SkuOwner>>(
            skus.Where(SkuOwners.ContainsKey).Distinct(StringComparer.Ordinal)
                .ToDictionary(sku => sku, sku => SkuOwners[sku], StringComparer.Ordinal));

    public Task<int> NextItemNumberAsync(CancellationToken cancellationToken) => Task.FromResult(NextItemNumber);

    public Task<List<ShopItemListRow>> ListAsync(
        ShopItemListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        string? category,
        CancellationToken cancellationToken) =>
        Task.FromResult(new List<ShopItemListRow>());

    public Task<ShopWriteOutcome> CreateAsync(
        ShopItem item, SkuChanges skus, string actorUserId, string summary, CancellationToken cancellationToken)
    {
        Writes.Add($"create:{summary}");
        LastSkuChanges = skus;
        if (Outcome == ShopWriteOutcome.Saved)
        {
            item.RowVersion = Guid.NewGuid();
            Rows[item.Id] = item;
        }

        return Task.FromResult(Outcome);
    }

    public Task<ShopWriteOutcome> UpdateAsync(
        ShopItem item,
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

    public Task<ShopWriteOutcome> TransitionAsync(
        ShopItem item, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
    {
        Writes.Add($"transition:{command}");
        LastTransition = (command, from);
        return Task.FromResult(Outcome);
    }

    public Task<ShopWriteOutcome> AbandonScheduleAsync(
        ShopItem item, PublishStatus from, string reason, CancellationToken cancellationToken)
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
        IReadOnlyList<ShopItem> items, SkuChanges skus, string actor, CancellationToken cancellationToken)
    {
        Seeded.AddRange(items);
        LastSkuChanges = skus;
        return Task.FromResult(items.Count);
    }

    /// <summary>One item in the store, with a photograph when one is given.</summary>
    public ShopItem Add(
        PublishStatus status = PublishStatus.Draft,
        Guid? photograph = null,
        DateTimeOffset? publishedAt = null,
        string? description = "<p>A piece.</p>",
        long? startingPriceCents = 12500,
        int? startingStockQuantity = 1,
        bool oneOfAKind = true,
        string sku = "ART-9")
    {
        var item = new ShopItem
        {
            Id = Guid.NewGuid(),
            Title = "Copper Cuff",
            Slug = "copper-cuff",
            Description = description,
            Sku = sku,
            ItemNumber = 9,
            OneOfAKind = oneOfAKind,
            StartingPriceCents = startingPriceCents,
            StartingStockQuantity = startingStockQuantity,
            Status = status,
            PublishedAt = publishedAt,
            UpdatedBy = "editor-1",
            RowVersion = Guid.NewGuid(),
        };

        if (photograph is { } image)
        {
            item.Images.Add(new ShopItemImage { ShopItemId = item.Id, MediaAssetId = image, Position = 0 });
        }

        SkuOwners[sku] = new SkuOwner(CatalogOwnerTypes.ShopItem, item.Id);
        Rows[item.Id] = item;
        return item;
    }
}

/// <summary>Business that records what the facade asked and answers what the test set.</summary>
internal sealed class RecordingShopBusiness : IShopBusiness
{
    public static readonly ShopItemDetail Item = new(
        Guid.Parse("77777777-7777-7777-7777-777777777777"),
        "Copper Cuff",
        "copper-cuff",
        "<p>A forged copper cuff.</p>",
        null,
        ["jewelry"],
        [],
        [],
        "ART-9",
        9,
        OneOfAKind: true,
        StartingPriceCents: 11000,
        StartingStockQuantity: 1,
        CatalogPolicy.Currency,
        StartingPriceEditable: true,
        StartingStockEditable: true,
        PriceCents: null,
        StockOnHand: null,
        SoldOut: false,
        SquareHasRecord: false,
        PublishStatus.Draft,
        null,
        null,
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        "editor-1",
        EntityTag.Format(Guid.Parse("0123456789abcdef0123456789abcdef")));

    public List<string> Calls { get; } = [];

    public ShopItemDetail Current { get; set; } = Item;

    public ShopItemMediaCheck? CreateMedia { get; private set; }

    public ShopItemMediaCheck? UpdateMedia { get; private set; }

    public (PublishCommand Command, ShopItemMediaCheck? Media)? LastTransition { get; private set; }

    public DateTimeOffset? LastScheduledFor { get; private set; }

    public ShopItemMediaCheck? ScheduleMedia { get; private set; }

    public ShopItemMediaCheck? ScheduledPublishMedia { get; private set; }

    public IReadOnlyList<Guid> Due { get; set; } = [];

    public ScheduledPublishOutcome ScheduledOutcome { get; set; } = ScheduledPublishOutcome.Published;

    /// <summary>The rows the list answers with, so the facade's artist completion can be seen.</summary>
    public List<ShopItemListRow> Page { get; } = [];

    public Task<OperationResult<CursorPage<ShopItemListRow>>> ListAsync(
        ShopItemListViewModel request, CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return Task.FromResult(OperationResult.Success(new CursorPage<ShopItemListRow>(Page, null)));
    }

    public Task<OperationResult<ShopItemDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Calls.Add("get");
        return Task.FromResult(id == Current.Id
            ? OperationResult.Success(Current)
            : OperationResult.Failure<ShopItemDetail>(OperationError.Of(ErrorCodes.NotFound, "No.")));
    }

    public Task<OperationResult<ShopItemDetail>> CreateAsync(
        ShopItemCreateViewModel create, ShopItemMediaCheck media, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add("create");
        CreateMedia = media;
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<ShopItemDetail>> UpdateAsync(
        Guid id,
        ShopItemUpdateViewModel update,
        string? ifMatch,
        ShopItemMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("update");
        UpdateMedia = media;
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<ShopItemDetail>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ShopItemMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add($"transition:{command}");
        LastTransition = (command, media);
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<ShopItemDetail>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        ShopItemMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("schedule");
        LastScheduledFor = scheduledFor;
        ScheduleMedia = media;
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<ShopItemDetail>> UnscheduleAsync(
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
        Guid id, DateTimeOffset asOf, ShopItemMediaCheck? media, CancellationToken cancellationToken)
    {
        Calls.Add("publish-scheduled");
        ScheduledPublishMedia = media;
        return Task.FromResult(ScheduledOutcome);
    }

    public Task<int> SeedAsync(IReadOnlyList<ShopItemSeed> seeds, string actor, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
