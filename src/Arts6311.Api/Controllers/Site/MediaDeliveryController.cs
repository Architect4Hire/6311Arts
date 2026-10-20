using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /media/{assetId}/{rendition}</c>: the only way an image's bytes leave the system (media.md "Never public
/// blob access"). The gateway forwards <c>/media/**</c> here, attaching a user token when there is a session.
/// </summary>
/// <remarks>
/// <para>
/// Unversioned, like <c>/webhooks/**</c>: it is a file URL the site writes into <c>&lt;img srcset&gt;</c>, not part
/// of the JSON API, so it stays out of the OpenAPI document (ADR 0030). Anonymous by design — drafts are then
/// refused by Business for anyone who is not signed-in staff, with the same 404 as a missing asset.
/// </para>
/// <para>
/// Caching: a published asset at its current <c>?v=</c> is immutable for a year; without it, it revalidates by its
/// strong ETag (the SHA-256 of the bytes). A draft is <c>private, no-cache</c>, so no shared cache keeps a copy.
/// </para>
/// </remarks>
[ApiController]
[Route("media/{id:guid}/{rendition}")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class MediaDeliveryController(IMediaFacade facade) : ControllerBase
{
    [HttpGet]
    [HttpHead]
    public async Task<IActionResult> GetAsync(
        Guid id,
        string rendition,
        [FromQuery(Name = "v")] string? version,
        CancellationToken cancellationToken)
    {
        var request = new MediaDeliveryViewModel(
            id,
            rendition,
            version,
            Request.Headers.IfNoneMatch.ToString(),
            HttpMethods.IsHead(Request.Method));

        var result = await facade.OpenAsync(
            request, User.IsCmsStaff() ? MediaViewer.Staff : MediaViewer.Public, cancellationToken);
        if (!result.Succeeded)
        {
            // A 404 here must not be stored: the same URL may be published a minute from now.
            Response.Headers.CacheControl = "no-store";
            return this.ToActionResult(result);
        }

        var content = result.Value!;
        Response.Headers.ETag = content.ETag;
        Response.Headers.CacheControl = content.Cacheability switch
        {
            MediaCacheability.Immutable => "public, max-age=31536000, immutable",
            MediaCacheability.Revalidate => "public, max-age=300, must-revalidate",
            _ => "private, no-cache",
        };

        if (content.NotModified)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        if (content.Content is null)
        {
            Response.ContentType = content.ContentType;
            Response.ContentLength = content.Length;
            return new EmptyResult();
        }

        // FileStreamResult disposes the blob stream once the body is written.
        return File(content.Content, content.ContentType, enableRangeProcessing: false);
    }
}
