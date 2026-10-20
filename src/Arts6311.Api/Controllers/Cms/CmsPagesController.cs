using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Pages.Facade;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Cms;

/// <summary>
/// Standalone pages (content.md): list, read, create, edit, and the publish / unpublish / archive commands. Bodies are
/// HTML, sanitized before they are stored (ADR 0034); every response carries the body as stored.
/// </summary>
/// <remarks>
/// Every write after the create carries the <c>ETag</c> it read as <c>If-Match</c> — the status commands too, so
/// nobody publishes a version they have not seen. Archive is Admin-only (auth.md "Sensitive operations") and audited.
/// </remarks>
[ApiController]
[Route("api/v1/cms/pages")]
[Authorize(Policy = ApiPolicies.CmsEditor)]
public sealed class CmsPagesController(IPagesFacade facade) : ControllerBase
{
    /// <summary>Pages by title, cursor-paged, optionally by status or part of a title.</summary>
    [HttpGet]
    [ProducesResponseType<CursorPage<PageSummaryServiceModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "status")] PublishStatus? status,
        [FromQuery(Name = "search")] string? search,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.ListAsync(new PageListViewModel(cursor, limit, status, search), cancellationToken));

    /// <summary>One page. The <c>ETag</c> header is what a write sends back as <c>If-Match</c>.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PageServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        WithETag(await facade.GetAsync(id, cancellationToken));

    /// <summary>A new Draft. The slug is made from the title when none is sent; the body is sanitized.</summary>
    [HttpPost]
    [ProducesResponseType<PageServiceModel>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] PageCreateViewModel create, CancellationToken cancellationToken)
    {
        var result = await facade.CreateAsync(create, User.UserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return this.ToActionResult(result);
        }

        Response.Headers.ETag = result.Value!.ETag;
        return Created($"/api/v1/cms/pages/{result.Value.Id}", result.Value);
    }

    /// <summary>An edit. A field left out is unchanged; an empty body clears it. Status changes only through the commands below.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<PageServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] PageUpdateViewModel update,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken) =>
        WithETag(await facade.UpdateAsync(id, update, ifMatch, User.UserId(), cancellationToken));

    /// <summary>Draft → Published. Refused while the body is empty.</summary>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType<PageServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> PublishAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        WithETag(await facade.PublishAsync(id, ifMatch, User.UserId(), cancellationToken));

    /// <summary>Published → Draft. The slug stays as it is.</summary>
    [HttpPost("{id:guid}/unpublish")]
    [ProducesResponseType<PageServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UnpublishAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        WithETag(await facade.UnpublishAsync(id, ifMatch, User.UserId(), cancellationToken));

    /// <summary>Draft or Published → Archived. Admin only; audited.</summary>
    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = ApiPolicies.CmsAdmin)]
    [ProducesResponseType<PageServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ArchiveAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        WithETag(await facade.ArchiveAsync(id, ifMatch, User.UserId(), cancellationToken));


    /// <summary>
    /// Draft or Scheduled → Scheduled: the Worker publishes a page when the time comes (ADR 0036). Sending it
    /// again moves the time. Refused for a time already past, or for anything that would stop a publish now.
    /// </summary>
    [HttpPost("{id:guid}/schedule")]
    [ProducesResponseType<PageServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ScheduleAsync(
        Guid id,
        [FromBody] ContentScheduleViewModel schedule,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken) =>
        WithETag(await facade.ScheduleAsync(id, schedule, ifMatch, User.UserId(), cancellationToken));

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    [HttpPost("{id:guid}/unschedule")]
    [ProducesResponseType<PageServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UnscheduleAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        WithETag(await facade.UnscheduleAsync(id, ifMatch, User.UserId(), cancellationToken));

    private IActionResult WithETag(OperationResult<PageServiceModel> result)
    {
        if (result.Succeeded)
        {
            Response.Headers.ETag = result.Value!.ETag;
        }

        return this.ToActionResult(result);
    }
}
