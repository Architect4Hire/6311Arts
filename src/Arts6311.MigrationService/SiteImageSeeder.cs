using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers;

namespace Arts6311.MigrationService;

/// <summary>
/// The site's own pictures — the logo and the default social image — at their fixed ids, published, in every
/// environment (prompt 3.3, <see cref="MediaSiteImages"/>). The bytes ship inside this assembly, so a clean environment
/// has them without anyone uploading; a run that finds them already there changes nothing. The renditions the site
/// names are produced by the Worker, as for any upload.
/// </summary>
internal sealed partial class SiteImageSeeder(IMediaFacade media, ILogger<SiteImageSeeder> logger) : IDataSeeder
{
    private static readonly (Guid Id, string FileName, string AltText)[] Images =
    [
        (MediaSiteImages.Logo, MediaSiteImages.LogoFileName, MediaSiteImages.LogoAltText),
        (MediaSiteImages.OpenGraph, MediaSiteImages.OpenGraphFileName, MediaSiteImages.OpenGraphAltText),
    ];

    public string Name => "Site images";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        foreach (var (id, fileName, altText) in Images)
        {
            var result = await media.EnsureSiteImageAsync(
                id, fileName, () => Open(fileName), altText, MediaSiteImages.SeedActor, cancellationToken);

            // A picture Media refuses is a broken build, not a state to carry on from: the site would name a 404.
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"The site image {fileName} was refused ({result.Error!.Code}).");
            }

            LogEnsured(fileName, id);
        }
    }

    private static Stream Open(string fileName) =>
        typeof(SiteImageSeeder).Assembly.GetManifestResourceStream($"SiteImages.{fileName}")
        ?? throw new InvalidOperationException($"The site image {fileName} is not embedded in MigrationService.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Site image {FileName} is in place as {AssetId}.")]
    private partial void LogEnsured(string fileName, Guid assetId);
}
