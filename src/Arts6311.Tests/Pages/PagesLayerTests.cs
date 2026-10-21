using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Html;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Pages.Business;
using Arts6311.Domain.Modules.Pages.Data;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Data.Repositories;
using Arts6311.Domain.Modules.Pages.Facade;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Managers.Validation;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;
using Arts6311.Tests.Artists.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Pages;

/// <summary>
/// Each layer against fakes of the one below (backend.md "Testing"): the DataLayer's call order and failure mapping,
/// Business's sanitize-on-write rules, and the facade's validation and Media hand-off.
/// </summary>
public sealed class PagesLayerTests
{
    private const string AnyTag = "\"0123456789abcdef0123456789abcdef\"";

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid ImageId = Guid.Parse("0199f0a0-0000-7000-8000-0000000000aa");

    private static readonly string Img = $"<img src=\"/media/{ImageId}/800.webp\" alt=\"Window\">";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- DataLayer ----

    [Fact]
    public async Task TheDataLayerAddsAuditsAndSavesOnceInThatOrder()
    {
        var (dataLayer, repository, audit, log) = DataLayer();

        await dataLayer.CreateAsync(NewPage(), "editor-1", "Created as Draft.", Ct);
        await dataLayer.TransitionAsync(NewPage(PublishStatus.Archived), PublishCommand.Archive, PublishStatus.Draft, "admin-1", Ct);

        // The transition's revalidation is staged before its audit row and both before its one save (ADR 0036).
        Assert.Equal(
            ["add", "audit:page.created", "save", "outbox:site.revalidate-requested", "audit:page.archived", "save"],
            log.Calls);
        Assert.Equal(("status:Draft", "status:Archived"), (audit.Entries[^1].BeforeReference, audit.Entries[^1].AfterReference));
        Assert.Single(repository.Rows);
    }

