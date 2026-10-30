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
using Arts6311.Tests.Infrastructure;
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

    [Fact]
    public async Task DeletingAPublishedPageStagesItsRevalidationAndDeletingADraftStagesNone()
    {
        // ADR 0052: the delete takes a live page off the site, so it clears what was showing it. Nothing public
        // renders a Draft, so that delete spends no outbox row (content.md).
        var (dataLayer, _, audit, log) = DataLayer();

        await dataLayer.DeleteAsync(NewPage(PublishStatus.Published), "admin-1", Ct);
        await dataLayer.DeleteAsync(NewPage(), "admin-1", Ct);

        Assert.Equal(
            ["outbox:site.revalidate-requested", "audit:page.deleted", "save", "audit:page.deleted", "save"],
            log.Calls);
        Assert.Equal("status:Published", audit.Entries[0].BeforeReference);
    }

    [Fact]
    public async Task ADeleteMarksTheRowAndARestoreClearsItWithoutRevalidating()
    {
        var (dataLayer, _, audit, log) = DataLayer();
        var page = NewPage(PublishStatus.Published);

        await dataLayer.DeleteAsync(page, "admin-1", Ct);
        Assert.Equal((Now, "admin-1"), (page.DeletedAt, page.DeletedBy));

        page.Status = PublishStatus.Draft;
        await dataLayer.RestoreAsync(page, PublishStatus.Published, "admin-1", Ct);

        Assert.Equal((null, null), (page.DeletedAt, page.DeletedBy));

        // One revalidation in all: the delete's. A restore brings the page back as a Draft, which nothing renders.
        Assert.Equal(1, log.Calls.Count(call => call == "outbox:site.revalidate-requested"));
        Assert.Equal(["page.deleted", "page.restored"], audit.Entries.Select(entry => entry.Action));
        Assert.Equal(("status:Published", "status:Draft"), (audit.Entries[^1].BeforeReference, audit.Entries[^1].AfterReference));
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
        var renamed = await business.UpdateAsync(page.Id, new PageUpdateViewModel(Slug: slug), EntityTag.Format(page.RowVersion), null, "e", Ct);

        Assert.Contains("slug", created.Error!.FieldErrors.Keys);
        Assert.Contains("slug", renamed.Error!.FieldErrors.Keys);
        Assert.Empty(dataLayer.Writes);

        // One made from the title steps past it, as past a taken one: staff never type the address.
        var fromTitle = await business.CreateAsync(new PageCreateViewModel(slug.ToUpperInvariant()), PageImageCheck.None, "e", Ct);

        Assert.Equal($"{slug}-2", fromTitle.Value!.Slug);
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
    public async Task BusinessDropsAPictureAndReferencesNothingWhateverMediaSaid()
    {
        // ADR 0045: the sanitizer takes the picture out, so there is nothing left for Media's answer to vouch for.
        var (business, dataLayer) = Business();
        var body = $"<p>Window.{Img}</p>";

        var notAsked = await business.CreateAsync(new PageCreateViewModel("About", Body: body), PageImageCheck.None, "e", Ct);
        var held = await business.CreateAsync(
            new PageCreateViewModel("About", Body: body), new PageImageCheck(new HashSet<Guid> { ImageId }, new HashSet<Guid>()), "e", Ct);

        Assert.Equal("<p>Window.</p>", notAsked.Value!.Body);
        Assert.Empty(notAsked.Value.ImageMediaIds);
        Assert.Empty(held.Value!.ImageMediaIds);
        Assert.Equal(["create", "create"], dataLayer.Writes);
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

    [Theory]
    [InlineData(PublishStatus.Draft)]
    [InlineData(PublishStatus.Scheduled)]
    [InlineData(PublishStatus.Published)]
    [InlineData(PublishStatus.Archived)]
    public async Task APageIsDeletedFromAnyStatus(PublishStatus status)
    {
        // ADR 0052: "this was a mistake" is not a lifecycle step, so unlike the status commands there is no
        // transition table to satisfy — Published goes straight out, taking its revalidation with it.
        var (business, dataLayer) = Business();
        var page = dataLayer.Add(NewPage(status));

        var result = await business.DeleteAsync(page.Id, EntityTag.Format(page.RowVersion), "admin-1", Ct);

        Assert.True(result.Succeeded);
        Assert.Equal(["delete"], dataLayer.Writes);
    }

    [Fact]
    public async Task ADeletedPageIsGoneToEveryOtherOperation()
    {
        var (business, dataLayer) = Business();
        var page = dataLayer.Add(NewPage(PublishStatus.Published));
        var tag = EntityTag.Format(page.RowVersion);
        await business.DeleteAsync(page.Id, tag, "admin-1", Ct);

        var read = await business.GetAsync(page.Id, Ct);
        var edited = await business.UpdateAsync(page.Id, new PageUpdateViewModel(Title: "New"), tag, null, "e", Ct);
        var unpublished = await business.TransitionAsync(page.Id, PublishCommand.Unpublish, tag, "e", Ct);
        var deletedAgain = await business.DeleteAsync(page.Id, tag, "admin-1", Ct);

        foreach (var result in new[] { read.Error, edited.Error, unpublished.Error, deletedAgain.Error })
        {
            Assert.Equal(ErrorCodes.NotFound, result!.Code);
        }

        // Only the delete itself was written: nothing above it reached the DataLayer.
        Assert.Equal(["delete"], dataLayer.Writes);
    }

    [Fact]
    public async Task ARestoreComesBackAsADraftWithItsScheduleClearedAndItsFirstPublishKept()
    {
        var (business, dataLayer) = Business();
        var page = dataLayer.Add(NewPage(PublishStatus.Scheduled));
        page.PublishedAt = Now.AddDays(-7);
        page.ScheduledFor = Now.AddDays(1);
        await business.DeleteAsync(page.Id, EntityTag.Format(page.RowVersion), "admin-1", Ct);

        var restored = await business.RestoreAsync(page.Id, EntityTag.Format(page.RowVersion), "admin-1", Ct);

        Assert.True(restored.Succeeded, restored.Error?.Code);
        Assert.Equal(PublishStatus.Draft, restored.Value!.Status);
        Assert.Null(restored.Value.ScheduledFor);

        // PublishedAt is the first publish, which the delete did not undo: it is still what makes a later slug
        // change owe a redirect (ADR 0035).
        Assert.Equal(Now.AddDays(-7), restored.Value.PublishedAt);
        Assert.True(restored.Value.SlugLocked);
        Assert.Equal(PublishStatus.Scheduled, dataLayer.LastDeletedAs);
    }

    [Fact]
    public async Task ARestoreIsRefusedWhenAnotherPageTookTheAddressAndAllowedOnceItIsFree()
    {
        var (business, dataLayer) = Business();
        var page = dataLayer.Add(NewPage(PublishStatus.Published, slug: "privacy"));
        await business.DeleteAsync(page.Id, EntityTag.Format(page.RowVersion), "admin-1", Ct);

        // The delete freed "privacy", so a new page could take it — and did.
        var replacement = dataLayer.Add(NewPage(slug: "privacy"));

        var refused = await business.RestoreAsync(page.Id, EntityTag.Format(page.RowVersion), "admin-1", Ct);
        replacement.Slug = "privacy-2";
        var allowed = await business.RestoreAsync(page.Id, EntityTag.Format(page.RowVersion), "admin-1", Ct);

        Assert.Equal(ContentErrorCodes.SlugTaken, refused.Error!.Code);
        Assert.Contains("slug", refused.Error.FieldErrors.Keys);
        Assert.True(allowed.Succeeded, allowed.Error?.Code);
        Assert.Equal(["delete", "restore"], dataLayer.Writes);
    }

    [Fact]
    public async Task ALivePageCannotBeRestoredAndAStaleTagIsAConflict()
    {
        var (business, dataLayer) = Business();
        var page = dataLayer.Add(NewPage(PublishStatus.Published));

        var live = await business.RestoreAsync(page.Id, EntityTag.Format(page.RowVersion), "admin-1", Ct);
        var staleDelete = await business.DeleteAsync(page.Id, EntityTag.Format(Guid.NewGuid()), "admin-1", Ct);
        await business.DeleteAsync(page.Id, EntityTag.Format(page.RowVersion), "admin-1", Ct);
        var staleRestore = await business.RestoreAsync(page.Id, EntityTag.Format(Guid.NewGuid()), "admin-1", Ct);

        Assert.Equal(ErrorCodes.NotFound, live.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, staleDelete.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, staleRestore.Error!.Code);
        Assert.Equal(["delete"], dataLayer.Writes);
    }

    // ---- Facade ----

    [Fact]
    public async Task TheFacadeAsksMediaNothingBecauseASanitizedBodyShowsNoPictures()
    {
        var (facade, business, media) = Facade();

        await facade.CreateAsync(new PageCreateViewModel("About", Body: "<p>Other.</p><img src=\"https://x.example/a.jpg\">"), "e", Ct);
        Assert.Equal(PageImageCheck.None, business.LastImages);

        // ADR 0045: even a library picture leaves the body, so Media has nothing to answer for.
        await facade.CreateAsync(new PageCreateViewModel("About", Body: $"<p>Window.{Img}</p>"), "e", Ct);

        Assert.Equal(0, media.Checks);
        Assert.Equal(PageImageCheck.None, business.LastImages);
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
            await facade.RestoreAsync(Guid.NewGuid(), null, "admin-1", Ct),
        };

        var deleted = await facade.DeleteAsync(Guid.NewGuid(), null, "admin-1", Ct);

        Assert.All(results, result => Assert.Equal(ErrorCodes.PreconditionRequired, result.Error!.Code));
        Assert.Equal(ErrorCodes.PreconditionRequired, deleted.Error!.Code);
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
            new PageListValidator(), new ContentScheduleValidator(), TestAppCache.Create());
        return (facade, business, media);
    }

    private sealed class FakePagesRepository(CallLog log) : IPagesRepository
    {
        public Task<global::Arts6311.Domain.Modules.Pages.Managers.ServiceModels.SitePageServiceModel?> FindPublishedAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<List<(string Slug, DateTimeOffset UpdatedAt)>> ListPublishedAddressesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public List<Page> Rows { get; } = [];

        public Exception? SaveFailure { get; set; }

        public bool SlugExists { get; set; }

        public void Add(Page page)
        {
            log.Add("add");
            Rows.Add(page);
        }

        public Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.FirstOrDefault(row => row.Id == id && row.DeletedAt is null));

        public Task<Page?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.FirstOrDefault(row => row.Id == id && row.DeletedAt is not null));

        public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) => Task.FromResult(SlugExists);

        public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) => Task.FromResult(new HashSet<string>());

        public Task<List<PageSummaryServiceModel>> ListAsync(
            PageListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
            Task.FromResult(new List<PageSummaryServiceModel>());

        public Task<List<PageDeletedRow>> ListDeletedAsync(
            PageListPosition? after, int take, string? search, CancellationToken cancellationToken) =>
            Task.FromResult(new List<PageDeletedRow>());

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
        public Task<global::Arts6311.Domain.Modules.Pages.Managers.ServiceModels.SitePageServiceModel?> FindPublishedAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<List<(string Slug, DateTimeOffset UpdatedAt)>> ListPublishedAddressesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        private readonly Dictionary<Guid, Page> rows = [];

        public List<string> Writes { get; } = [];

        public List<string> Summaries { get; } = [];

        public List<Page> Seeded { get; } = [];

        public Page Add(Page page)
        {
            rows[page.Id] = page;
            return page;
        }

        public Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(rows.GetValueOrDefault(id) is { DeletedAt: null } live ? live : null);

        public Task<Page?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(rows.GetValueOrDefault(id) is { DeletedAt: not null } deleted ? deleted : null);

        public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
            Task.FromResult(rows.Values.Any(row => row.Slug == slug && row.Id != exceptId && row.DeletedAt is null));

        public Task<List<PageSummaryServiceModel>> ListAsync(
            PageListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
            Task.FromResult(new List<PageSummaryServiceModel>());

        public Task<List<PageDeletedRow>> ListDeletedAsync(
            PageListPosition? after, int take, string? search, CancellationToken cancellationToken) =>
            Task.FromResult<List<PageDeletedRow>>(
            [
                .. rows.Values
                    .Where(row => row.DeletedAt is not null)
                    .OrderBy(row => row.Title, StringComparer.Ordinal)
                    .Take(take)
                    .Select(row => new PageDeletedRow(
                        row.Id, row.Title, row.Slug, row.Status, row.DeletedAt!.Value, row.RowVersion)),
            ]);

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

        public PageWriteOutcome DeleteOutcome { get; set; } = PageWriteOutcome.Saved;

        public Task<PageWriteOutcome> DeleteAsync(Page page, string actorUserId, CancellationToken cancellationToken)
        {
            Writes.Add("delete");
            if (DeleteOutcome == PageWriteOutcome.Saved)
            {
                SoftDelete.Stamp(page, actorUserId, Now);
            }

            return Task.FromResult(DeleteOutcome);
        }

        public PublishStatus? LastDeletedAs { get; private set; }

        public PageWriteOutcome RestoreOutcome { get; set; } = PageWriteOutcome.Saved;

        public Task<PageWriteOutcome> RestoreAsync(
            Page page, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken)
        {
            Writes.Add("restore");
            LastDeletedAs = deletedAs;
            if (RestoreOutcome == PageWriteOutcome.Saved)
            {
                SoftDelete.Clear(page);
            }

            return Task.FromResult(RestoreOutcome);
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
        public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Modules.Pages.Managers.ServiceModels.SitePageServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Managers.Publishing.PreviewRecord<global::Arts6311.Domain.Modules.Pages.Managers.ServiceModels.SitePageServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<global::Arts6311.Domain.Managers.Publishing.SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

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

        public Task<OperationResult<CursorPage<PageDeletedServiceModel>>> ListDeletedAsync(
            PageListViewModel request, CancellationToken cancellationToken)
        {
            Calls.Add("list-deleted");
            return Task.FromResult(OperationResult.Success(new CursorPage<PageDeletedServiceModel>([], null)));
        }

        public Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            Calls.Add("delete");
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult<PageServiceModel>> RestoreAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            Calls.Add("restore");
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
