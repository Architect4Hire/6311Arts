using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Domain.Modules.Redirects.Data.Entities;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;
using Arts6311.Tests.Media.Infrastructure;
using Arts6311.Tests.Redirects.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Redirects;

/// <summary>
/// The module end to end through its facade, on a real (SQLite) database: staff writes refuse loops and chains,
/// slug changes keep every chain one hop whatever order they arrive in, and a rename travels from an artist's save to
/// a redirect through the outbox.
/// </summary>
public sealed class RedirectsModuleTests
{
    private const string Admin = RedirectsTestHost.AdminId;

    private static readonly DateTimeOffset T1 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddDays(1);
    private static readonly DateTimeOffset T3 = T1.AddDays(2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Every <c>site.revalidate-requested</c> staged so far, oldest first, parsed.</summary>
    private static async Task<List<SiteRevalidateRequested>> StagedAsync(RedirectsTestHost host)
    {
        var rows = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == SiteRevalidateRequested.MessageType)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Select(message => message.PayloadJson)
            .ToListAsync());

        return [.. rows.Select(payload => SiteRevalidateRequested.TryParse(payload)!)];
    }

    // ---- Staff writes ----

    [Fact]
    public async Task AStaffRedirectIsStoredInNormalFormAsManualAndAudited()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);

        var redirect = await CreateAsync(host, new RedirectCreateViewModel(" /Old-Gallery/ ", "/Exhibitions"));

        Assert.Equal(("/old-gallery", "/exhibitions", 301, RedirectSource.Manual), (redirect.FromPath, redirect.ToPath, redirect.StatusCode, redirect.Source));
        Assert.Equal(["redirect.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task TwoSpellingsOfOneOldAddressAreOneRedirect()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new RedirectCreateViewModel("/gallery", "/exhibitions"));

        var again = await host.WithFacadeAsync(facade => facade.CreateAsync(new RedirectCreateViewModel("/Gallery/", "/classes"), Admin, Ct));

        Assert.Equal(RedirectErrorCodes.FromTaken, again.Error!.Code);
        Assert.Contains("fromPath", again.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task ARedirectToItselfIsRefused()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);

        var result = await host.WithFacadeAsync(facade => facade.CreateAsync(new RedirectCreateViewModel("/gallery", "/Gallery/#top"), Admin, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Contains("toPath", result.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task ALoopIsRefusedAtWriteTime()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new RedirectCreateViewModel("/a", "/b"));

        var loop = await host.WithFacadeAsync(facade => facade.CreateAsync(new RedirectCreateViewModel("/b", "/a"), Admin, Ct));

        Assert.Equal(RedirectErrorCodes.LoopOrChain, loop.Error!.Code);
        Assert.Contains("loop", loop.Error.FieldErrors["toPath"][0], StringComparison.Ordinal);
        Assert.Equal(["/a → /b"], await host.TableAsync());
    }

    [Fact]
    public async Task AChainIsRefusedFromEitherEnd()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new RedirectCreateViewModel("/b", "/c"));

        // a → b would be a → b → c: the destination already redirects onward.
        var onward = await host.WithFacadeAsync(facade => facade.CreateAsync(new RedirectCreateViewModel("/a", "/b"), Admin, Ct));
        // c → d would be b → c → d: something already arrives at the old address.
        var incoming = await host.WithFacadeAsync(facade => facade.CreateAsync(new RedirectCreateViewModel("/c", "/d"), Admin, Ct));

        Assert.Equal(RedirectErrorCodes.LoopOrChain, onward.Error!.Code);
        Assert.Contains("toPath", onward.Error.FieldErrors.Keys);
        Assert.Contains("/c", onward.Error.FieldErrors["toPath"][0], StringComparison.Ordinal);
        Assert.Equal(RedirectErrorCodes.LoopOrChain, incoming.Error!.Code);
        Assert.Contains("fromPath", incoming.Error.FieldErrors.Keys);
        Assert.Equal(["/b → /c"], await host.TableAsync());
    }

    [Fact]
    public async Task AChainThroughAFragmentOrToAnotherSiteIsJudgedByItsAddress()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new RedirectCreateViewModel("/b", "https://squareup.com/store/6311arts"));

        var throughFragment = await host.WithFacadeAsync(facade => facade.CreateAsync(new RedirectCreateViewModel("/a", "/b#top"), Admin, Ct));
        var external = await CreateAsync(host, new RedirectCreateViewModel("/c", "https://squareup.com/store/6311arts"));

        Assert.Equal(RedirectErrorCodes.LoopOrChain, throughFragment.Error!.Code);
        Assert.Equal(RedirectSource.Manual, external.Source);
    }

    [Fact]
    public async Task EveryStaffWriteStagesOneRevalidationNamingBothEndsOfTheHop()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);

        var created = await CreateAsync(host, new RedirectCreateViewModel("/gallery", "/exhibitions"));
        var updated = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            created.Id, new RedirectUpdateViewModel(ToPath: "/artists"), created.ETag, Admin, Ct)));
        var deleted = await host.WithFacadeAsync(facade => facade.DeleteAsync(created.Id, updated.ETag, Admin, Ct));
        Assert.True(deleted.Succeeded);

        var staged = await StagedAsync(host);

        Assert.Equal(3, staged.Count);
        Assert.All(staged, message => Assert.Equal("redirect", message.EntityType));
        Assert.All(staged, message => Assert.Equal(created.Id, message.EntityId));

        // The create names the hop it made; the edit and the delete name the hop as it then stood. All three
        // commit in the same save as their write, and the clock does not move between them, so they are compared
        // as a set.
        Assert.Equal(
            ["/gallery,/artists", "/gallery,/artists", "/gallery,/exhibitions"],
            staged.Select(message => string.Join(',', message.Paths)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnAddressACacheTagCannotHoldIsLeftOutRatherThanPoisoningTheMessage()
    {
        // RedirectPaths keeps a query, so an old address may hold a ':' that CacheTags.SitePath refuses, and an
        // external destination is not a site path at all. Either one in the payload would poison the message and
        // clear nothing; the type tag still clears the site's list (ADR 0036).
        await using var host = await RedirectsTestHost.CreateAsync(Ct);

        var withQuery = await CreateAsync(host, new RedirectCreateViewModel("/old?at=10:30", "/exhibitions"));
        var external = await CreateAsync(host, new RedirectCreateViewModel("/press", "https://example.org/press"));

        var staged = await StagedAsync(host);

        // The query is dropped — a tag would never match it — and the external destination is left out entirely.
        Assert.Equal(["/old", "/exhibitions"], staged.Single(message => message.EntityId == withQuery.Id).Paths);
        Assert.Equal(["/press"], staged.Single(message => message.EntityId == external.Id).Paths);
    }

    [Fact]
    public async Task AFailedOutboxInsertTakesTheRedirectRowWithIt()
    {
        // The other direction from the test below, and the one that catches a *second* SaveChangesAsync: if the
        // row were saved and the message staged afterwards in its own save, failing the outbox insert would leave
        // the redirect behind, live, with nothing telling the site's cache to stop serving the old answer. Failing
        // the audit insert cannot catch that, because the audit shares whichever save goes first (test-gap review).
        var failOutbox = new FailInsertInto("OutboxMessages");
        await using var host = await RedirectsTestHost.CreateAsync(Ct, failOutbox);

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(
            facade => facade.CreateAsync(new RedirectCreateViewModel("/gallery", "/exhibitions"), Admin, Ct)));

        Assert.True(failOutbox.Fired, "The interceptor never fired, so the save was not actually broken.");
        Assert.Empty(await host.TableAsync());
        Assert.Empty(await host.QueryAsync(context => context.OutboxMessages.AsNoTracking().ToListAsync(Ct)));
        Assert.Empty(await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AFailedRedirectWriteLeavesNeitherTheRowNorItsRevalidation()
    {
        // outbox.md's atomicity test for this module's staging path: the row and the message commit together.
        var failAudit = new FailInsertInto("AuditLogs");
        await using var host = await RedirectsTestHost.CreateAsync(Ct, failAudit);

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(
            facade => facade.CreateAsync(new RedirectCreateViewModel("/gallery", "/exhibitions"), Admin, Ct)));

        Assert.True(failAudit.Fired);
        Assert.Empty(await host.TableAsync());
        Assert.Equal(0, await host.QueryAsync(context => context.OutboxMessages.CountAsync(Ct)));
    }

    [Fact]
    public async Task AnEditIsCheckedAgainstEveryOtherRedirectButNotItself()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        var first = await CreateAsync(host, new RedirectCreateViewModel("/a", "/b"));
        await CreateAsync(host, new RedirectCreateViewModel("/c", "/d"));

        var statusOnly = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            first.Id, new RedirectUpdateViewModel(StatusCode: 302), first.ETag, Admin, Ct)));
        var chain = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            first.Id, new RedirectUpdateViewModel(ToPath: "/c"), statusOnly.ETag, Admin, Ct));
        var stale = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            first.Id, new RedirectUpdateViewModel(ToPath: "/e"), first.ETag, Admin, Ct));

        Assert.Equal(302, statusOnly.StatusCode);
        Assert.Equal(RedirectErrorCodes.LoopOrChain, chain.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
    }

    [Fact]
    public async Task ADeleteNeedsTheCurrentVersionAndIsAudited()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        var redirect = await CreateAsync(host, new RedirectCreateViewModel("/a", "/b"));

        var missing = await host.WithFacadeAsync(facade => facade.DeleteAsync(redirect.Id, null, Admin, Ct));
        var stale = await host.WithFacadeAsync(facade => facade.DeleteAsync(redirect.Id, EntityTag.Format(Guid.NewGuid()), Admin, Ct));
        var deleted = await host.WithFacadeAsync(facade => facade.DeleteAsync(redirect.Id, redirect.ETag, Admin, Ct));

        Assert.Equal(ErrorCodes.PreconditionRequired, missing.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.True(deleted.Succeeded);
        Assert.Empty(await host.TableAsync());
        Assert.Contains("redirect.deleted", await host.AuditActionsAsync());
    }

    [Fact]
    public async Task TheListPagesByOldAddressAndSearchesBothAddresses()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        foreach (var (from, to) in new[] { ("/c", "/x"), ("/a", "/y"), ("/b", "/classes") })
        {
            await CreateAsync(host, new RedirectCreateViewModel(from, to));
        }

        var first = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new RedirectListViewModel(Limit: 2), Ct)));
        var second = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new RedirectListViewModel(first.NextCursor, Limit: 2), Ct)));
        var search = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new RedirectListViewModel(Search: "CLASS"), Ct)));

        Assert.Equal(["/a", "/b"], first.Items.Select(row => row.FromPath));
        Assert.Equal(["/c"], second.Items.Select(row => row.FromPath));
        Assert.Equal(["/b"], search.Items.Select(row => row.FromPath));
    }

    // ---- Slug changes ----

    [Fact]
    public async Task ASlugChangeRedirectsTheOldAddressAndIsIdempotent()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);

        Assert.True((await host.SlugChangeAsync("/artists/jane", "/artists/jane-doe", T1, Ct)).Succeeded);
        Assert.True((await host.SlugChangeAsync("/artists/jane", "/artists/jane-doe", T1, Ct)).Succeeded);

        Assert.Equal(["/artists/jane → /artists/jane-doe"], await host.TableAsync());
        var redirect = await host.QueryAsync(context => context.Set<Redirect>().AsNoTracking().SingleAsync(Ct));
        Assert.Equal((RedirectSource.SlugChange, "system:slug-change", 301), (redirect.Source, redirect.UpdatedBy, redirect.StatusCode));
        Assert.Single(await host.AuditActionsAsync(), action => action == "redirect.slug-changed");
    }

    [Fact]
    public async Task RenamingAgainCollapsesTheChainToOneHop()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);

        await host.SlugChangeAsync("/artists/a", "/artists/b", T1, Ct);
        await host.SlugChangeAsync("/artists/b", "/artists/c", T2, Ct);

        Assert.Equal(["/artists/a → /artists/c", "/artists/b → /artists/c"], await host.TableAsync());
    }

    [Fact]
    public async Task RenamesAppliedOutOfOrderEndTheSameWay()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);

        await host.SlugChangeAsync("/artists/b", "/artists/c", T2, Ct);
        await host.SlugChangeAsync("/artists/a", "/artists/b", T1, Ct);

        Assert.Equal(["/artists/a → /artists/c", "/artists/b → /artists/c"], await host.TableAsync());
    }

    [Fact]
    public async Task RenamingBackRetiresTheRedirectFromTheAddressThatIsLiveAgain()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);

        await host.SlugChangeAsync("/artists/a", "/artists/b", T1, Ct);
        await host.SlugChangeAsync("/artists/b", "/artists/a", T2, Ct);

        Assert.Equal(["/artists/b → /artists/a"], await host.TableAsync());
    }

    [Fact]
    public async Task RenamingBackOutOfOrderNeverRedirectsTheLiveAddress()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);

        await host.SlugChangeAsync("/artists/b", "/artists/a", T2, Ct);
        await host.SlugChangeAsync("/artists/a", "/artists/b", T1, Ct);

        Assert.Equal(["/artists/b → /artists/a"], await host.TableAsync());
    }

    [Fact]
    public async Task AStaffRedirectArrivingAtTheOldAddressFollowsTheRename()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        await host.AddAsync("/artist/jane", "/artists/jane#work", RedirectSource.Imported, T1);

        await host.SlugChangeAsync("/artists/jane", "/artists/jane-doe", T2, Ct);

        Assert.Equal(["/artist/jane → /artists/jane-doe#work", "/artists/jane → /artists/jane-doe"], await host.TableAsync());
    }

    [Fact]
    public async Task ANewerStaffRedirectIsNotOverwrittenByAnOlderRename()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        await host.AddAsync("/artists/jane", "/exhibitions", RedirectSource.Manual, T3);

        await host.SlugChangeAsync("/artists/jane", "/artists/jane-doe", T2, Ct);

        Assert.Equal(["/artists/jane → /exhibitions"], await host.TableAsync());
    }

    [Fact]
    public async Task ARenameTravelsFromTheArtistsSaveThroughTheOutboxToARedirect()
    {
        // The whole seam (ADR 0035): content save stages redirects.slug-changed → the Worker's dispatcher → handler →
        // Redirects facade.
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        var artist = await Ok(host.WithArtistsAsync(facade => facade.CreateAsync(new ArtistCreateViewModel("Jane Doe"), "editor-1", Ct)));
        var published = await Ok(host.WithArtistsAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, "editor-1", Ct)));
        await Ok(host.WithArtistsAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Slug: "jane-q-doe"), published.ETag, "editor-1", Ct)));

        Assert.Empty(await host.TableAsync());
        await host.DispatchAsync(Ct);

        Assert.Equal(["/artists/jane-doe → /artists/jane-q-doe"], await host.TableAsync());

        // The rename's own message. The publish and the rename each staged a site.revalidate-requested as well,
        // and those are delivered by the same pass (ADR 0036).
        var message = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .SingleAsync(row => row.Type == RedirectSlugChanged.MessageType, Ct));
        Assert.Equal(OutboxMessageStatus.Completed, message.Status);
        // Four messages in all, and the pass left no failure behind: the publish's revalidation, the rename's
        // own revalidation (the artist page is live, so an edit to it invalidates what shows it) and the rename's
        // slug change are all delivered, and the redirect the handler wrote staged a revalidation of its own,
        // which the next pass delivers (ADR 0036).
        var all = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToListAsync(Ct));

        Assert.Equal(4, all.Count);
        Assert.Equal(3, all.Count(row => row.Status == OutboxMessageStatus.Completed));
        var pending = Assert.Single(all, row => row.Status == OutboxMessageStatus.Pending);
        Assert.Equal(SiteRevalidateRequested.MessageType, pending.Type);
        Assert.All(all, row => Assert.Null(row.LastError));
    }

    [Fact]
    public async Task TheHandlerPoisonsAMalformedMessageAndAppliesAGoodOneOnce()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        using var scope = host.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IOutboxMessageHandler>(RedirectSlugChanged.MessageType);
        var good = new RedirectSlugChanged("page", Guid.NewGuid(), "/terms", "/terms-of-use", T1).Serialize();

        await Assert.ThrowsAsync<OutboxPoisonException>(() => handler.HandleAsync(
            new OutboxMessageEnvelope(Guid.NewGuid(), RedirectSlugChanged.MessageType, "{\"fromPath\":\"/a\"}", Guid.NewGuid(), 1), Ct));
        await Assert.ThrowsAsync<OutboxPoisonException>(() => handler.HandleAsync(
            new OutboxMessageEnvelope(Guid.NewGuid(), RedirectSlugChanged.MessageType,
                new RedirectSlugChanged("page", Guid.NewGuid(), "/admin", "/x", T1).Serialize(), Guid.NewGuid(), 1), Ct));

        var envelope = new OutboxMessageEnvelope(Guid.NewGuid(), RedirectSlugChanged.MessageType, good, Guid.NewGuid(), 1);
        await handler.HandleAsync(envelope, Ct);
        await handler.HandleAsync(envelope with { Attempt = 2 }, Ct);

        Assert.Equal(["/terms → /terms-of-use"], await host.TableAsync());
    }

    // ---- The site's list, and the seed ----

    [Fact]
    public async Task TheSiteListIsCachedSoAWriteShowsOnlyOnceItExpires()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new RedirectCreateViewModel("/a", "/b"));

        var first = await Ok(host.WithFacadeAsync(facade => facade.ListForSiteAsync(Ct)));
        await CreateAsync(host, new RedirectCreateViewModel("/c", "/d"));
        var cached = await Ok(host.WithFacadeAsync(facade => facade.ListForSiteAsync(Ct)));

        Assert.Equal([new SiteRedirectServiceModel("/a", "/b", 301)], first);
        Assert.Equal(first, cached);
    }

    [Fact]
    public async Task TheLegacySeedAddsTheSpikesFixedRedirectsOnce()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct, withSeeder: true);

        await host.SeedAsync(Ct);
        await host.SeedAsync(Ct);

        Assert.Equal(["/events → /classes", "/gallery → /exhibitions", "/submit-your-work → /submit"], await host.TableAsync());
        Assert.Single(await host.AuditActionsAsync(), action => action == "redirect.seeded");
    }

    [Fact]
    public async Task TheLegacySeedRunsOncePerDatabaseSoADeletionStays()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct, withSeeder: true);
        await host.SeedAsync(Ct);
        var events = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new RedirectListViewModel(Search: "/events"), Ct)));
        var deleted = await host.WithFacadeAsync(facade => facade.DeleteAsync(events.Items[0].Id, events.Items[0].ETag, Admin, Ct));

        await host.SeedAsync(Ct);

        Assert.True(deleted.Succeeded);
        Assert.Equal(["/gallery → /exhibitions", "/submit-your-work → /submit"], await host.TableAsync());
    }

    [Fact]
    public async Task ASeedThatWouldChainIsSkipped()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct, withSeeder: true);
        await host.AddAsync("/classes", "/workshops");

        await host.SeedAsync(Ct);

        Assert.DoesNotContain("/events → /classes", await host.TableAsync());
        Assert.Contains("/gallery → /exhibitions", await host.TableAsync());
    }

    [Fact]
    public async Task ACollapsedChainNeverCarriesTwoFragments()
    {
        await using var host = await RedirectsTestHost.CreateAsync(Ct);
        await host.AddAsync("/artists/b", "/artists/c#work", RedirectSource.Manual, T3);
        await host.AddAsync("/old", "/artists/a#bio", RedirectSource.Imported, T1);

        await host.SlugChangeAsync("/artists/a", "/artists/b", T2, Ct);

        Assert.Contains("/old → /artists/c#work", await host.TableAsync());
        Assert.Contains("/artists/a → /artists/c#work", await host.TableAsync());
    }

    private static async Task<RedirectServiceModel> CreateAsync(RedirectsTestHost host, RedirectCreateViewModel create) =>
        await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(create, Admin, Ct)));

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.True(result.Succeeded, result.Error?.Code + ": " + result.Error?.Message);
        return result.Value!;
    }
}
