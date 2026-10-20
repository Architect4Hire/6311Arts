using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Arts6311.Api.Controllers.Cms;

/// <summary>
/// The media library (media.md): upload, list, read, edit alt text / credit / preset, and delete.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The upload is streamed.</strong> The file part is handed to the facade as it arrives, so no layer holds
/// the whole file in memory; Business spools it to a self-deleting temp file under the 25 MB cap. The gateway's
/// <c>cms-media-upload</c> route and <see cref="RequestSizeLimitAttribute"/> below apply the same ceiling.
/// </para>
/// <para>Deletes are Admin-only (api-contract.md "CMS": deletes <c>CmsAdmin</c>) and audited.</para>
/// </remarks>
[ApiController]
[Route("api/v1/cms/media")]
[Authorize(Policy = ApiPolicies.CmsEditor)]
public sealed class CmsMediaController(IMediaFacade facade) : ControllerBase
{
    private const string FileField = "file";
    private const string AltTextField = "altText";
    private const string CreditField = "credit";
    private const string PresetField = "compressionPreset";

    /// <summary>A text field longer than this is not a text field.</summary>
    private const int MaxTextFieldBytes = 4096;

    /// <summary>
    /// Uploads one image (JPEG, PNG or WebP; no SVG). It is stored as a compressed master — downscaled to
    /// 2560 px on its longest side if larger, with GPS and other metadata removed and copyright kept — and its
    /// renditions are produced in the background.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MediaPolicy.MaxUploadRequestBytes)]
    [StreamedMultipartUpload(FileField, AltTextField, CreditField, PresetField)]
    [ProducesResponseType<MediaAssetServiceModel>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadAsync(CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType)
            || HeaderUtilities.RemoveQuotes(contentType.Boundary).Value is not { Length: > 0 and <= 200 } boundary)
        {
            return this.ToActionResult(Invalid<MediaAssetServiceModel>(FileField, "The body must be multipart/form-data with a boundary."));
        }

        var reader = new MultipartReader(boundary, Request.Body);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            // Not IsFormDisposition(): that means "a field without a file name", which the file part is not.
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                || !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? string.Empty;
            if (disposition.IsFileDisposition() && name == FileField)
            {
                var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue
                    ? disposition.FileNameStar
                    : disposition.FileName).Value ?? string.Empty;

                // A preset that was sent must be one of the names; a typo is refused, as the PATCH refuses it.
                MediaCompressionPreset? preset = null;
                if (fields.TryGetValue(PresetField, out var presetText))
                {
                    if (!Enum.GetNames<MediaCompressionPreset>().Contains(presetText, StringComparer.Ordinal))
                    {
                        return this.ToActionResult(Invalid<MediaAssetServiceModel>(
                            PresetField, $"compressionPreset must be one of {string.Join(", ", Enum.GetNames<MediaCompressionPreset>())}."));
                    }

                    preset = Enum.Parse<MediaCompressionPreset>(presetText);
                }

                var upload = new MediaUploadViewModel(
                    fileName,
                    section.Body,
                    fields.GetValueOrDefault(AltTextField),
                    fields.GetValueOrDefault(CreditField),
                    preset);

                var result = await facade.UploadAsync(upload, User.UserId(), cancellationToken);
                if (!result.Succeeded)
                {
                    return this.ToActionResult(result);
                }

                Response.Headers.ETag = result.Value!.ETag;
                return Created($"/api/v1/cms/media/{result.Value.Id}", result.Value);
            }

            if (!disposition.IsFileDisposition() && name is (AltTextField or CreditField or PresetField) && !fields.ContainsKey(name))
            {
                var text = await ReadTextAsync(section.Body, cancellationToken);
                if (text is null)
                {
                    return this.ToActionResult(Invalid<MediaAssetServiceModel>(name, $"The {name} field is too long."));
                }

                fields[name] = text;
            }
        }

        return this.ToActionResult(Invalid<MediaAssetServiceModel>(FileField, "The body has no file part named 'file'."));
    }

    /// <summary>The library: originals only, newest first, cursor-paged.</summary>
    [HttpGet]
    [ProducesResponseType<CursorPage<MediaAssetSummaryServiceModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "missingAltText")] bool? missingAltText,
        [FromQuery(Name = "status")] MediaProcessingStatus? status,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.ListAsync(new MediaListViewModel(cursor, limit, missingAltText, status), cancellationToken));

    /// <summary>One asset with its renditions. The <c>ETag</c> header is what an edit sends back as <c>If-Match</c>.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<MediaAssetServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        WithETag(await facade.GetAsync(id, cancellationToken));

    /// <summary>
    /// Alt text, credit and compression preset. A field left out is unchanged; an empty string clears it. A new
    /// preset re-renders the renditions in the background.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<MediaAssetServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] MediaAssetUpdateViewModel update,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken) =>
        WithETag(await facade.UpdateAsync(id, update, ifMatch, User.UserId(), cancellationToken));

    /// <summary>Deletes an original and its renditions. Refused with <c>media.in_use</c> while anything references it.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = ApiPolicies.CmsAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        this.ToNoContentResult(await facade.DeleteAsync(id, ifMatch, User.UserId(), cancellationToken));

    private IActionResult WithETag(OperationResult<MediaAssetServiceModel> result)
    {
        if (result.Succeeded)
        {
            Response.Headers.ETag = result.Value!.ETag;
        }

        return this.ToActionResult(result);
    }

    /// <summary>A refusal of the body's shape, keyed to the form field it is about.</summary>
    private static OperationResult<T> Invalid<T>(string field, string message) =>
        OperationResult.Failure<T>(OperationError.Validation(message, [(field, message)]));

    /// <returns>The field's text, or <c>null</c> when it is longer than <see cref="MaxTextFieldBytes"/>.</returns>
    private static async Task<string?> ReadTextAsync(Stream body, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxTextFieldBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await body.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
        {
            total += read;
        }

        return total > MaxTextFieldBytes ? null : System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }
}
