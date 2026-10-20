using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Domain.Modules.Redirects.Data.Entities;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;
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
        var message = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking().SingleAsync(Ct));
        Assert.Equal(OutboxMessageStatus.Completed, message.Status);
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
