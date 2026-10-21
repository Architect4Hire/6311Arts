using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;
using Arts6311.MigrationService;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arts6311.Tests.Media;

/// <summary>
/// The site's own pictures (prompt 3.3): seeded at fixed ids, published, in every environment, so the site can name
/// them by id; the seed can run on every start.
/// </summary>
public sealed class SiteImageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSeedPutsBothPicturesAtTheirIdsPublishedWithTheirAltText()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);

        await SeedAsync(host);

        var logo = await host.OriginalAsync(MediaSiteImages.Logo);
        var social = await host.OriginalAsync(MediaSiteImages.OpenGraph);
        Assert.Equal((MediaVisibility.Published, MediaAssetKind.Original, MediaSiteImages.LogoAltText), (logo.Visibility, logo.Kind, logo.AltText));
        Assert.Equal((MediaVisibility.Published, "image/jpeg", MediaSiteImages.OpenGraphAltText), (social.Visibility, social.MimeType, social.AltText));
        Assert.Equal(MediaSiteImages.SeedActor, logo.UploadedBy);

        // Renditions follow through the Worker, as for any upload: the site names 800.webp and 1600.jpg.
        await host.DispatchAsync(Ct);
        Assert.Contains((await host.RenditionsAsync(MediaSiteImages.Logo)).Select(r => r.RenditionName), name => name == "800.webp");
        Assert.Contains((await host.RenditionsAsync(MediaSiteImages.OpenGraph)).Select(r => r.RenditionName), name => name == "1600.jpg");
    }

    [Fact]
    public async Task ASecondRunChangesNothing()
    {
        await using var host = await MediaTestHost.CreateAsync(Ct);
        await SeedAsync(host);
        var before = await host.OriginalAsync(MediaSiteImages.Logo);

        await SeedAsync(host);

        var after = await host.OriginalAsync(MediaSiteImages.Logo);
        Assert.Equal(before.RowVersion, after.RowVersion);
    }

    [Fact]
    public async Task TheSameBytesInTheLibraryDoNotStopTheSeed()
    {
        // A staff member may well have uploaded the logo themselves; the site still gets its own at the fixed id.
        await using var host = await MediaTestHost.CreateAsync(Ct);
        var bytes = Resource(MediaSiteImages.LogoFileName);
        Assert.True((await host.UploadAsync(bytes, "logo.png", cancellationToken: Ct)).Succeeded);

        await SeedAsync(host);

        Assert.Equal(MediaVisibility.Published, (await host.OriginalAsync(MediaSiteImages.Logo)).Visibility);
    }

    [Fact]
    public async Task AStaffUploadCannotChooseItsIdOrPublishItself()
    {
        // The fixed id and the published start belong to the seed alone: the CMS route builds its view model without them.
        await using var host = await MediaTestHost.CreateAsync(Ct);

        var upload = await host.UploadAsync(Arts6311.Tests.Media.Infrastructure.ImageFixtures.Jpeg(800, 600), "staff.jpg", cancellationToken: Ct);

        Assert.NotEqual(MediaSiteImages.Logo, upload.Value!.Id);
        Assert.Equal(MediaVisibility.Draft, upload.Value.Visibility);
    }

    private static async Task SeedAsync(MediaTestHost host)
    {
        using var scope = host.CreateScope();
        var seeder = new SiteImageSeeder(MediaTestHost.Resolve<IMediaFacade>(scope), NullLogger<SiteImageSeeder>.Instance);
        await seeder.SeedAsync(Ct);
    }

    private static byte[] Resource(string fileName)
    {
        using var stream = typeof(SiteImageSeeder).Assembly.GetManifestResourceStream($"SiteImages.{fileName}")!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
