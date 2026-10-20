using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Tests.Artists.Infrastructure;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Artists;

/// <summary>
/// The module end to end through its facade, on a real (SQLite) database: the rules, the audit trail, the atomic
/// save, and the portrait's relationship with Media.
/// </summary>
public sealed class ArtistsModuleTests
{
    private const string Editor = ArtistsTestHost.EditorId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreatedArtistIsADraftWithASlugFromItsNameAndAnAuditRow()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);

        var artist = await CreateAsync(host, new ArtistCreateViewModel("  Socorro Muciño ", Medium: " Painting ", Bio: "One.\r\n\r\nTwo."));

        Assert.Equal("Socorro Muciño", artist.Name);
        Assert.Equal("socorro-mucino", artist.Slug);
        Assert.Equal("Painting", artist.Medium);
        Assert.Equal("One.\n\nTwo.", artist.Bio);
        Assert.Equal(PublishStatus.Draft, artist.Status);
        Assert.False(artist.SlugLocked);
        Assert.Null(artist.PublishedAt);
        Assert.Equal(Editor, artist.UpdatedBy);

        var stored = (await host.StoredAsync(artist.Id))!;
        Assert.Equal(EntityTag.Format(stored.RowVersion), artist.ETag);
        Assert.Equal(["artist.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task ATakenSlugIsRefusedOnTheSlugField()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var second = await host.WithFacadeAsync(facade => facade.CreateAsync(new ArtistCreateViewModel("Jane  Doe"), Editor, Ct));

        Assert.Equal(ContentErrorCodes.SlugTaken, second.Error!.Code);
        Assert.Contains("slug", second.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task APortraitMustBeAnOriginalInTheLibraryButMayLackAltTextOnADraft()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var noAlt = await host.AddImageAsync(altText: null);
        var rendition = await host.AddImageAsync("A rendition", MediaAssetKind.Derived);

        var missing = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ArtistCreateViewModel("Jane Doe", PortraitMediaId: Guid.NewGuid()), Editor, Ct));
        var derived = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ArtistCreateViewModel("Jane Doe", PortraitMediaId: rendition), Editor, Ct));
        var draft = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe", PortraitMediaId: noAlt));

        Assert.Equal(ErrorCodes.ValidationFailed, missing.Error!.Code);
        Assert.Contains("portraitMediaId", missing.Error.FieldErrors.Keys);
        Assert.Equal(ErrorCodes.ValidationFailed, derived.Error!.Code);
        Assert.Equal(noAlt, draft.PortraitMediaId);
    }

    [Fact]
    public async Task PublishWaitsForThePortraitsAltText()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var portrait = await host.AddImageAsync(altText: null);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe", PortraitMediaId: portrait));

        var refused = await host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Contains("portraitMediaId", refused.Error.FieldErrors.Keys);
        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(artist.Id))!.Status);

        await SetAltTextAsync(host, portrait, "Jane in her studio");
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Published, published.Status);
        Assert.Equal(host.Time.GetUtcNow(), published.PublishedAt);
        Assert.True(published.SlugLocked);
        Assert.NotEqual(artist.ETag, published.ETag);
        Assert.Contains("artist.published", await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AnArtistWithoutAPortraitPublishesWithoutAskingMedia()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Published, published.Status);
    }

    [Fact]
    public async Task TheLifecycleRunsDraftPublishedDraftArchivedAndKeepsTheFirstPublishTime()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));
        host.Time.Advance(TimeSpan.FromDays(1));
        var unpublished = await Ok(host.WithFacadeAsync(facade => facade.UnpublishAsync(artist.Id, published.ETag, Editor, Ct)));
        var republished = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, unpublished.ETag, Editor, Ct)));
        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(artist.Id, republished.ETag, ArtistsTestHost.AdminId, Ct)));

        Assert.Equal(PublishStatus.Draft, unpublished.Status);
        Assert.Equal(published.PublishedAt, unpublished.PublishedAt);
        Assert.Equal(published.PublishedAt, republished.PublishedAt);
        Assert.Equal(PublishStatus.Archived, archived.Status);
        Assert.Equal(ArtistsTestHost.AdminId, archived.UpdatedBy);
        Assert.Equal(
            ["artist.archived", "artist.created", "artist.published", "artist.published", "artist.unpublished"],
            (await host.AuditActionsAsync()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task IllegalTransitionsAreRefusedAndChangeNothing()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var unpublishDraft = await host.WithFacadeAsync(facade => facade.UnpublishAsync(artist.Id, artist.ETag, Editor, Ct));
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, unpublishDraft.Error!.Code);

        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(artist.Id, artist.ETag, ArtistsTestHost.AdminId, Ct)));
        var publishArchived = await host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, archived.ETag, Editor, Ct));
        var archiveTwice = await host.WithFacadeAsync(facade => facade.ArchiveAsync(artist.Id, archived.ETag, ArtistsTestHost.AdminId, Ct));

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, publishArchived.Error!.Code);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, archiveTwice.Error!.Code);
        Assert.Equal(PublishStatus.Archived, (await host.StoredAsync(artist.Id))!.Status);
        Assert.Equal(EntityTag.Format((await host.StoredAsync(artist.Id))!.RowVersion), archived.ETag);
    }

    [Fact]
    public async Task PublishingTwiceIsRefusedRatherThanSilentlyIgnored()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        var again = await host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, published.ETag, Editor, Ct));

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, again.Error!.Code);
    }

    [Fact]
    public async Task AStaleIfMatchIsAConflictAndTheRowIsUnchanged()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var edited = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Medium: "Ceramics"), artist.ETag, Editor, Ct)));

        // A second editor still holding the first version.
        var stale = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Medium: "Painting"), artist.ETag, "editor-2", Ct));
        var stalePublish = await host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, "editor-2", Ct));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, stalePublish.Error!.Code);
        var stored = (await host.StoredAsync(artist.Id))!;
        Assert.Equal("Ceramics", stored.Medium);
        Assert.Equal(PublishStatus.Draft, stored.Status);
        Assert.Equal(edited.ETag, EntityTag.Format(stored.RowVersion));
    }

    [Fact]
    public async Task AWriteWithoutIfMatchOrWithAMalformedOneIsRefused()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var missing = await host.WithFacadeAsync(facade => facade.UpdateAsync(artist.Id, new ArtistUpdateViewModel(Active: false), null, Editor, Ct));
        var weak = await host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, $"W/{artist.ETag}", Editor, Ct));

        Assert.Equal(ErrorCodes.PreconditionRequired, missing.Error!.Code);
        Assert.Equal(ErrorCodes.ValidationFailed, weak.Error!.Code);
        Assert.Contains("ifMatch", weak.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task ASlugMovesFreelyUntilFirstPublishAndNeverAfterEvenWhenUnpublished()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var moved = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Slug: "jane-q-doe"), artist.ETag, Editor, Ct)));
        Assert.Equal("jane-q-doe", moved.Slug);

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, moved.ETag, Editor, Ct)));
        var whilePublished = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Slug: "jane-doe"), published.ETag, Editor, Ct));
        Assert.Equal(ContentErrorCodes.SlugLocked, whilePublished.Error!.Code);

        var unpublished = await Ok(host.WithFacadeAsync(facade => facade.UnpublishAsync(artist.Id, published.ETag, Editor, Ct)));
        var afterUnpublish = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Slug: "jane-doe"), unpublished.ETag, Editor, Ct));
        Assert.Equal(ContentErrorCodes.SlugLocked, afterUnpublish.Error!.Code);
        Assert.True(unpublished.SlugLocked);

        // Sending the slug it already has is not a change, so it is not refused.
        var same = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Slug: "jane-q-doe", Medium: "Ink"), unpublished.ETag, Editor, Ct));
        Assert.True(same.Succeeded);
    }

    [Fact]
    public async Task APublishedArtistCannotSwapInAPortraitWithoutAltText()
    {
        // media.md: published content never carries an image without alt text, whichever way the image arrives.
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var bare = await host.AddImageAsync(altText: null);
        var described = await host.AddImageAsync("Jane at the wheel");
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        var refused = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(PortraitMediaId: bare), published.ETag, Editor, Ct));
        var accepted = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(PortraitMediaId: described), published.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Contains("portraitMediaId", refused.Error.FieldErrors.Keys);
        Assert.Equal(described, accepted.Value!.PortraitMediaId);
    }

    [Fact(Skip = "Prompt 2.8: publish and unpublish stage site.revalidate-requested in the same save (content.md, ADR 0031). Unskip and implement there.")]
    public async Task PublishStagesARevalidationInTheSameSave()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        var types = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking().Select(message => message.Type).ToListAsync(Ct));
        Assert.Contains("site.revalidate-requested", types);
    }

    [Fact]
    public async Task AnEditCannotTakeAnotherArtistsSlug()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var john = await CreateAsync(host, new ArtistCreateViewModel("John Doe"));

        var result = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            john.Id, new ArtistUpdateViewModel(Slug: "jane-doe"), john.ETag, Editor, Ct));

        Assert.Equal(ContentErrorCodes.SlugTaken, result.Error!.Code);
    }

    [Fact]
    public async Task AnEditClearsSetsAndRemovesAndAnEmptyEditWritesNothing()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var portrait = await host.AddImageAsync("Jane");
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe", Medium: "Ink", Bio: "Bio", PortraitMediaId: portrait));

        var cleared = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Medium: "", Bio: "  ", Active: false, RemovePortrait: true), artist.ETag, Editor, Ct)));

        Assert.Null(cleared.Medium);
        Assert.Null(cleared.Bio);
        Assert.False(cleared.Active);
        Assert.Null(cleared.PortraitMediaId);

        var unchanged = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Name: "Jane Doe ", Active: false), cleared.ETag, Editor, Ct)));

        Assert.Equal(cleared.ETag, unchanged.ETag);
        Assert.Equal(2, (await host.AuditActionsAsync()).Count);
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheArtistNorItsAuditRow()
    {
        // backend.md: the real-database test that the write and its bookkeeping commit together or not at all.
        var failAudit = new FailInsertInto("AuditLogs");
        await using var host = await ArtistsTestHost.CreateAsync(Ct, failAudit);

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade =>
            facade.CreateAsync(new ArtistCreateViewModel("Jane Doe"), Editor, Ct)));

        Assert.True(failAudit.Fired);
        Assert.Equal(0, await host.QueryAsync(context => context.Set<Artist>().CountAsync(Ct)));
        Assert.Empty(await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AFailedTransitionLeavesTheStatusAndVersionAsTheyWere()
    {
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ArtistsTestHost.CreateAsync(Ct, failAudit);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        var stored = (await host.StoredAsync(artist.Id))!;
        Assert.Equal(PublishStatus.Draft, stored.Status);
        Assert.Null(stored.PublishedAt);
        Assert.Equal(artist.ETag, EntityTag.Format(stored.RowVersion));
        Assert.Equal(["artist.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task MediaRefusesToDeleteAPortraitInUseAndAllowsItOnceRemoved()
    {
        // The first real Restrict foreign key to MediaAsset: Media's delete guard finds it in the model (ADR 0030).
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var portrait = await host.AddImageAsync("Jane");
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe", PortraitMediaId: portrait));

        var inUse = await DeleteImageAsync(host, portrait);
        Assert.Equal(MediaErrorCodes.InUse, inUse.Error!.Code);

        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(RemovePortrait: true), artist.ETag, Editor, Ct)));

        Assert.True((await DeleteImageAsync(host, portrait)).Succeeded);
    }

    [Fact]
    public async Task TheListPagesByCursorAndACursorOnlyFitsItsOwnFilter()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        foreach (var name in new[] { "Ann", "Ben", "Cal" })
        {
            await CreateAsync(host, new ArtistCreateViewModel(name));
        }

        var first = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new ArtistListViewModel(Limit: 2), Ct)));
        var second = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new ArtistListViewModel(first.NextCursor, Limit: 2), Ct)));
        var otherFilter = await host.WithFacadeAsync(facade => facade.ListAsync(
            new ArtistListViewModel(first.NextCursor, Limit: 2, Status: PublishStatus.Draft), Ct));

        Assert.Equal(["Ann", "Ben"], first.Items.Select(row => row.Name));
        Assert.Equal(["Cal"], second.Items.Select(row => row.Name));
        Assert.Null(second.NextCursor);
        Assert.Equal(ErrorCodes.CursorInvalid, otherFilter.Error!.Code);
    }

    [Fact]
    public async Task TheDevelopmentSeedAddsTheSpikeRosterOnceAndNeverUndoesAnEdit()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct, withSeeder: true);

        await host.SeedAsync(Ct);
        var carrie = await host.QueryAsync(context => context.Set<Artist>().AsNoTracking().SingleAsync(a => a.Slug == "carrie-haase", Ct));
        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            carrie.Id, new ArtistUpdateViewModel(Medium: "Metalsmithing"), EntityTag.Format(carrie.RowVersion), Editor, Ct)));
        await host.SeedAsync(Ct);

        var all = await host.QueryAsync(context => context.Set<Artist>().AsNoTracking().ToListAsync(Ct));
        Assert.Equal(ArtistDevelopmentSeeder.Roster.Count, all.Count);
        Assert.All(all, artist => Assert.Equal(PublishStatus.Published, artist.Status));
        Assert.Equal("Jewelry", carrie.Medium);
        Assert.Equal("Metalsmithing", all.Single(artist => artist.Slug == "carrie-haase").Medium);
        Assert.Equal(ArtistDevelopmentSeeder.SeedActor, all.First(artist => artist.Slug == "piloto").UpdatedBy);
        Assert.Single(await host.AuditActionsAsync(), action => action == "artist.seeded");
    }

    private static async Task<ArtistServiceModel> CreateAsync(ArtistsTestHost host, ArtistCreateViewModel create) =>
        await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(create, Editor, Ct)));

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.True(result.Succeeded, result.Error?.Code + ": " + result.Error?.Message);
        return result.Value!;
    }

    private static Task SetAltTextAsync(ArtistsTestHost host, Guid assetId, string altText) =>
        host.ExecuteAsync(async context =>
        {
            var asset = await context.Set<MediaAsset>().SingleAsync(row => row.Id == assetId, Ct);
            asset.AltText = altText;
            await context.SaveChangesAsync(Ct);
        });

    private static async Task<OperationResult> DeleteImageAsync(ArtistsTestHost host, Guid assetId)
    {
        var version = await host.QueryAsync(context =>
            context.Set<MediaAsset>().AsNoTracking().Where(row => row.Id == assetId).Select(row => row.RowVersion).SingleAsync(Ct));

        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediaFacade>()
            .DeleteAsync(assetId, EntityTag.Format(version), "admin-1", Ct);
    }
}
