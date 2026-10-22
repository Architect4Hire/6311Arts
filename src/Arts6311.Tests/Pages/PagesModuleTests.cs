using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;
using Arts6311.Tests.Media.Infrastructure;
using Arts6311.Tests.Pages.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Pages;

/// <summary>
/// The module end to end through its facade, on a real (SQLite) database with the real sanitizer: what is stored is
/// only ever sanitized HTML, a picture in it is dropped (ADR 0045), and the lifecycle, audit and atomic save
/// work as for every content type.
/// </summary>
public sealed class PagesModuleTests
{
    private const string Editor = PagesTestHost.EditorId;

    private const string Admin = PagesTestHost.AdminId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheBodyIsStoredOnlyAsTheSanitizerLeftItAndTheAuditSaysWhatWentAway()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);

        var page = await CreateAsync(host, new PageCreateViewModel(
            "About Us",
            Body: "<h2 onclick=\"steal()\">Hello</h2><script>steal()</script><p style=\"color:red\">We <b>love</b> art.</p>"));

        Assert.Equal("about-us", page.Slug);
        Assert.Equal("<h2>Hello</h2><p>We <strong>love</strong> art.</p>", page.Body);
        Assert.Equal(page.Body, (await host.StoredAsync(page.Id))!.Body);

        var summary = await host.QueryAsync(context => context.AuditLogs.AsNoTracking().Select(entry => entry.Summary).SingleAsync(Ct));
        Assert.Contains("Sanitizer removed:", summary, StringComparison.Ordinal);
        Assert.Contains("<script> and its contents", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoWritePathStoresScriptOrInlineHandlers()
    {
        // content.md: "Scripts and inline event handlers are never stored" — create and edit alike.
        await using var host = await PagesTestHost.CreateAsync(Ct);
        const string attack = "<p><a href=\"javascript:alert(1)\" onmouseover=\"alert(1)\">x</a></p><img src=x onerror=alert(1)><svg onload=alert(1)>";

        var created = await CreateAsync(host, new PageCreateViewModel("Attack", Body: attack));
        var edited = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            created.Id, new PageUpdateViewModel(Body: attack + "<p>more</p>"), created.ETag, Editor, Ct)));

        foreach (var body in new[] { created.Body, edited.Body, (await host.StoredAsync(created.Id))!.Body })
        {
            Assert.DoesNotContain("javascript", body ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("on", Attributes(body), StringComparer.Ordinal);
            Assert.DoesNotContain("<svg", body ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AWellFormedBodyIsKeptExactlyAndSendingItAgainIsNoChange()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        const string copy = "<h2>Your privacy</h2><p>See <strong>Visit</strong>.</p><ul><li>One</li></ul>";
        var page = await CreateAsync(host, new PageCreateViewModel("Privacy", Body: copy));

        var again = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            page.Id, new PageUpdateViewModel(Body: copy), page.ETag, Editor, Ct)));

        Assert.Equal(copy, page.Body);
        Assert.Equal(page.ETag, again.ETag);
        Assert.Equal(["page.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task APictureInTheBodyIsDroppedAndHoldsNothingInTheLibrary()
    {
        // ADR 0045: no pictures in a body at this time, even a described library original.
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var image = await host.AddImageAsync();
        var page = await CreateAsync(host, new PageCreateViewModel(
            "About", Body: $"<p>Our window.{PagesTestHost.Img(image)}</p>"));

        Assert.Equal("<p>Our window.</p>", page.Body);
        Assert.Empty(page.ImageMediaIds);
        Assert.Empty((await host.StoredAsync(page.Id))!.Images);
        Assert.True((await DeleteImageAsync(host, image)).Succeeded);

        var summary = await host.QueryAsync(context => context.AuditLogs.AsNoTracking()
            .Where(entry => entry.Action == "page.created").Select(entry => entry.Summary).SingleAsync(Ct));
        Assert.Contains("a picture", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishWaitsForABodyAndAPublishedPageKeepsOne()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var page = await CreateAsync(host, new PageCreateViewModel("Terms", Body: "<p> </p>"));
        Assert.Null(page.Body);

        var refused = await host.WithFacadeAsync(facade => facade.PublishAsync(page.Id, page.ETag, Editor, Ct));
        Assert.Contains("body", refused.Error!.FieldErrors.Keys);

        var written = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            page.Id, new PageUpdateViewModel(Body: "<p>The terms.</p>"), page.ETag, Editor, Ct)));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(page.Id, written.ETag, Editor, Ct)));
        var cleared = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            page.Id, new PageUpdateViewModel(Body: ""), published.ETag, Editor, Ct));

        Assert.Equal(PublishStatus.Published, published.Status);
        Assert.True(published.SlugLocked);
        Assert.Contains("body", cleared.Error!.FieldErrors.Keys);
    }

    [Fact]
    public async Task TheLifecycleRunsDraftPublishedDraftArchivedAndIllegalMovesAreRefused()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var page = await CreateAsync(host, new PageCreateViewModel("Terms", Body: "<p>Terms.</p>"));

        var unpublishDraft = await host.WithFacadeAsync(facade => facade.UnpublishAsync(page.Id, page.ETag, Editor, Ct));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(page.Id, page.ETag, Editor, Ct)));
        var publishTwice = await host.WithFacadeAsync(facade => facade.PublishAsync(page.Id, published.ETag, Editor, Ct));
        var unpublished = await Ok(host.WithFacadeAsync(facade => facade.UnpublishAsync(page.Id, published.ETag, Editor, Ct)));
        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(page.Id, unpublished.ETag, PagesTestHost.AdminId, Ct)));
        var publishArchived = await host.WithFacadeAsync(facade => facade.PublishAsync(page.Id, archived.ETag, Editor, Ct));

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, unpublishDraft.Error!.Code);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, publishTwice.Error!.Code);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, publishArchived.Error!.Code);
        Assert.Equal(published.PublishedAt, unpublished.PublishedAt);
        Assert.Equal(PublishStatus.Archived, archived.Status);
        Assert.Equal(
            ["page.archived", "page.created", "page.published", "page.unpublished"],
            (await host.AuditActionsAsync()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ABodyTooDeeplyNestedIsRefusedNotStoredEmpty()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);

        var result = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new PageCreateViewModel("Deep", Body: string.Concat(Enumerable.Repeat("<b>", 66_000))), Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Contains("nested", result.Error.FieldErrors["body"][0], StringComparison.Ordinal);
        Assert.Equal(0, await host.QueryAsync(context => context.Set<Page>().CountAsync(Ct)));
    }

    [Fact]
    public async Task AnUntouchedPlaceholderCannotBePublishedButItsRealTextCan()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct, withSeeder: true);
        await host.SeedAsync(Ct);
        var terms = await host.QueryAsync(context => context.Set<Page>().AsNoTracking().SingleAsync(page => page.Slug == "terms", Ct));

        var refused = await host.WithFacadeAsync(facade => facade.PublishAsync(terms.Id, EntityTag.Format(terms.RowVersion), Editor, Ct));
        Assert.Contains("placeholder", refused.Error!.FieldErrors["body"][0], StringComparison.Ordinal);

        var written = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            terms.Id, new PageUpdateViewModel(Body: "<p>The real terms.</p>"), EntityTag.Format(terms.RowVersion), Editor, Ct)));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(terms.Id, written.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Published, published.Status);
    }

    [Fact]
    public async Task APlaceholderSeededBeforeLinksWereTakenOutIsStillAPlaceholder()
    {
        // ADR 0045: a database seeded under the wider allow-list holds the placeholder with its mailto: link.
        await using var host = await PagesTestHost.CreateAsync(Ct, withSeeder: true);
        await host.SeedAsync(Ct);
        await host.ExecuteAsync(async context =>
        {
            var seeded = await context.Set<Page>().SingleAsync(page => page.Slug == "terms", Ct);
            seeded.Body = seeded.Body!.Replace(
                "info@6311arts.com.", "<a href=\"mailto:info@6311arts.com\">info@6311arts.com</a>.", StringComparison.Ordinal);
            await context.SaveChangesAsync(Ct);
        });
        var terms = await host.QueryAsync(context => context.Set<Page>().AsNoTracking().SingleAsync(page => page.Slug == "terms", Ct));

        var refused = await host.WithFacadeAsync(facade => facade.PublishAsync(terms.Id, EntityTag.Format(terms.RowVersion), Editor, Ct));

        Assert.Contains("<a href", terms.Body, StringComparison.Ordinal);
        Assert.Contains("placeholder", refused.Error!.FieldErrors["body"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnArchivedPageKeepsItsBodyToo()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var page = await CreateAsync(host, new PageCreateViewModel("Old", Body: "<p>Old text.</p>"));
        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(page.Id, page.ETag, PagesTestHost.AdminId, Ct)));

        var cleared = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            page.Id, new PageUpdateViewModel(Body: ""), archived.ETag, Editor, Ct));

        Assert.Contains("body", cleared.Error!.FieldErrors.Keys);
    }

    [Fact]
    public async Task AStaleIfMatchIsAConflictAndAMissingOneA428()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var page = await CreateAsync(host, new PageCreateViewModel("Terms", Body: "<p>One.</p>"));
        var edited = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            page.Id, new PageUpdateViewModel(Body: "<p>Two.</p>"), page.ETag, Editor, Ct)));

        var stale = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            page.Id, new PageUpdateViewModel(Body: "<p>Three.</p>"), page.ETag, "editor-2", Ct));
        var stalePublish = await host.WithFacadeAsync(facade => facade.PublishAsync(page.Id, page.ETag, "editor-2", Ct));
        var missing = await host.WithFacadeAsync(facade => facade.ArchiveAsync(Guid.NewGuid(), null, PagesTestHost.AdminId, Ct));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, stalePublish.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, missing.Error!.Code);
        var stored = (await host.StoredAsync(page.Id))!;
        Assert.Equal("<p>Two.</p>", stored.Body);
        Assert.Equal(edited.ETag, EntityTag.Format(stored.RowVersion));
    }

    [Fact]
    public async Task ASlugIsUniqueAndAPublishedOneMovesWithARedirect()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        await CreateAsync(host, new PageCreateViewModel("Privacy", Body: "<p>P.</p>"));
        var terms = await CreateAsync(host, new PageCreateViewModel("Terms", Body: "<p>T.</p>"));

        var taken = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            terms.Id, new PageUpdateViewModel(Slug: "privacy"), terms.ETag, Editor, Ct));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(terms.Id, terms.ETag, Editor, Ct)));
        var moved = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            terms.Id, new PageUpdateViewModel(Slug: "terms-of-use"), published.ETag, Editor, Ct)));

        Assert.Equal(ContentErrorCodes.SlugTaken, taken.Error!.Code);
        Assert.Equal("terms-of-use", moved.Slug);
        var payload = Assert.Single(await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == "redirects.slug-changed").Select(message => message.PayloadJson).ToListAsync(Ct)));
        Assert.Contains("\"fromPath\":\"/terms\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"toPath\":\"/terms-of-use\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedRenameLeavesNeitherTheNewSlugNorItsRedirectMessage()
    {
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await PagesTestHost.CreateAsync(Ct, failAudit);
        var page = await CreateAsync(host, new PageCreateViewModel("Terms", Body: "<p>T.</p>"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(page.Id, page.ETag, Editor, Ct)));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.UpdateAsync(
            page.Id, new PageUpdateViewModel(Slug: "terms-of-use"), published.ETag, Editor, Ct)));

        Assert.Equal("terms", (await host.StoredAsync(page.Id))!.Slug);
        // Scoped to the rename's own message: the publish above left a site.revalidate-requested of its own, which
        // committed with it (ADR 0036).
        Assert.Equal(0, await host.QueryAsync(context => context.OutboxMessages
            .CountAsync(message => message.Type == RedirectSlugChanged.MessageType, Ct)));
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherThePageNorItsReferencesNorItsAuditRow()
    {
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await PagesTestHost.CreateAsync(Ct, failAudit);
        var image = await host.AddImageAsync();

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.CreateAsync(
            new PageCreateViewModel("About", Body: $"<p>{PagesTestHost.Img(image)}</p>"), Editor, Ct)));

        Assert.True(failAudit.Fired);
        Assert.Equal(0, await host.QueryAsync(context => context.Set<Page>().CountAsync(Ct)));
        Assert.Equal(0, await host.QueryAsync(context => context.Set<PageMediaReference>().CountAsync(Ct)));
        Assert.Empty(await host.AuditActionsAsync());
    }

    [Fact]
    public async Task PublishStagesARevalidationInTheSameSave()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var page = await CreateAsync(host, new PageCreateViewModel("Terms", Body: "<p>T.</p>"));

        await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(page.Id, page.ETag, Editor, Ct)));

        var messages = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking().ToListAsync(Ct));
        var message = Assert.Single(messages);
        Assert.Equal(SiteRevalidateRequested.MessageType, message.Type);

        // A page lives at the root, so its address is just its slug (ADR 0036).
        var staged = SiteRevalidateRequested.TryParse(message.PayloadJson)!;
        Assert.Equal("page", staged.EntityType);
        Assert.Equal(page.Id, staged.EntityId);
        Assert.Equal(["/terms"], staged.Paths);
    }

    [Fact]
    public async Task EveryStatusCommandStagesItsOwnRevalidationAndAFailedOneStagesNone()
    {
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await PagesTestHost.CreateAsync(Ct, failAudit);
        var page = await CreateAsync(host, new PageCreateViewModel("Terms", Body: "<p>T.</p>"));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(page.Id, page.ETag, Editor, Ct)));
        var drafted = await Ok(host.WithFacadeAsync(facade => facade.UnpublishAsync(page.Id, published.ETag, Editor, Ct)));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(
            facade => facade.ArchiveAsync(page.Id, drafted.ETag, PagesTestHost.AdminId, Ct)));

        // Two commits, two messages: the archive that failed left neither its status nor its message (outbox.md).
        Assert.Equal(2, await host.QueryAsync(context => context.OutboxMessages
            .CountAsync(message => message.Type == SiteRevalidateRequested.MessageType, Ct)));
        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(page.Id))!.Status);
    }

    [Fact]
    public async Task SchedulingSetsTheTimeUnschedulingClearsItAndAPastTimeIsRefused()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var page = await CreateAsync(host, new PageCreateViewModel("Terms", Body: "<p>T.</p>"));
        var when = host.Time.GetUtcNow().AddHours(2);

        var past = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            page.Id, new ContentScheduleViewModel(host.Time.GetUtcNow().AddMinutes(-1)), page.ETag, Editor, Ct));
        var scheduled = await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            page.Id, new ContentScheduleViewModel(when), page.ETag, Editor, Ct)));
        var drafted = await Ok(host.WithFacadeAsync(facade => facade.UnscheduleAsync(page.Id, scheduled.ETag, Editor, Ct)));

        Assert.Equal(ErrorCodes.ValidationFailed, past.Error!.Code);
        Assert.Contains("scheduledFor", past.Error.FieldErrors.Keys);
        Assert.Equal((PublishStatus.Scheduled, when), (scheduled.Status, scheduled.ScheduledFor));
        Assert.Equal((PublishStatus.Draft, (DateTimeOffset?)null), (drafted.Status, drafted.ScheduledFor));
    }

    [Fact]
    public async Task APlaceholderBodyIsRefusedAtScheduleAsWellAsAtPublish()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct, withSeeder: true);
        await host.SeedAsync(Ct);
        var placeholder = await host.QueryAsync(context => context.Set<Page>().AsNoTracking().FirstAsync(Ct));

        var result = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            placeholder.Id,
            new ContentScheduleViewModel(host.Time.GetUtcNow().AddHours(1)),
            EntityTag.Format(placeholder.RowVersion),
            Editor,
            Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Contains("body", result.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task TheScheduledPublisherPublishesADuePageOnceAndSkipsOneAlreadyPublished()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var page = await CreateAsync(host, new PageCreateViewModel("Terms", Body: "<p>T.</p>"));
        var asOf = host.Time.GetUtcNow();
        await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            page.Id, new ContentScheduleViewModel(asOf.AddMinutes(1)), page.ETag, Editor, Ct)));

        var passTime = asOf.AddMinutes(2);
        var due = await host.WithFacadeAsync(facade => facade.ListDueAsync(passTime, 50, Ct));
        var first = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(page.Id, passTime, Ct));
        var second = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(page.Id, passTime, Ct));

        Assert.Equal([page.Id], due);
        Assert.Equal(ScheduledPublishOutcome.Published, first);

        // The second pass re-reads it, finds it published, and does nothing: the unit is idempotent.
        Assert.Equal(ScheduledPublishOutcome.Skipped, second);
        Assert.Equal(PublishStatus.Published, (await host.StoredAsync(page.Id))!.Status);
        Assert.Single(await host.AuditActionsAsync(), action => action == "page.published");
    }

    [Fact]
    public async Task TheListPagesByTitleAndACursorOnlyFitsItsOwnFilter()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        foreach (var title in new[] { "Visit", "About", "Terms" })
        {
            await CreateAsync(host, new PageCreateViewModel(title, Body: "<p>x</p>"));
        }

        var first = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new PageListViewModel(Limit: 2), Ct)));
        var second = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new PageListViewModel(first.NextCursor, Limit: 2), Ct)));
        var otherFilter = await host.WithFacadeAsync(facade => facade.ListAsync(new PageListViewModel(first.NextCursor, Search: "t"), Ct));

        Assert.Equal(["About", "Terms"], first.Items.Select(row => row.Title));
        Assert.Equal(["Visit"], second.Items.Select(row => row.Title));
        Assert.Equal(ErrorCodes.CursorInvalid, otherFilter.Error!.Code);
    }

    [Fact]
    public async Task ThePlaceholdersAreDraftsSeededOnceAndAStaffEditSurvivesARerun()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct, withSeeder: true);

        await host.SeedAsync(Ct);
        var privacy = await host.QueryAsync(context => context.Set<Page>().AsNoTracking().SingleAsync(page => page.Slug == "privacy", Ct));
        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            privacy.Id, new PageUpdateViewModel(Body: "<p>Our real policy.</p>"), EntityTag.Format(privacy.RowVersion), Editor, Ct)));
        await host.SeedAsync(Ct);

        var all = await host.QueryAsync(context => context.Set<Page>().AsNoTracking().OrderBy(page => page.Slug).ToListAsync(Ct));
        Assert.Equal(["privacy", "terms"], all.Select(page => page.Slug));
        Assert.All(all, page => Assert.Equal(PublishStatus.Draft, page.Status));
        Assert.Equal("<p>Our real policy.</p>", all[0].Body);
        Assert.Equal(PagePlaceholderSeeder.SeedActor, all[1].UpdatedBy);
        Assert.Contains("info@6311arts.com", all[1].Body, StringComparison.Ordinal);
        Assert.Single(await host.AuditActionsAsync(), action => action == "page.seeded");
    }

    [Fact]
    public async Task ThePlaceholderSeedRunsWithWhatMigrationServiceComposesAndNoMedia()
    {
        // MigrationService seeds in every environment, Production included, where it composes no Media module.
        await using var host = await PagesTestHost.CreateAsync(Ct, seedOnly: true);

        await host.SeedAsync(Ct);

        Assert.Equal(2, await host.QueryAsync(context => context.Set<Page>().CountAsync(Ct)));
    }

    [Fact]
    public async Task ADeletedPageIsGoneFromTheCmsTheSiteTheSitemapAndAPreview()
    {
        // ADR 0052: one query filter, so "deleted" means gone from every read — not a status every read has to
        // remember to exclude.
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var page = await CreateAsync(host, new PageCreateViewModel("Privacy", Body: "<p>P.</p>"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(page.Id, page.ETag, Editor, Ct)));

        // The sitemap is read straight through, so it says what the site knows now; the page read itself is cached
        // until the Worker handles the delete's revalidation, exactly as an unpublish is (ADR 0036, 0049), which
        // is why the site read below is this test's first.
        Assert.Equal(["/privacy"], (await host.WithFacadeAsync(facade => facade.ListSitemapAsync(Ct))).Select(entry => entry.Path));

        var deleted = await host.WithFacadeAsync(facade => facade.DeleteAsync(page.Id, published.ETag, Admin, Ct));

        Assert.True(deleted.Succeeded, deleted.Error?.Code);
        Assert.Equal(ErrorCodes.NotFound, (await host.WithFacadeAsync(facade => facade.GetAsync(page.Id, Ct))).Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, (await host.WithFacadeAsync(facade => facade.GetForSiteAsync("privacy", Ct))).Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, (await host.WithFacadeAsync(facade => facade.GetForPreviewAsync(page.Id, Ct))).Error!.Code);
        Assert.Empty((await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new PageListViewModel(), Ct)))).Items);
        Assert.Empty(await host.WithFacadeAsync(facade => facade.ListSitemapAsync(Ct)));

        // The row is still there, marked, which is what a restore and the audit trail need.
        var row = (await host.RowAsync(page.Id))!;
        Assert.Equal(Admin, row.DeletedBy);
        Assert.Equal(host.Time.GetUtcNow(), row.DeletedAt);
        Assert.Null(await host.StoredAsync(page.Id));

        var trash = await Ok(host.WithFacadeAsync(facade => facade.ListDeletedAsync(new PageListViewModel(), Ct)));
        var deletedRow = Assert.Single(trash.Items);
        Assert.Equal(("Privacy", "privacy", PublishStatus.Published), (deletedRow.Title, deletedRow.Slug, deletedRow.Status));
        Assert.Equal(EntityTag.Format(row.RowVersion), deletedRow.ETag);
    }

    [Fact]
    public async Task DeletingAPublishedPageRevalidatesItsAddressAndDeletingADraftStagesNothing()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var live = await CreateAsync(host, new PageCreateViewModel("Privacy", Body: "<p>P.</p>"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(live.Id, live.ETag, Editor, Ct)));
        var draft = await CreateAsync(host, new PageCreateViewModel("Terms", Body: "<p>T.</p>"));

        await host.WithFacadeAsync(facade => facade.DeleteAsync(live.Id, published.ETag, Admin, Ct));
        await host.WithFacadeAsync(facade => facade.DeleteAsync(draft.Id, draft.ETag, Admin, Ct));

        // Two messages: the publish's and the delete's. The draft's delete added none — nothing public showed it.
        var staged = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == SiteRevalidateRequested.MessageType)
            .Select(message => message.PayloadJson).ToListAsync(Ct));

        Assert.Equal(2, staged.Count);
        Assert.All(staged, payload => Assert.Equal(["/privacy"], SiteRevalidateRequested.TryParse(payload)!.Paths));
    }

    [Fact]
    public async Task DeletingFreesTheAddressAndARestoreIsRefusedUntilItIsFreeAgain()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var first = await CreateAsync(host, new PageCreateViewModel("Privacy", Body: "<p>First.</p>"));
        await host.WithFacadeAsync(facade => facade.DeleteAsync(first.Id, first.ETag, Admin, Ct));

        // The address is free at once, which is the point: the mistake stops standing in the way (ADR 0052).
        var second = await CreateAsync(host, new PageCreateViewModel("Privacy", Body: "<p>Second.</p>"));

        // The delete rotated the row version, as every write does, so a restore sends back the one the Deleted
        // view is showing.
        var tag = EntityTag.Format((await host.RowAsync(first.Id))!.RowVersion);
        var refused = await host.WithFacadeAsync(facade => facade.RestoreAsync(first.Id, tag, Admin, Ct));

        var moved = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            second.Id, new PageUpdateViewModel(Slug: "privacy-policy"), second.ETag, Editor, Ct)));
        var restored = await Ok(host.WithFacadeAsync(facade => facade.RestoreAsync(first.Id, tag, Admin, Ct)));

        Assert.Equal("privacy", second.Slug);
        Assert.Equal(ContentErrorCodes.SlugTaken, refused.Error!.Code);
        Assert.Equal("privacy-policy", moved.Slug);
        Assert.Equal("privacy", restored.Slug);
    }

    [Fact]
    public async Task ARestoreBringsThePageBackAsADraftTheSiteStillDoesNotShow()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var page = await CreateAsync(host, new PageCreateViewModel("Privacy", Body: "<p>P.</p>"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(page.Id, page.ETag, Editor, Ct)));
        await host.WithFacadeAsync(facade => facade.DeleteAsync(page.Id, published.ETag, Admin, Ct));
        var tag = EntityTag.Format((await host.RowAsync(page.Id))!.RowVersion);

        var restored = await Ok(host.WithFacadeAsync(facade => facade.RestoreAsync(page.Id, tag, Admin, Ct)));

        Assert.Equal(PublishStatus.Draft, restored.Status);
        Assert.Equal(published.PublishedAt, restored.PublishedAt);
        Assert.Equal(ErrorCodes.NotFound, (await host.WithFacadeAsync(facade => facade.GetForSiteAsync("privacy", Ct))).Error!.Code);
        Assert.Empty((await Ok(host.WithFacadeAsync(facade => facade.ListDeletedAsync(new PageListViewModel(), Ct)))).Items);
        Assert.Equal(
            ["page.created", "page.deleted", "page.published", "page.restored"],
            (await host.AuditActionsAsync()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheScheduledPublisherNeverPublishesADeletedPage()
    {
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var page = await CreateAsync(host, new PageCreateViewModel("Terms", Body: "<p>T.</p>"));
        var asOf = host.Time.GetUtcNow();
        var scheduled = await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            page.Id, new ContentScheduleViewModel(asOf.AddMinutes(1)), page.ETag, Editor, Ct)));
        await host.WithFacadeAsync(facade => facade.DeleteAsync(page.Id, scheduled.ETag, Admin, Ct));

        var passTime = asOf.AddMinutes(2);
        var due = await host.WithFacadeAsync(facade => facade.ListDueAsync(passTime, 50, Ct));
        var outcome = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(page.Id, passTime, Ct));

        Assert.Empty(due);
        Assert.Equal(ScheduledPublishOutcome.Skipped, outcome);
        Assert.Equal(PublishStatus.Scheduled, (await host.RowAsync(page.Id))!.Status);
    }

    [Fact]
    public async Task ThePlaceholderSeedDoesNotBringADeletedPageBack()
    {
        // The seeder adds the slugs that are missing. A deleted page is missing to every other read, so without
        // reading past the filter the next MigrationService run would undo the delete (ADR 0052).
        await using var host = await PagesTestHost.CreateAsync(Ct, withSeeder: true);
        await host.SeedAsync(Ct);
        var privacy = await host.QueryAsync(context => context.Set<Page>().AsNoTracking().SingleAsync(page => page.Slug == "privacy", Ct));
        await host.WithFacadeAsync(facade => facade.DeleteAsync(privacy.Id, EntityTag.Format(privacy.RowVersion), Admin, Ct));

        await host.SeedAsync(Ct);

        Assert.Equal(["terms"], await host.QueryAsync(context => context.Set<Page>().AsNoTracking()
            .OrderBy(page => page.Slug).Select(page => page.Slug).ToListAsync(Ct)));
        Assert.Single(await host.AuditActionsAsync(), action => action == "page.seeded");
    }

    [Fact]
    public async Task ADeletedPageStillHoldsItsPicturesSoMediaRefusesToDeleteThem()
    {
        // The delete guard mirrors a Restrict foreign key and the database has not forgotten the deleted row
        // (ADR 0052). ADR 0045 keeps pictures out of a body, so the reference is written here directly — the
        // behaviour this pins down belongs to whichever soft-deletable type attaches an image next.
        await using var host = await PagesTestHost.CreateAsync(Ct);
        var image = await host.AddImageAsync();
        var page = await CreateAsync(host, new PageCreateViewModel("Privacy", Body: "<p>P.</p>"));
        await host.ExecuteAsync(async context =>
        {
            context.Add(new PageMediaReference { PageId = page.Id, MediaAssetId = image });
            await context.SaveChangesAsync(Ct);
        });

        await host.WithFacadeAsync(facade => facade.DeleteAsync(page.Id, page.ETag, Admin, Ct));
        var refused = await DeleteImageAsync(host, image);

        Assert.Equal(MediaErrorCodes.InUse, refused.Error!.Code);
    }

    private static async Task<PageServiceModel> CreateAsync(PagesTestHost host, PageCreateViewModel create) =>
        await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(create, Editor, Ct)));

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.True(result.Succeeded, result.Error?.Code + ": " + result.Error?.Message);
        return result.Value!;
    }

    /// <summary>Every attribute name's first two letters, so an <c>on*</c> handler shows as "on".</summary>
    private static List<string> Attributes(string? html)
    {
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(string.Empty);
        document.Body!.InnerHtml = html ?? string.Empty;
        return [.. document.Body.QuerySelectorAll("*").SelectMany(element => element.Attributes).Select(attribute => attribute.Name[..Math.Min(2, attribute.Name.Length)])];
    }

    private static async Task<OperationResult> DeleteImageAsync(PagesTestHost host, Guid assetId)
    {
        var version = await host.QueryAsync(context =>
            context.Set<MediaAsset>().AsNoTracking().Where(row => row.Id == assetId).Select(row => row.RowVersion).SingleAsync(Ct));

        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediaFacade>()
            .DeleteAsync(assetId, EntityTag.Format(version), PagesTestHost.AdminId, Ct);
    }
}