    [Fact]
    public async Task APublishedSlugChangeIsStagedBeforeTheOneSave()
    {
        var (dataLayer, _, _, log) = DataLayer();

        await dataLayer.UpdateAsync(NewPage(PublishStatus.Published), "editor-1", "Updated: slug.", new PublishedPathChange("/terms", "/terms-of-use"), Ct);

        Assert.Equal(
            ["outbox:redirects.slug-changed", "outbox:site.revalidate-requested", "audit:page.updated", "save"],
            log.Calls);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("signin")]
    [InlineData("artists")]
    public async Task APageCannotTakeAnAddressTheSiteAlreadyAnswers(string slug)
    {
        var (business, dataLayer) = Business();
        var page = dataLayer.Add(NewPage());

        var created = await business.CreateAsync(new PageCreateViewModel("Whatever", Slug: slug), PageImageCheck.None, "e", Ct);
        var fromTitle = await business.CreateAsync(new PageCreateViewModel(slug.ToUpperInvariant()), PageImageCheck.None, "e", Ct);
        var renamed = await business.UpdateAsync(page.Id, new PageUpdateViewModel(Slug: slug), EntityTag.Format(page.RowVersion), null, "e", Ct);

        Assert.Contains("slug", created.Error!.FieldErrors.Keys);
        Assert.Contains("slug", fromTitle.Error!.FieldErrors.Keys);
        Assert.Contains("slug", renamed.Error!.FieldErrors.Keys);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task AConcurrencyFailureIsAConflictAndOnlyARealSlugClashIsATakenSlug()
    {
        var (dataLayer, repository, _, log) = DataLayer();

        repository.SaveFailure = new DbUpdateConcurrencyException("stale");
        Assert.Equal(PageWriteOutcome.Conflict, await dataLayer.UpdateAsync(NewPage(), "e", "Updated.", pathChange: null, Ct));
        Assert.Equal("discard", log.Calls[^1]);

        repository.SaveFailure = new DbUpdateException("unique");
        repository.SlugExists = true;
        Assert.Equal(PageWriteOutcome.SlugTaken, await dataLayer.CreateAsync(NewPage(), "e", "Created.", Ct));

        // A picture deleted since Media was asked fails the Restrict key: a fault, not a refusal.
        repository.SaveFailure = new DbUpdateException("foreign key");
        repository.SlugExists = false;
        await Assert.ThrowsAsync<DbUpdateException>(() => dataLayer.CreateAsync(NewPage(), "e", "Created.", Ct));
    }

    // ---- Business ----

    [Fact]
    public async Task BusinessSanitizesItselfWhateverTheFacadeDid()
    {
        var (business, dataLayer) = Business();

        var result = await business.CreateAsync(
            new PageCreateViewModel("About", Body: "<p onclick=\"x()\">Hi</p><script>x()</script>"), PageImageCheck.None, "e", Ct);

        Assert.Equal("<p>Hi</p>", result.Value!.Body);
        Assert.Contains("Sanitizer removed", dataLayer.Summaries.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BusinessRefusesAPictureMediaWasNotAskedAboutOrDoesNotHave()
    {
        var (business, dataLayer) = Business();
        var body = $"<p>{Img}</p>";

        var notAsked = await business.CreateAsync(new PageCreateViewModel("About", Body: body), PageImageCheck.None, "e", Ct);
        var missing = await business.CreateAsync(
            new PageCreateViewModel("About", Body: body), new PageImageCheck(new HashSet<Guid> { ImageId }, new HashSet<Guid> { ImageId }), "e", Ct);
        var held = await business.CreateAsync(
            new PageCreateViewModel("About", Body: body), new PageImageCheck(new HashSet<Guid> { ImageId }, new HashSet<Guid>()), "e", Ct);

        Assert.Contains("body", notAsked.Error!.FieldErrors.Keys);
        Assert.Contains("body", missing.Error!.FieldErrors.Keys);
        Assert.Equal([ImageId], held.Value!.ImageMediaIds);
        Assert.Equal(["create"], dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessRefusesABodyTooLongOnceSanitized()
    {
        var (business, dataLayer) = Business();
        var body = string.Concat(Enumerable.Repeat("<p>" + new string('x', 996) + "</p>", 101));

        var result = await business.CreateAsync(new PageCreateViewModel("Long", Body: body), PageImageCheck.None, "e", Ct);

        Assert.Contains("body", result.Error!.FieldErrors.Keys);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessChecksTheVersionBeforeAnyRuleAndRefusesPublishingAnEmptyPage()
    {
        var (business, dataLayer) = Business();
        var archived = dataLayer.Add(NewPage(PublishStatus.Archived));
        var empty = dataLayer.Add(NewPage(body: null));

        var stale = await business.TransitionAsync(archived.Id, PublishCommand.Publish, EntityTag.Format(Guid.NewGuid()), "e", Ct);
        var publishEmpty = await business.TransitionAsync(empty.Id, PublishCommand.Publish, EntityTag.Format(empty.RowVersion), "e", Ct);

        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.Contains("body", publishEmpty.Error!.FieldErrors.Keys);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task TheSeedAddsOnlyMissingSlugsAsSanitizedDrafts()
    {
        var (business, dataLayer) = Business();
        dataLayer.Add(NewPage(slug: "privacy"));

        var added = await business.SeedAsync(
            [new PagePlaceholder("Privacy Policy", "privacy", "<p>P</p>"), new PagePlaceholder("Terms", "terms", "<p onclick=\"x\">T</p>")],
            "system:test",
            Ct);

        Assert.Equal(1, added);
        var terms = Assert.Single(dataLayer.Seeded);
        Assert.Equal(("terms", PublishStatus.Draft, "<p>T</p>"), (terms.Slug, terms.Status, terms.Body));
    }

    // ---- Facade ----

    [Fact]
    public async Task TheFacadeAsksMediaOnlyAboutTheLibraryPicturesASanitizedBodyShows()
    {
        var (facade, business, media) = Facade();

        await facade.CreateAsync(new PageCreateViewModel("About", Body: "<p>No pictures.</p><img src=\"https://x.example/a.jpg\">"), "e", Ct);
        Assert.Equal(0, media.Checks);
        Assert.Equal(PageImageCheck.None, business.LastImages);

        media.Missing.Add(ImageId);
        await facade.CreateAsync(new PageCreateViewModel("About", Body: $"<p>{Img}</p>"), "e", Ct);

        Assert.Equal(1, media.Checks);
        Assert.Equal([ImageId], business.LastImages!.Asked);
        Assert.Equal([ImageId], business.LastImages.Missing);
    }

    [Fact]
    public async Task AnEditWithoutABodyAsksMediaNothing()
    {
        var (facade, business, media) = Facade();

        await facade.UpdateAsync(Guid.NewGuid(), new PageUpdateViewModel(Title: "New"), AnyTag, "e", Ct);

        Assert.Equal(0, media.Checks);
        Assert.Null(business.LastImages);
    }

    [Fact]
    public async Task EveryWriteWithoutIfMatchIsA428BeforeAnythingIsAsked()
    {
        var (facade, business, media) = Facade();

        var results = new[]
        {
            await facade.UpdateAsync(Guid.NewGuid(), new PageUpdateViewModel(Body: $"<p>{Img}</p>"), null, "e", Ct),
            await facade.PublishAsync(Guid.NewGuid(), null, "e", Ct),
            await facade.UnpublishAsync(Guid.NewGuid(), " ", "e", Ct),
            await facade.ArchiveAsync(Guid.NewGuid(), null, "admin-1", Ct),
        };

        Assert.All(results, result => Assert.Equal(ErrorCodes.PreconditionRequired, result.Error!.Code));
        Assert.Empty(business.Calls);
        Assert.Equal(0, media.Checks);
    }

    [Fact]
    public async Task AnInvalidRequestStopsAtTheValidator()
    {
        var (facade, business, media) = Facade();

        var result = await facade.CreateAsync(new PageCreateViewModel(" ", Body: $"<p>{Img}</p>"), "e", Ct);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Empty(business.Calls);
        Assert.Equal(0, media.Checks);
    }

    private static Page NewPage(PublishStatus status = PublishStatus.Draft, string? body = "<p>Body.</p>", string slug = "about") => new()
    {
        Id = Guid.NewGuid(),
        Title = "About",
        Slug = slug,
        Body = body,
        Status = status,
        UpdatedBy = "editor-1",
        RowVersion = Guid.NewGuid(),
    };

    private static (PagesDataLayer, FakePagesRepository, RecordingAuditWriter, CallLog) DataLayer()
    {
        var log = new CallLog();
        var repository = new FakePagesRepository(log);
        var audit = new RecordingAuditWriter(log);
        return (new PagesDataLayer(repository, audit, new RecordingOutboxWriter(log), new FixedCorrelation(), new FixedClock(Now)), repository, audit, log);
    }

    private static (PagesBusiness, FakePagesDataLayer) Business()
    {
        var dataLayer = new FakePagesDataLayer();
        return (new PagesBusiness(dataLayer, new AllowListHtmlSanitizer(), new FixedClock(Now)), dataLayer);
    }

    private static (PagesFacade, RecordingPagesBusiness, FakeMediaFacade) Facade()
    {
        var business = new RecordingPagesBusiness();
        var media = new FakeMediaFacade();
        var facade = new PagesFacade(
            business, media, new AllowListHtmlSanitizer(), new PageCreateValidator(), new PageUpdateValidator(),
            new PageListValidator(), new ContentScheduleValidator());
        return (facade, business, media);
    }

    private sealed class FakePagesRepository(CallLog log) : IPagesRepository
    {
        public List<Page> Rows { get; } = [];

        public Exception? SaveFailure { get; set; }

        public bool SlugExists { get; set; }

        public void Add(Page page)
        {
            log.Add("add");
            Rows.Add(page);
        }

        public Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

        public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) => Task.FromResult(SlugExists);

        public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) => Task.FromResult(new HashSet<string>());

        public Task<List<PageSummaryServiceModel>> ListAsync(
            PageListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
            Task.FromResult(new List<PageSummaryServiceModel>());

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

    private sealed class FakePagesDataLayer : IPagesDataLayer
    {
        private readonly Dictionary<Guid, Page> rows = [];

        public List<string> Writes { get; } = [];

        public List<string> Summaries { get; } = [];

        public List<Page> Seeded { get; } = [];

        public Page Add(Page page)
        {
            rows[page.Id] = page;
            return page;
        }

        public Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(rows.GetValueOrDefault(id));

        public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
            Task.FromResult(rows.Values.Any(row => row.Slug == slug && row.Id != exceptId));

        public Task<List<PageSummaryServiceModel>> ListAsync(
            PageListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
            Task.FromResult(new List<PageSummaryServiceModel>());

        public Task<PageWriteOutcome> CreateAsync(Page page, string actorUserId, string summary, CancellationToken cancellationToken)
        {
            Writes.Add("create");
            Summaries.Add(summary);
            return Task.FromResult(PageWriteOutcome.Saved);
        }

        public Task<PageWriteOutcome> UpdateAsync(
            Page page, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken)
        {
            Writes.Add("update");
            Summaries.Add(summary);
            return Task.FromResult(PageWriteOutcome.Saved);
        }

        public Task<PageWriteOutcome> TransitionAsync(
            Page page, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
        {
            Writes.Add($"transition:{command}");
            return Task.FromResult(PageWriteOutcome.Saved);
        }

        public (PublishStatus From, string Reason)? LastAbandon { get; private set; }

        public Task<PageWriteOutcome> AbandonScheduleAsync(
            Page page, PublishStatus from, string reason, CancellationToken cancellationToken)
        {
            Writes.Add("abandon-schedule");
            LastAbandon = (from, reason);
            return Task.FromResult(PageWriteOutcome.Saved);
        }

        public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(
            [
                .. rows.Values
                    .Where(row => row.Status == PublishStatus.Scheduled && row.ScheduledFor is { } when && when <= asOf)
                    .OrderBy(row => row.ScheduledFor)
                    .Take(take)
                    .Select(row => row.Id),
            ]);

        public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(rows.Values.Select(row => row.Slug).ToHashSet(StringComparer.Ordinal));

        public Task<int> AddSeedAsync(IReadOnlyList<Page> pages, string actor, CancellationToken cancellationToken)
        {
            Seeded.AddRange(pages);
            return Task.FromResult(pages.Count);
        }
    }

    private sealed class RecordingPagesBusiness : IPagesBusiness
    {
        private static readonly PageServiceModel Page = new(
            Guid.NewGuid(), "About", "about", "<p>x</p>", [], PublishStatus.Draft, null, null, false, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "editor-1", AnyTag);

        public List<string> Calls { get; } = [];

        public PageImageCheck? LastImages { get; private set; }

        public Task<OperationResult<CursorPage<PageSummaryServiceModel>>> ListAsync(PageListViewModel request, CancellationToken cancellationToken)
        {
            Calls.Add("list");
            return Task.FromResult(OperationResult.Success(new CursorPage<PageSummaryServiceModel>([], null)));
        }

        public Task<OperationResult<PageServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            Calls.Add("get");
            return Task.FromResult(OperationResult.Success(Page));
        }

        public Task<OperationResult<PageServiceModel>> CreateAsync(
            PageCreateViewModel create, PageImageCheck images, string actorUserId, CancellationToken cancellationToken)
        {
            Calls.Add("create");
            LastImages = images;
            return Task.FromResult(OperationResult.Success(Page));
        }

        public Task<OperationResult<PageServiceModel>> UpdateAsync(
            Guid id, PageUpdateViewModel update, string? ifMatch, PageImageCheck? images, string actorUserId, CancellationToken cancellationToken)
        {
            Calls.Add("update");
            LastImages = images;
            return Task.FromResult(OperationResult.Success(Page));
        }

        public Task<OperationResult<PageServiceModel>> TransitionAsync(
            Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            Calls.Add($"transition:{command}");
            return Task.FromResult(OperationResult.Success(Page));
        }

        public Task<int> SeedAsync(IReadOnlyList<PagePlaceholder> placeholders, string actor, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public DateTimeOffset? LastScheduledFor { get; private set; }

        public Task<OperationResult<PageServiceModel>> ScheduleAsync(
            Guid id, DateTimeOffset scheduledFor, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            Calls.Add("schedule");
            LastScheduledFor = scheduledFor;
            return Task.FromResult(OperationResult.Success(Page));
        }

        public Task<OperationResult<PageServiceModel>> UnscheduleAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            Calls.Add("unschedule");
            return Task.FromResult(OperationResult.Success(Page));
        }

        public IReadOnlyList<Guid> Due { get; set; } = [];

        public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
        {
            Calls.Add("list-due");
            return Task.FromResult(Due);
        }

        public ScheduledPublishOutcome ScheduledOutcome { get; set; } = ScheduledPublishOutcome.Published;

        public Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken)
        {
            Calls.Add("publish-scheduled");
            return Task.FromResult(ScheduledOutcome);
        }
    }
}
