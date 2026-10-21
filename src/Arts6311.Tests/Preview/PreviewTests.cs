using System.Text.Json;
using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Domain.Modules.Classes;
using Arts6311.Domain.Modules.Instructors;
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Arts6311.Domain.Modules.Memberships;
using Arts6311.Domain.Modules.Memberships.Facade;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;
using Arts6311.Domain.Modules.Shop;
using Arts6311.Domain.Modules.Shop.Facade;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using Arts6311.Domain.Modules.Exhibitions;
using Arts6311.Domain.Modules.Exhibitions.Facade;
using Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Domain.Modules.Pages;
using Arts6311.Domain.Modules.Pages.Facade;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;
using Arts6311.Domain.Modules.Preview;
using Arts6311.Domain.Modules.Preview.Facade;
using Arts6311.Domain.Modules.Preview.Managers;
using Arts6311.Domain.Modules.Preview.Managers.ViewModels;
using Arts6311.Tests.Infrastructure;
using Arts6311.Tests.Kernel;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Preview;

/// <summary>
/// Preview links (ADR 0036): what a token names, what the site reads back, and that everything unusable — expired,
/// tampered with, or a record since gone — is the same 404.
/// </summary>
public sealed class PreviewTests : IAsyncLifetime
{
    private const string Editor = "editor-1";

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly FakeTimeProvider time = new(Now);
    private SqliteArtsDatabase database = null!;
    private ServiceProvider services = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        database = await SqliteArtsDatabase.CreateAsync(Ct);

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddScoped(_ => database.NewContext());
        collection.AddArtsKernel();

