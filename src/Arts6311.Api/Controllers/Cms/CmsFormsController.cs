using System.Text;
using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Forms.Facade;
using Arts6311.Domain.Modules.Forms.Managers;
using Arts6311.Domain.Modules.Forms.Managers.ServiceModels;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Cms;

/// <summary>
/// What visitors sent through the public forms (ADR 0047): the artist submissions inbox, contact messages and the
/// newsletter list. Editors read and work the inbox; taking an address off the list and exporting it are Admin-only
/// and audited, because the list is personal data.
/// </summary>
[ApiController]
[Route("api/v1/cms/forms")]
[Authorize(Policy = ApiPolicies.CmsEditor)]
public sealed class CmsFormsController(IFormsFacade facade) : ControllerBase
{
    /// <summary>The submissions inbox, newest first, optionally one status.</summary>
    [HttpGet("submissions")]
    [ProducesResponseType<CursorPage<SubmissionSummaryServiceModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListSubmissionsAsync(
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "status")] SubmissionStatus? status,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.ListSubmissionsAsync(new SubmissionListViewModel(cursor, limit, status), cancellationToken));

    /// <summary>One submission with its pictures. The <c>ETag</c> header is what a change sends back as <c>If-Match</c>.</summary>
    [HttpGet("submissions/{id:guid}")]
    [ProducesResponseType<SubmissionServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubmissionAsync(Guid id, CancellationToken cancellationToken) =>
        WithETag(await facade.GetSubmissionAsync(id, cancellationToken));

    /// <summary>Moves a submission through the inbox, or edits staff's notes on it. A field left out is unchanged.</summary>
    [HttpPatch("submissions/{id:guid}")]
    [ProducesResponseType<SubmissionServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateSubmissionAsync(
        Guid id,
        [FromBody] SubmissionUpdateViewModel update,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken) =>
        WithETag(await facade.UpdateSubmissionAsync(id, update, ifMatch, User.UserId(), cancellationToken));

    /// <summary>Contact messages, newest first, optionally only handled or unhandled ones.</summary>
    [HttpGet("messages")]
    [ProducesResponseType<CursorPage<ContactMessageServiceModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListMessagesAsync(
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "handled")] bool? handled,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.ListContactsAsync(new ContactListViewModel(cursor, limit, handled), cancellationToken));

    [HttpGet("messages/{id:guid}")]
    [ProducesResponseType<ContactMessageServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessageAsync(Guid id, CancellationToken cancellationToken) =>
        WithETag(await facade.GetContactAsync(id, cancellationToken));

    /// <summary>Marks a message handled, or not.</summary>
    [HttpPatch("messages/{id:guid}")]
    [ProducesResponseType<ContactMessageServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateMessageAsync(
        Guid id,
        [FromBody] ContactUpdateViewModel update,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken) =>
        WithETag(await facade.UpdateContactAsync(id, update, ifMatch, User.UserId(), cancellationToken));

    /// <summary>The newsletter list, newest first, optionally part of an address.</summary>
    [HttpGet("newsletter")]
    [ProducesResponseType<CursorPage<NewsletterSubscriberServiceModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListSubscribersAsync(
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "search")] string? search,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.ListSubscribersAsync(new NewsletterListViewModel(cursor, limit, search), cancellationToken));

    /// <summary>Takes an address off the list, for someone who asked. Admin-only and audited.</summary>
    [HttpDelete("newsletter/{id:guid}")]
    [Authorize(Policy = ApiPolicies.CmsAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveSubscriberAsync(Guid id, CancellationToken cancellationToken) =>
        this.ToNoContentResult(await facade.RemoveSubscriberAsync(id, User.UserId(), cancellationToken));

    /// <summary>
    /// The whole list as a CSV file, safe to open in a spreadsheet. Admin-only and audited: personal data leaving the
    /// system. Never cached.
    /// </summary>
    [HttpGet("newsletter/export")]
    [Authorize(Policy = ApiPolicies.CmsAdmin)]
    [Produces("text/csv")]
    [ProducesResponseType<string>(StatusCodes.Status200OK, "text/csv")]
    public async Task<IActionResult> ExportSubscribersAsync(CancellationToken cancellationToken)
    {
        var export = await facade.ExportSubscribersAsync(User.UserId(), cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(export.Csv)).ToArray(), "text/csv; charset=utf-8", export.FileName);
    }

    private IActionResult WithETag<T>(OperationResult<T> result)
        where T : class
    {
        if (result.Succeeded)
        {
            Response.Headers.ETag = result.Value switch
            {
                SubmissionServiceModel submission => submission.ETag,
                ContactMessageServiceModel message => message.ETag,
                _ => Response.Headers.ETag,
            };
        }

        return this.ToActionResult(result);
    }
}
