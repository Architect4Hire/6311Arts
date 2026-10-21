using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Forms.Facade;
using Arts6311.Domain.Modules.Forms.Managers;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Arts6311.Api.Controllers.Forms;

/// <summary>
/// The public forms (ADR 0047): an artist's submission, a contact message and a newsletter sign-up. Anonymous by
/// design, so each opts out of authentication explicitly; the gateway rate-limits them (<c>forms</c>) and requires
/// antiforgery, as for every unsafe request (auth.md).
/// </summary>
/// <remarks>
/// Every success is <c>202</c> with no body, whatever happened behind it — a bot's honeypot hit and an address already
/// on the newsletter list answer exactly as a new one does, so the answer tells a caller nothing.
/// </remarks>
[ApiController]
[Route("api/v1/forms")]
[AllowAnonymous]
public sealed class FormsController(IFormsFacade facade) : ControllerBase
{
    private const string SubmissionField = "submission";
    private const string ImagesField = "images";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// An artist's submission: a <c>submission</c> part holding the answers as JSON, then up to six <c>images</c>
    /// parts (JPEG, PNG or WebP, 10 MiB each). The pictures stream straight into Media as hidden drafts.
    /// </summary>
    [HttpPost("submissions")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(FormsPolicy.MaxSubmissionRequestBytes)]
    [StreamedMultipartUpload(ImagesField, SubmissionField)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> SubmitAsync(CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType)
            || HeaderUtilities.RemoveQuotes(contentType.Boundary).Value is not { Length: > 0 and <= 200 } boundary)
        {
            return Invalid(SubmissionField, "The body must be multipart/form-data with a boundary.");
        }

        var reader = new MultipartReader(boundary, Request.Body);

        // The answers come first, so they are validated before a single picture is read. Known by name only: a
        // browser that sends them as a JSON Blob (to give the part its type) also gives it a file name, "blob".
        var first = await reader.ReadNextSectionAsync(cancellationToken);
        if (first is null
            || !ContentDispositionHeaderValue.TryParse(first.ContentDisposition, out var disposition)
            || HeaderUtilities.RemoveQuotes(disposition.Name).Value != SubmissionField)
        {
            return Invalid(SubmissionField, "The first part must be the submission's answers, named 'submission'.");
        }

        var json = await ReadTextAsync(first.Body, FormsPolicy.MaxSubmissionJsonBytes, cancellationToken);
        if (json is null)
        {
            return Invalid(SubmissionField, "The submission's answers are too long.");
        }

        ArtistSubmissionViewModel? submission;
        try
        {
            submission = JsonSerializer.Deserialize<ArtistSubmissionViewModel>(json, Web);
        }
        catch (JsonException)
        {
            submission = null;
        }

        if (submission is null)
        {
            return Invalid(SubmissionField, "The submission's answers are not valid JSON.");
        }

        return this.ToAcceptedResult(await facade.SubmitAsync(submission, ImagesAsync(reader, cancellationToken), cancellationToken));
    }

    /// <summary>A message from the contact form. The sender and staff each get an email.</summary>
    [HttpPost("contact")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ContactAsync([FromBody] ContactMessageViewModel message, CancellationToken cancellationToken) =>
        this.ToAcceptedResult(await facade.ContactAsync(message, cancellationToken));

    /// <summary>A newsletter sign-up. Signing up again refreshes the consent; the answer is the same either way.</summary>
    [HttpPost("newsletter")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubscribeAsync([FromBody] NewsletterSignupViewModel signup, CancellationToken cancellationToken) =>
        this.ToAcceptedResult(await facade.SubscribeAsync(signup, cancellationToken));

    /// <summary>
    /// The picture parts, as the facade asks for them: each one's body is the request stream itself, so nothing holds
    /// a whole file. Parts not named <c>images</c>, and text parts, are skipped.
    /// </summary>
    private static async IAsyncEnumerable<SubmissionImageViewModel> ImagesAsync(
        MultipartReader reader, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                || !disposition.IsFileDisposition()
                || HeaderUtilities.RemoveQuotes(disposition.Name).Value != ImagesField)
            {
                continue;
            }

            var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue
                ? disposition.FileNameStar
                : disposition.FileName).Value ?? string.Empty;
            yield return new SubmissionImageViewModel(fileName, section.Body);
        }
    }

    /// <summary>A text part, or <c>null</c> when it runs past <paramref name="maxBytes"/>.</summary>
    private static async Task<string?> ReadTextAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[maxBytes + 1];
        var total = 0;
        int read;
        while ((read = await body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                return null;
            }
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    private IActionResult Invalid(string field, string message) =>
        this.ToAcceptedResult(OperationResult.Failure(
            OperationError.Validation("One or more fields are not valid.", [(field, message)])));
}