        // The real Data Protection stack on a key ring in memory, so the token is signed and encrypted as it is in
        // the API (the blob-backed ring is the host's, ADR 0023).
        collection.AddDataProtection().SetApplicationName("Arts6311");
        collection.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryXmlRepository());

        collection.AddMediaModule();
        collection.AddArtistsModule();
        collection.AddExhibitionsModule();
        collection.AddPagesModule();
        collection.AddInstructorsModule();
        collection.AddClassesModule();
        collection.AddShopModule();
        collection.AddMembershipsModule();
        collection.AddPreviewModule();
        collection.Replace(ServiceDescriptor.Singleton<IMediaBlobGateway>(new FakeMediaBlobGateway()));

        services = collection.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task ALinkToADraftArtistReadsBackAsThatArtistWithNoIndex()
    {
        var artist = await NewArtistAsync();

        var link = await Ok(WithPreviewAsync(facade => facade.CreateLinkAsync(new PreviewLinkRequestViewModel("artist", artist), Ct)));
        var preview = await Ok(WithPreviewAsync(facade => facade.ReadAsync(link.Token, Ct)));

        Assert.Equal($"/preview/{link.Token}", link.Path);
        Assert.Equal(Now + PreviewPolicy.TokenLifetime, link.ExpiresAt);
        Assert.Equal("artist", preview.EntityType);
        Assert.Equal(artist, preview.EntityId);
        Assert.Equal("/artists/jane-doe", preview.Path);
        Assert.Equal(PublishStatus.Draft, preview.Status);
        Assert.True(preview.NoIndex);
        Assert.Equal("Jane Doe", preview.Artist!.Name);
        Assert.Null(preview.Exhibition);
        Assert.Null(preview.Page);
    }

    [Fact]
    public async Task EachPreviewableTypeAnswersWithItsOwnRecordAtItsOwnAddress()
    {
        var exhibitionId = await NewExhibitionAsync();
        var pageId = await NewPageAsync();
        var classId = await NewClassAsync();
        var itemId = await NewShopItemAsync();
        var planId = await NewMembershipPlanAsync();

        var exhibition = await Ok(ReadLinkAsync("exhibition", exhibitionId));
        var page = await Ok(ReadLinkAsync("page", pageId));
        var offering = await Ok(ReadLinkAsync("class-offering", classId));
        var piece = await Ok(ReadLinkAsync("shop-item", itemId));
        var plan = await Ok(ReadLinkAsync("membership-plan", planId));

        Assert.Equal("/exhibitions/show", exhibition.Path);
        Assert.Equal("Show", exhibition.Exhibition!.Title);
        Assert.Null(exhibition.Artist);

        // A page lives at the root (PagePolicy.SitePathPrefix).
        Assert.Equal("/terms", page.Path);
        Assert.Equal("Terms", page.Page!.Title);

        Assert.Equal("/classes/scratchboard", offering.Path);
        Assert.Equal("Scratchboard", offering.ClassOffering!.Title);

        Assert.Equal("/shop/copper-cuff", piece.Path);
        Assert.Equal("Copper Cuff", piece.ShopItem!.Title);

        // A membership tier has no address of its own: the preview opens the page every tier shares (ADR 0040).
        Assert.Equal("/memberships", plan.Path);
        Assert.Equal("Gray", plan.MembershipPlan!.Name);
    }

    [Fact]
    public async Task AnExpiredLinkIsNotFound()
    {
        var artist = await NewArtistAsync();

        // Minted with the same key and purpose but already out of time. Data Protection validates the expiry it
        // embedded against the system clock, so this is how an expired link is reproduced without waiting half an
        // hour (see PreviewTokens' remarks).
        var expiredToken = Protector().Protect($"artist:{artist:N}", DateTimeOffset.UtcNow.AddMinutes(-1));

        var expired = await WithPreviewAsync(facade => facade.ReadAsync(expiredToken, Ct));
        var live = await WithPreviewAsync(facade => facade.ReadAsync(
            Protector().Protect($"artist:{artist:N}", PreviewPolicy.TokenLifetime), Ct));

        Assert.Equal(ErrorCodes.NotFound, expired.Error!.Code);
        Assert.True(live.Succeeded);
    }

    [Fact]
    public async Task ATamperedOrForeignTokenIsNotFoundAndSaysNoMore()
    {
        var artist = await NewArtistAsync();
        var link = await Ok(WithPreviewAsync(facade => facade.CreateLinkAsync(new PreviewLinkRequestViewModel("artist", artist), Ct)));

        // A token another purpose minted, for the same payload: it must not open a preview (PreviewPolicy.ProtectorPurpose).
        var foreign = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Arts6311.SomethingElse.v1")
            .ToTimeLimitedDataProtector()
            .Protect($"artist:{artist:N}", PreviewPolicy.TokenLifetime);

        var answers = new[]
        {
            await WithPreviewAsync(facade => facade.ReadAsync(link.Token[..^4] + "AAAA", Ct)),
            await WithPreviewAsync(facade => facade.ReadAsync(foreign, Ct)),
            await WithPreviewAsync(facade => facade.ReadAsync("not-a-token", Ct)),
            await WithPreviewAsync(facade => facade.ReadAsync(new string('a', PreviewPolicy.TokenMaxLength + 1), Ct)),
            await WithPreviewAsync(facade => facade.ReadAsync(null, Ct)),
        };

        Assert.All(answers, answer => Assert.Equal(ErrorCodes.NotFound, answer.Error!.Code));

        // Every refusal reads the same, so a holder learns nothing about what exists.
        Assert.Single(answers.Select(answer => answer.Error!.Message).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ALinkToARecordThatIsGoneIsTheSameAnswerAsABadToken()
    {
        var artist = await NewArtistAsync();
        var link = await Ok(WithPreviewAsync(facade => facade.CreateLinkAsync(new PreviewLinkRequestViewModel("artist", artist), Ct)));
        var bad = await WithPreviewAsync(facade => facade.ReadAsync("not-a-token", Ct));

        await using (var context = database.NewContext())
        {
            context.Remove(await context.Set<Artist>().SingleAsync(row => row.Id == artist, Ct));
            await context.SaveChangesAsync(Ct);
        }

        var gone = await WithPreviewAsync(facade => facade.ReadAsync(link.Token, Ct));

        Assert.Equal(bad.Error!.Code, gone.Error!.Code);
        Assert.Equal(bad.Error.Message, gone.Error.Message);
    }

    [Fact]
    public async Task ALinkIsRefusedForATypeWithoutPreviewsAndForARecordThatDoesNotExist()
    {
        var unknownType = await WithPreviewAsync(facade => facade.CreateLinkAsync(
            new PreviewLinkRequestViewModel("redirect", Guid.NewGuid()), Ct));
        var noRecord = await WithPreviewAsync(facade => facade.CreateLinkAsync(
            new PreviewLinkRequestViewModel("artist", Guid.NewGuid()), Ct));
        var noId = await WithPreviewAsync(facade => facade.CreateLinkAsync(
            new PreviewLinkRequestViewModel("artist", Guid.Empty), Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, unknownType.Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, noRecord.Error!.Code);
        Assert.Equal(ErrorCodes.ValidationFailed, noId.Error!.Code);
    }

    [Fact]
    public void EveryPreviewableTypeHasASiteAddressToRenderAt()
    {
        // The types and their prefixes are one map now, so they cannot drift (ADR 0036).
        Assert.Equal(PreviewPolicy.EntityTypes.Order(StringComparer.Ordinal), PreviewPolicy.PathPrefixes.Keys.Order(StringComparer.Ordinal));
        Assert.All(PreviewPolicy.PathPrefixes.Values, prefix => Assert.StartsWith("/", prefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task APreviewNamesNoStaffMemberAndCarriesNoRowVersion()
    {
        // ADR 0036, 0042: whoever holds the link reads the draft, so a preview is the site's own model — which has no
        // CMS bookkeeping to empty, the editor's user id above all — rather than the CMS's.
        var artist = await NewArtistAsync();

        var preview = await Ok(ReadLinkAsync("artist", artist));
        var json = JsonSerializer.Serialize(preview, Web);

        Assert.DoesNotContain("\"updatedBy\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"etag\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"legacyWordPressId\"", json, StringComparison.Ordinal);

        // What the page is made of is all still there, and the status rides beside it.
        Assert.Equal("Jane Doe", preview.Artist!.Name);
        Assert.Equal("jane-doe", preview.Artist.Slug);
        Assert.Equal(PublishStatus.Draft, preview.Status);
    }

    [Fact]
    public void ATokenCarriesNothingButTheRecordItNames()
    {
        var tokens = services.GetRequiredService<IPreviewTokens>();
        var id = Guid.NewGuid();

        var (token, expiresAt) = tokens.Create("artist", id);
        var subject = tokens.Read(token);

        Assert.Equal(Now + PreviewPolicy.TokenLifetime, expiresAt);
        Assert.Equal(new PreviewSubject("artist", id), subject);

        // The token is opaque: neither the type nor the id is readable from it.
        Assert.DoesNotContain("artist", token, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(id.ToString("N"), token, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> NewArtistAsync()
    {
        using var scope = services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IArtistsFacade>()
            .CreateAsync(new ArtistCreateViewModel("Jane Doe"), Editor, Ct);
        Assert.True(created.Succeeded);
        return created.Value!.Id;
    }

    private async Task<Guid> NewShopItemAsync()
    {
        using var scope = services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IShopFacade>().CreateAsync(
            new ShopItemCreateViewModel("Copper Cuff", Description: "<p>A forged copper cuff.</p>"),
            Editor,
            Ct);
        Assert.True(created.Succeeded);
        return created.Value!.Id;
    }

    private async Task<Guid> NewMembershipPlanAsync()
    {
        using var scope = services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IMembershipsFacade>().CreateAsync(
            new MembershipPlanCreateViewModel("Gray", Perks: ["5% off every class"], StartingPriceCents: 10_000),
            Editor,
            Ct);
        Assert.True(created.Succeeded);
        return created.Value!.Id;
    }

    private async Task<Guid> NewClassAsync()
    {
        using var scope = services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IClassesFacade>().CreateAsync(
            new ClassCreateViewModel("Scratchboard", Summary: "A class.", Runs: [new ClassRunViewModel(12, StartingPriceCents: 7500)]),
            Editor,
            Ct);
        Assert.True(created.Succeeded);
        return created.Value!.Id;
    }

    private async Task<Guid> NewExhibitionAsync()
    {
        using var scope = services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IExhibitionsFacade>().CreateAsync(
            new ExhibitionCreateViewModel("Show", new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), Summary: "A show."),
            Editor,
            Ct);
        Assert.True(created.Succeeded);
        return created.Value!.Id;
    }

    private async Task<Guid> NewPageAsync()
    {
        using var scope = services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IPagesFacade>()
            .CreateAsync(new PageCreateViewModel("Terms", Body: "<p>T.</p>"), Editor, Ct);
        Assert.True(created.Succeeded);
        return created.Value!.Id;
    }

    private async Task<OperationResult<Domain.Modules.Preview.Managers.ServiceModels.ContentPreviewServiceModel>> ReadLinkAsync(
        string entityType, Guid entityId)
    {
        var link = await Ok(WithPreviewAsync(facade => facade.CreateLinkAsync(new PreviewLinkRequestViewModel(entityType, entityId), Ct)));
        return await WithPreviewAsync(facade => facade.ReadAsync(link.Token, Ct));
    }

    /// <summary>The same key ring and purpose <c>PreviewTokens</c> uses, for tokens a test needs to shape itself.</summary>
    private ITimeLimitedDataProtector Protector() =>
        services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(PreviewPolicy.ProtectorPurpose)
            .ToTimeLimitedDataProtector();

    private async Task<T> WithPreviewAsync<T>(Func<IPreviewFacade, Task<T>> action)
    {
        using var scope = services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPreviewFacade>());
    }

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.Null(result.Error);
        return result.Value!;
    }
}
