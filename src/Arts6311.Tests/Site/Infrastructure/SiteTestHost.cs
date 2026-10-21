using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Revalidation;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Classes;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Instructors;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Memberships;
using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Pages;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Redirects;
using Arts6311.Domain.Modules.Shop;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Site;
using Arts6311.Tests.Kernel;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Site.Infrastructure;

/// <summary>
/// Every content module, the Site module and the revalidation handler as the API and the Worker compose them, over one
/// SQLite <see cref="ArtsDbContext"/> and one in-memory HybridCache, so a site read, a CMS write, the outbox and the
/// cache's invalidation all travel the real path (ADR 0042). Only the blob container is faked.
/// </summary>
/// <remarks>
/// The <c>Add*Async</c> helpers write rows straight into the tables, so a read test can set up any shape — a draft
/// beside a published record, a session already over — without going through each module's own rules, which their
/// modules' tests cover.
/// </remarks>
internal sealed class SiteTestHost : IAsyncDisposable
{
    public const string EditorId = "editor-1";

    private readonly SqliteArtsDatabase database;
    private readonly ServiceProvider services;
    private int next;

    private SiteTestHost(SqliteArtsDatabase database, ServiceProvider services, FakeTimeProvider time)
    {
        this.database = database;
        this.services = services;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    public DateTimeOffset Now => Time.GetUtcNow();

    public static async Task<SiteTestHost> CreateAsync(CancellationToken cancellationToken)
    {
        var database = await SqliteArtsDatabase.CreateAsync(cancellationToken);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddScoped(_ => database.NewContext());
        collection.AddArtsKernel();
        collection.AddMediaModule();
        collection.AddArtistsModule();
        collection.AddInstructorsModule();
        collection.AddExhibitionsModule();
        collection.AddClassesModule();
        collection.AddShopModule();
        collection.AddMembershipsModule();
        collection.AddPagesModule();
        collection.AddRedirectsModule();
        collection.AddSiteModule();

        // As the Worker composes it: the one handler that turns a committed change into cache invalidations.
        collection.AddArtsRevalidation();
        collection.Replace(ServiceDescriptor.Singleton<IMediaBlobGateway>(new FakeMediaBlobGateway()));

        return new SiteTestHost(database, collection.BuildServiceProvider(), time);
    }

    /// <summary>A context on the same database, for a host that serves it (the API under test).</summary>
    public ArtsDbContext NewContext() => database.NewContext();

    public async Task<T> WithAsync<TFacade, T>(Func<TFacade, Task<T>> action)
        where TFacade : notnull
    {
        using var scope = services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TFacade>());
    }

