using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Tests.Exhibitions.Infrastructure;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Exhibitions;

/// <summary>
/// The module end to end through its facade, on a real (SQLite) database: the rules, the credits across the Artists
/// boundary, the audit trail, the atomic save, and the hero's relationship with Media.
/// </summary>
public sealed class ExhibitionsModuleTests
{
    private const string Editor = ExhibitionsTestHost.EditorId;

    private static readonly DateOnly Start = new(2026, 10, 24);
    private static readonly DateOnly End = new(2026, 12, 5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreatedExhibitionIsADraftWithASlugCreditsInOrderAndAnAuditRow()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var harry = await host.AddArtistAsync("Harry Sudman");
        var brian = await host.AddArtistAsync("Brian Eaton");
        var sixPm = new DateTimeOffset(2026, 10, 24, 18, 0, 0, TimeSpan.FromHours(-5));

        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel(
            "  Portrait Mode ", Start, End, Summary: "A group show.",
            Reception: new(sixPm, sixPm.AddHours(3), "Free and open to all"), ArtistIds: [brian, harry]));

        Assert.Equal(("Portrait Mode", "portrait-mode", PublishStatus.Draft), (exhibition.Title, exhibition.Slug, exhibition.Status));
        Assert.Equal(["Brian Eaton", "Harry Sudman"], exhibition.Artists.Select(artist => artist.Name));
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero), exhibition.Reception!.StartsAt);
        Assert.Equal("America/Chicago", exhibition.DisplayTimeZone);
        Assert.False(exhibition.SlugLocked);

        var stored = (await host.StoredAsync(exhibition.Id))!;
        Assert.Equal(EntityTag.Format(stored.RowVersion), exhibition.ETag);
        Assert.Equal([brian, harry], stored.Artists.OrderBy(credit => credit.Position).Select(credit => credit.ArtistId));
        Assert.Equal(["exhibition.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task ATakenSlugIsRefusedOnTheSlugField()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new ExhibitionCreateViewModel("Queue", Start, End));

        var second = await host.WithFacadeAsync(facade => facade.CreateAsync(new ExhibitionCreateViewModel("QUEUE", Start, End), Editor, Ct));

        Assert.Equal(ContentErrorCodes.SlugTaken, second.Error!.Code);
        Assert.Contains("slug", second.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task AnArchivedArtistCannotBeCreditedButADraftOneCan()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var draft = await host.AddArtistAsync("New Artist", PublishStatus.Draft);
        var archived = await host.AddArtistAsync("Old Artist", PublishStatus.Archived);

        var refused = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: [draft, archived]), Editor, Ct));
        var unknown = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: [Guid.NewGuid()]), Editor, Ct));
        var accepted = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: [draft]));

        Assert.Contains("artistIds", refused.Error!.FieldErrors.Keys);
        Assert.Contains("artistIds", unknown.Error!.FieldErrors.Keys);
        Assert.Equal(PublishStatus.Draft, Assert.Single(accepted.Artists).Status);
    }

    [Fact]
    public async Task AnEditReplacesReordersAndClearsTheCredits()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var (a, b, c) = (await host.AddArtistAsync("Ann A"), await host.AddArtistAsync("Ben B"), await host.AddArtistAsync("Cal C"));
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: [a, b]));

        var reordered = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(ArtistIds: [c, b]), exhibition.ETag, Editor, Ct)));
        Assert.Equal(["Cal C", "Ben B"], reordered.Artists.Select(artist => artist.Name));
        Assert.NotEqual(exhibition.ETag, reordered.ETag);

        var cleared = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(ArtistIds: []), reordered.ETag, Editor, Ct)));
        Assert.Empty(cleared.Artists);
        Assert.Equal(0, await host.QueryAsync(context => context.Set<ExhibitionArtist>().CountAsync(Ct)));
    }

    [Fact]
    public async Task AnArtistArchivedAfterBeingCreditedStaysCreditedAndShowsAsArchived()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var jane = await host.AddArtistAsync("Jane Doe");
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Solo", Start, End, ArtistIds: [jane]));
        await host.ExecuteAsync(async context =>
        {
            (await context.Set<Artist>().SingleAsync(artist => artist.Id == jane, Ct)).Status = PublishStatus.Archived;
            await context.SaveChangesAsync(Ct);
        });

        var read = await Ok(host.WithFacadeAsync(facade => facade.GetAsync(exhibition.Id, Ct)));

        Assert.Equal(PublishStatus.Archived, Assert.Single(read.Artists).Status);
    }

    [Fact]
    public async Task PublishWaitsForASummaryAndTheHerosAltText()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var hero = await host.AddImageAsync(altText: null);
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End, HeroMediaId: hero));

        var refused = await host.WithFacadeAsync(facade => facade.PublishAsync(exhibition.Id, exhibition.ETag, Editor, Ct));
        Assert.Equal(["heroMediaId", "summary"], refused.Error!.FieldErrors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(exhibition.Id))!.Status);

        await SetAltTextAsync(host, hero, "Reclaimed wood boats hung from the gallery ceiling");
        var withSummary = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(Summary: "New work."), exhibition.ETag, Editor, Ct)));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(exhibition.Id, withSummary.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Published, published.Status);
        Assert.Equal(host.Time.GetUtcNow(), published.PublishedAt);
        Assert.True(published.SlugLocked);
        Assert.Contains("exhibition.published", await host.AuditActionsAsync());
    }

    [Fact]
    public async Task TheLifecycleRunsDraftPublishedDraftArchivedAndKeepsTheFirstPublishTime()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End, Summary: "A show."));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(exhibition.Id, exhibition.ETag, Editor, Ct)));
        host.Time.Advance(TimeSpan.FromDays(1));
        var unpublished = await Ok(host.WithFacadeAsync(facade => facade.UnpublishAsync(exhibition.Id, published.ETag, Editor, Ct)));
        var republished = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(exhibition.Id, unpublished.ETag, Editor, Ct)));
        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(exhibition.Id, republished.ETag, ExhibitionsTestHost.AdminId, Ct)));

        Assert.Equal(PublishStatus.Draft, unpublished.Status);
        Assert.Equal(published.PublishedAt, republished.PublishedAt);
        Assert.Equal(PublishStatus.Archived, archived.Status);
        Assert.Equal(ExhibitionsTestHost.AdminId, archived.UpdatedBy);
        Assert.Equal(
            ["exhibition.archived", "exhibition.created", "exhibition.published", "exhibition.published", "exhibition.unpublished"],
            (await host.AuditActionsAsync()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task IllegalTransitionsAreRefusedAndChangeNothing()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End, Summary: "A show."));

        var unpublishDraft = await host.WithFacadeAsync(facade => facade.UnpublishAsync(exhibition.Id, exhibition.ETag, Editor, Ct));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(exhibition.Id, exhibition.ETag, Editor, Ct)));
        var publishTwice = await host.WithFacadeAsync(facade => facade.PublishAsync(exhibition.Id, published.ETag, Editor, Ct));
        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(exhibition.Id, published.ETag, ExhibitionsTestHost.AdminId, Ct)));
        var publishArchived = await host.WithFacadeAsync(facade => facade.PublishAsync(exhibition.Id, archived.ETag, Editor, Ct));

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, unpublishDraft.Error!.Code);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, publishTwice.Error!.Code);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, publishArchived.Error!.Code);
        Assert.Equal(archived.ETag, EntityTag.Format((await host.StoredAsync(exhibition.Id))!.RowVersion));
    }

    [Fact]
    public async Task AStaleIfMatchIsAConflictAndTheRowAndCreditsAreUnchanged()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var (a, b) = (await host.AddArtistAsync("Ann A"), await host.AddArtistAsync("Ben B"));
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End, Summary: "A show.", ArtistIds: [a]));
        var edited = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(Body: "Hung in the East gallery."), exhibition.ETag, Editor, Ct)));

        // A second editor still holding the first version.
        var stale = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(ArtistIds: [b]), exhibition.ETag, "editor-2", Ct));
        var stalePublish = await host.WithFacadeAsync(facade => facade.PublishAsync(exhibition.Id, exhibition.ETag, "editor-2", Ct));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, stalePublish.Error!.Code);
        var stored = (await host.StoredAsync(exhibition.Id))!;
        Assert.Equal([a], stored.Artists.Select(credit => credit.ArtistId));
        Assert.Equal(PublishStatus.Draft, stored.Status);
        Assert.Equal(edited.ETag, EntityTag.Format(stored.RowVersion));
    }

    [Fact]
    public async Task ASlugChangeOnceEverPublishedStagesARedirect()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End, Summary: "A show."));

        var moved = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(Slug: "the-show"), exhibition.ETag, Editor, Ct)));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(exhibition.Id, moved.ETag, Editor, Ct)));
        var renamed = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(Slug: "show"), published.ETag, Editor, Ct)));

        Assert.Equal("show", renamed.Slug);
        var payload = Assert.Single(await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == "redirects.slug-changed").Select(message => message.PayloadJson).ToListAsync(Ct)));
        var change = Arts6311.Domain.Managers.Outbox.Events.RedirectSlugChanged.TryParse(payload)!;
        Assert.Equal(("exhibition", "/exhibitions/the-show", "/exhibitions/show"), (change.ContentType, change.FromPath, change.ToPath));
    }

    [Fact]
    public async Task AFailedRenameLeavesNeitherTheNewSlugNorItsRedirectMessage()
    {
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct, failAudit);
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End, Summary: "A show."));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(exhibition.Id, exhibition.ETag, Editor, Ct)));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(Slug: "the-show"), published.ETag, Editor, Ct)));

        Assert.Equal("show", (await host.StoredAsync(exhibition.Id))!.Slug);
        Assert.Equal(0, await host.QueryAsync(context => context.OutboxMessages.CountAsync(Ct)));
    }

    [Fact]
    public async Task ADateEditThatWouldEndTheShowBeforeItStartsIsRefused()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End));

        var refused = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(StartDate: End.AddDays(1)), exhibition.ETag, Editor, Ct));

        Assert.Contains("startDate", refused.Error!.FieldErrors.Keys);
        Assert.Equal(Start, (await host.StoredAsync(exhibition.Id))!.StartDate);
    }

    [Fact]
    public async Task AReceptionIsSetReplacedAndRemoved()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var sixPm = new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero);
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End));

        var set = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(Reception: new(sixPm, sixPm.AddHours(3), "Free")), exhibition.ETag, Editor, Ct)));
        var removed = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(RemoveReception: true), set.ETag, Editor, Ct)));

        Assert.Equal(new ExhibitionReceptionServiceModel(sixPm, sixPm.AddHours(3), "Free"), set.Reception);
        Assert.Null(removed.Reception);
        Assert.Null((await host.StoredAsync(exhibition.Id))!.ReceptionNote);
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheExhibitionNorItsCreditsNorItsAuditRow()
    {
        // backend.md: the real-database test that the write and its bookkeeping commit together or not at all.
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct, failAudit);
        var jane = await host.AddArtistAsync("Jane Doe");

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade =>
            facade.CreateAsync(new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: [jane]), Editor, Ct)));

        Assert.True(failAudit.Fired);
        Assert.Equal(0, await host.QueryAsync(context => context.Set<Exhibition>().CountAsync(Ct)));
        Assert.Equal(0, await host.QueryAsync(context => context.Set<ExhibitionArtist>().CountAsync(Ct)));
        Assert.Empty(await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AFailedCreditEditLeavesTheOldCreditsAndVersion()
    {
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct, failAudit);
        var (a, b) = (await host.AddArtistAsync("Ann A"), await host.AddArtistAsync("Ben B"));
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: [a]));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(ArtistIds: [b, a]), exhibition.ETag, Editor, Ct)));

        var stored = (await host.StoredAsync(exhibition.Id))!;
        Assert.Equal([a], stored.Artists.Select(credit => credit.ArtistId));
        Assert.Equal(exhibition.ETag, EntityTag.Format(stored.RowVersion));
    }

    [Fact(Skip = "Prompt 2.8: publish and unpublish stage site.revalidate-requested in the same save (content.md, ADR 0031, 0033). Unskip and implement there.")]
    public async Task PublishStagesARevalidationInTheSameSave()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End, Summary: "A show."));

        await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(exhibition.Id, exhibition.ETag, Editor, Ct)));

        var types = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking().Select(message => message.Type).ToListAsync(Ct));
        Assert.Contains("site.revalidate-requested", types);
    }

    [Fact]
    public async Task MediaRefusesToDeleteAHeroInUseAndAllowsItOnceRemoved()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        var hero = await host.AddImageAsync("Boats");
        var exhibition = await CreateAsync(host, new ExhibitionCreateViewModel("Show", Start, End, HeroMediaId: hero));

        Assert.Equal(MediaErrorCodes.InUse, (await DeleteImageAsync(host, hero)).Error!.Code);

        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(RemoveHero: true), exhibition.ETag, Editor, Ct)));

        Assert.True((await DeleteImageAsync(host, hero)).Succeeded);
    }

    [Fact]
    public async Task TheListPagesNewestFirstAndACursorOnlyFitsItsOwnFilter()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct);
        foreach (var (title, month) in new[] { ("March", 3), ("June", 6), ("September", 9) })
        {
            await CreateAsync(host, new ExhibitionCreateViewModel(title, new DateOnly(2026, month, 1), new DateOnly(2026, month, 28)));
        }

        var first = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new ExhibitionListViewModel(Limit: 2), Ct)));
        var second = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new ExhibitionListViewModel(first.NextCursor, Limit: 2), Ct)));
        var otherFilter = await host.WithFacadeAsync(facade => facade.ListAsync(
            new ExhibitionListViewModel(first.NextCursor, Limit: 2, Search: "e"), Ct));

        Assert.Equal(["September", "June"], first.Items.Select(row => row.Title));
        Assert.Equal(["March"], second.Items.Select(row => row.Title));
        Assert.Null(second.NextCursor);
        Assert.Equal(ErrorCodes.CursorInvalid, otherFilter.Error!.Code);
    }

    [Fact]
    public async Task TheDevelopmentSeedAddsTheSpikeShowsOnceCreditingRosterArtistsInOrder()
    {
        await using var host = await ExhibitionsTestHost.CreateAsync(Ct, withSeeders: true);

        await host.SeedAsync(Ct);
        var queue = await host.QueryAsync(context => context.Set<Exhibition>().AsNoTracking().SingleAsync(e => e.Slug == "queue", Ct));
        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            queue.Id, new ExhibitionUpdateViewModel(Body: "Edited by staff."), EntityTag.Format(queue.RowVersion), Editor, Ct)));
        await host.SeedAsync(Ct);

        var all = await host.QueryAsync(context => context.Set<Exhibition>().AsNoTracking().ToListAsync(Ct));
        Assert.Equal(ExhibitionDevelopmentSeeder.Shows.Count, all.Count);
        Assert.All(all, exhibition => Assert.Equal(PublishStatus.Published, exhibition.Status));
        Assert.Equal("Edited by staff.", all.Single(exhibition => exhibition.Slug == "queue").Body);

        // Portrait Mode credits seven roster artists, in the spike's order; Boats & Pianos' artist is not on the roster.
        var portraitMode = all.Single(exhibition => exhibition.Slug == "portrait-mode");
        var credits = await Ok(host.WithFacadeAsync(facade => facade.GetAsync(portraitMode.Id, Ct)));
        Assert.Equal(
            ["Harry Sudman", "Jeffrey Brooks", "Eddwin Meyers", "Alejandra Guerrero", "Hector Hernandez", "Adrian Zavala", "Brian Eaton"],
            credits.Artists.Select(artist => artist.Name));

        var boats = await Ok(host.WithFacadeAsync(facade => facade.GetAsync(all.Single(exhibition => exhibition.Slug == "boats-pianos").Id, Ct)));
        Assert.Empty(boats.Artists);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero), boats.Reception!.StartsAt);
        Assert.Equal(ExhibitionDevelopmentSeeder.SeedActor, portraitMode.UpdatedBy);
        Assert.Single(await host.AuditActionsAsync(), action => action == "exhibition.seeded");
    }

    private static async Task<ExhibitionServiceModel> CreateAsync(ExhibitionsTestHost host, ExhibitionCreateViewModel create) =>
        await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(create, Editor, Ct)));

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.True(result.Succeeded, result.Error?.Code + ": " + result.Error?.Message);
        return result.Value!;
    }

    private static Task SetAltTextAsync(ExhibitionsTestHost host, Guid assetId, string altText) =>
        host.ExecuteAsync(async context =>
        {
            var asset = await context.Set<MediaAsset>().SingleAsync(row => row.Id == assetId, Ct);
            asset.AltText = altText;
            await context.SaveChangesAsync(Ct);
        });

    private static async Task<OperationResult> DeleteImageAsync(ExhibitionsTestHost host, Guid assetId)
    {
        var version = await host.QueryAsync(context =>
            context.Set<MediaAsset>().AsNoTracking().Where(row => row.Id == assetId).Select(row => row.RowVersion).SingleAsync(Ct));

        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediaFacade>()
            .DeleteAsync(assetId, EntityTag.Format(version), ExhibitionsTestHost.AdminId, Ct);
    }
}