    /// <summary>One Worker pass, as the hosted service runs it: this is where a revalidation reaches the cache.</summary>
    public async Task<OutboxDispatchSummary> DispatchAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchDueAsync(cancellationToken);
    }

    public async Task ExecuteAsync(Func<ArtsDbContext, Task> action)
    {
        await using var context = database.NewContext();
        await action(context);
    }

    /// <summary>
    /// A library original with every rendition the site describes (or none, as before its Worker pass has run).
    /// </summary>
    public async Task<Guid> AddImageAsync(string altText, bool withRenditions = true)
    {
        var id = Guid.NewGuid();
        await AddAsync(context =>
        {
            context.Add(Asset(id, MediaAssetKind.Original, parent: null, renditionName: null, "image/jpeg", 1600, 1000, altText));
            if (withRenditions)
            {
                foreach (var rendition in MediaRenditionName.All)
                {
                    context.Add(Asset(
                        Guid.NewGuid(),
                        MediaAssetKind.Derived,
                        id,
                        rendition.ToString(),
                        rendition.Format == MediaImageFormat.Webp ? "image/webp" : "image/jpeg",
                        rendition.Width,
                        rendition.Width * 1000 / 1600,
                        altText: null));
                }
            }
        });
        return id;
    }

    public async Task<Guid> AddArtistAsync(
        string name,
        PublishStatus status = PublishStatus.Published,
        bool active = true,
        Guid? portrait = null,
        string? bio = null)
    {
        var artist = new Artist
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = Slugs.FromName(name),
            Medium = "Printmaking",
            Bio = bio,
            Active = active,
            PortraitMediaId = portrait,
            LegacyWordPressId = NextLegacyId(),
        };
        artist.Status = status;
        artist.PublishedAt = PublishedAtFor(status);
        artist.CreatedAt = Now;
        artist.UpdatedAt = Now;
        artist.UpdatedBy = EditorId;
        artist.RowVersion = Guid.NewGuid();
        await AddAsync(context => context.Add(artist));
        return artist.Id;
    }

    public async Task<Guid> AddPageAsync(string title, PublishStatus status = PublishStatus.Published, string? body = "<p>Text.</p>")
    {
        var page = new Page { Id = Guid.NewGuid(), Title = title, Slug = Slugs.FromName(title), Body = body, LegacyWordPressId = NextLegacyId() };
        page.Status = status;
        page.PublishedAt = PublishedAtFor(status);
        page.CreatedAt = Now;
        page.UpdatedAt = Now;
        page.UpdatedBy = EditorId;
        page.RowVersion = Guid.NewGuid();
        await AddAsync(context => context.Add(page));
        return page.Id;
    }

    /// <summary>An instructor, for a class run to name.</summary>
    public async Task<Guid> AddInstructorAsync(string name, PublishStatus status = PublishStatus.Published)
    {
        var instructor = new Instructor
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = Slugs.FromName(name),
            Active = true,
            LegacyWordPressId = NextLegacyId(),
        };
        instructor.Status = status;
        instructor.PublishedAt = PublishedAtFor(status);
        instructor.CreatedAt = Now;
        instructor.UpdatedAt = Now;
        instructor.UpdatedBy = EditorId;
        instructor.RowVersion = Guid.NewGuid();
        await AddAsync(context => context.Add(instructor));
        return instructor.Id;
    }

    /// <param name="runs">
    /// Each run's one session's start (it lasts two hours), Square's price and seats (null until Square has reported),
    /// and sold-out. Every run is taught by <paramref name="instructor"/>.
    /// </param>
    public Task<Guid> AddClassAsync(
        string title,
        PublishStatus status = PublishStatus.Published,
        Guid? hero = null,
        params (DateTimeOffset StartsAt, long? PriceCents, int? Seats, bool SoldOut)[] runs) =>
        AddClassAsync(title, instructor: null, status, hero, runs);

    /// <inheritdoc cref="AddClassAsync(string, PublishStatus, Guid?, ValueTuple{DateTimeOffset, long?, int?, bool}[])"/>
    public async Task<Guid> AddClassAsync(
        string title,
        Guid? instructor,
        PublishStatus status = PublishStatus.Published,
        Guid? hero = null,
        params (DateTimeOffset StartsAt, long? PriceCents, int? Seats, bool SoldOut)[] runs)
    {
        var number = Interlocked.Increment(ref next);
        var id = Guid.NewGuid();
        var offering = new ClassOffering
        {
            Id = id,
            Title = title,
            Slug = Slugs.FromName(title),
            Summary = $"About {title}.",
            Body = "<p>What you will make.</p>",
            ClassType = "Printmaking",
            Level = "Beginner",
            OfferingNumber = number,
            HeroMediaId = hero,
            LegacyWordPressId = NextLegacyId(),
            Runs =
            [
                .. runs.Select((spec, index) =>
                {
                    var runId = Guid.NewGuid();
                    return new ClassRun
                    {
                        Id = runId,
                        ClassOfferingId = id,
                        InstructorId = instructor,
                        Cadence = "Saturdays",
                        DisplayTimeZone = "America/Chicago",
                        Sku = $"CLS-{number}-{index + 1}",
                        Capacity = 10,
                        StartingPriceCents = 99_00,
                        PriceCents = spec.PriceCents,
                        SeatsRemaining = spec.Seats,
                        SoldOut = spec.SoldOut,
                        Sessions =
                        [
                            new ClassSession
                            {
                                Id = Guid.NewGuid(),
                                ClassRunId = runId,
                                StartsAt = spec.StartsAt,
                                EndsAt = spec.StartsAt.AddHours(2),
                            },
                        ],
                    };
                }),
            ],
        };
        offering.Status = status;
        offering.PublishedAt = PublishedAtFor(status);
        offering.CreatedAt = Now;
        offering.UpdatedAt = Now;
        offering.UpdatedBy = EditorId;
        offering.RowVersion = Guid.NewGuid();
        await AddAsync(context => context.Add(offering));
        return id;
    }

    public async Task<Guid> AddExhibitionAsync(
        string title,
        DateOnly startDate,
        PublishStatus status = PublishStatus.Published,
        Guid? hero = null,
        params Guid[] artists)
    {
        var id = Guid.NewGuid();
        var exhibition = new Exhibition
        {
            Id = id,
            Title = title,
            Slug = Slugs.FromName(title),
            Summary = $"About {title}.",
            Body = "First paragraph.\n\nSecond paragraph.",
            StartDate = startDate,
            EndDate = startDate.AddDays(30),
            ReceptionStartsAt = new DateTimeOffset(startDate.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero),
            ReceptionEndsAt = new DateTimeOffset(startDate.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero).AddHours(3),
            ReceptionNote = "Light refreshments",
            HeroMediaId = hero,
            LegacyWordPressId = NextLegacyId(),
            Artists = [.. artists.Select((artist, index) => new ExhibitionArtist { ExhibitionId = id, ArtistId = artist, Position = index })],
        };
        exhibition.Status = status;
        exhibition.PublishedAt = PublishedAtFor(status);
        exhibition.CreatedAt = Now;
        exhibition.UpdatedAt = Now;
        exhibition.UpdatedBy = EditorId;
        exhibition.RowVersion = Guid.NewGuid();
        await AddAsync(context => context.Add(exhibition));
        return id;
    }

    /// <param name="publishedAt">The first publish, which "newest" orders by; defaults to now.</param>
    public async Task<Guid> AddShopItemAsync(
        string title,
        PublishStatus status = PublishStatus.Published,
        Guid? artistId = null,
        long? priceCents = 125_00,
        DateTimeOffset? publishedAt = null,
        string[]? categories = null,
        params Guid[] images)
    {
        var number = Interlocked.Increment(ref next);
        var id = Guid.NewGuid();
        var item = new ShopItem
        {
            Id = id,
            Title = title,
            Slug = Slugs.FromName(title),
            Description = "<p>A piece.</p>",
            ArtistId = artistId,
            Sku = $"ART-{number}",
            ItemNumber = number,
            OneOfAKind = true,
            StartingPriceCents = 99_00,
            StartingStockQuantity = 1,
            PriceCents = priceCents,
            StockOnHand = priceCents is null ? null : 1,
            LegacyWordPressId = NextLegacyId(),
            Categories = [.. (categories ?? ["prints"]).Select(category => new ShopItemCategory { ShopItemId = id, Category = category })],
            Images = [.. images.Select((image, index) => new ShopItemImage { ShopItemId = id, MediaAssetId = image, Position = index })],
        };
        item.Status = status;
        item.PublishedAt = PublishedAtFor(status);
        item.CreatedAt = Now;
        item.UpdatedAt = Now;
        item.UpdatedBy = EditorId;
        item.RowVersion = Guid.NewGuid();
        if (status == PublishStatus.Published)
        {
            item.PublishedAt = publishedAt ?? Now;
        }

        await AddAsync(context => context.Add(item));
        return id;
    }

    public async Task<Guid> AddPlanAsync(
        string name,
        int displayOrder,
        PublishStatus status = PublishStatus.Published,
        long? priceCents = 60_00,
        string? cadence = "ANNUAL")
    {
        var id = Guid.NewGuid();
        var plan = new MembershipPlan
        {
            Id = id,
            Name = name,
            Slug = Slugs.FromName(name),
            DiscountPercent = 10,
            DisplayOrder = displayOrder,
            StartingPriceCents = 50_00,
            PriceCents = priceCents,
            Cadence = cadence,
            LegacyWordPressId = NextLegacyId(),
            Perks = [new MembershipPlanPerk { MembershipPlanId = id, Position = 0, Text = "Class discount" }],
        };
        plan.Status = status;
        plan.PublishedAt = PublishedAtFor(status);
        plan.CreatedAt = Now;
        plan.UpdatedAt = Now;
        plan.UpdatedBy = EditorId;
        plan.RowVersion = Guid.NewGuid();
        await AddAsync(context => context.Add(plan));
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }

    private MediaAsset Asset(
        Guid id, MediaAssetKind kind, Guid? parent, string? renditionName, string mimeType, int width, int height, string? altText) => new()
    {
        Id = id,
        Kind = kind,
        ParentAssetId = parent,
        RenditionName = renditionName,
        FileName = kind == MediaAssetKind.Derived ? null : "piece.jpg",
        MimeType = mimeType,
        Bytes = 1000,
        Width = width,
        Height = height,
        Checksum = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        BlobKey = $"{parent ?? id}/{renditionName ?? "original.jpg"}",
        ProcessingStatus = MediaProcessingStatus.Ready,
        Visibility = MediaVisibility.Draft,
        AltText = altText,
        UploadedBy = EditorId,
        UploadedAt = Now,
        UpdatedAt = Now,
        RowVersion = Guid.NewGuid(),
    };

    /// <summary>The first publish, as a CMS write would leave it: set once a record has been published, kept when archived.</summary>
    private DateTimeOffset? PublishedAtFor(PublishStatus status) =>
        status is PublishStatus.Draft or PublishStatus.Scheduled ? null : Now;

    /// <summary>
    /// A WordPress id for provenance: unique per type, and set on every row so a test can prove the site never shows
    /// it.
    /// </summary>
    private long NextLegacyId() => 7000 + Interlocked.Increment(ref next);

    private async Task AddAsync(Action<ArtsDbContext> add)
    {
        await using var context = database.NewContext();
        add(context);
        await context.SaveChangesAsync();
    }
}
