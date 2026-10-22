using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Exhibitions.Facade;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Cms;

/// <summary>
/// Exhibitions (content.md): list, read, create, edit, and the publish / unpublish / archive commands.
/// </summary>
/// <remarks>
/// Every write after the create carries the <c>ETag</c> it read as <c>If-Match</c> — the status commands too, so
/// nobody publishes a version they have not seen. Archive is Admin-only (auth.md "Sensitive operations") and audited.
/// </remarks>
[ApiController]
[Route("api/v1/cms/exhibitions")]
[Authorize(Policy = ApiPolicies.CmsEditor)]
public sealed class CmsExhibitionsController(IExhibitionsFacade facade) : ControllerBase
{
    /// <summary>Exhibitions, newest show first, cursor-paged, optionally by status or part of a title.</summary>
    [HttpGet]
    [ProducesResponseType<CursorPage<ExhibitionSummaryServiceModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "status")] PublishStatus? status,
        [FromQuery(Name = "search")] string? search,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.ListAsync(new ExhibitionListViewModel(cursor, limit, status, search), cancellationToken));


    /// <summary>
    /// The exhibitions staff deleted (ADR 0052), cursor-paged. Admin only, like the delete itself. Each row carries
    /// the <c>etag</c> that restoring it sends back as <c>If-Match</c>.
    /// </summary>
    [HttpGet("deleted")]
    [Authorize(Policy = ApiPolicies.CmsAdmin)]
    [ProducesResponseType<CursorPage<ExhibitionDeletedServiceModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListDeletedAsync(
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "search")] string? search,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.ListDeletedAsync(
            new ExhibitionListViewModel(cursor, limit, Status: null, search), cancellationToken));

    /// <summary>One exhibition, with its credited artists. The <c>ETag</c> header is what a write sends back as <c>If-Match</c>.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ExhibitionServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        WithETag(await facade.GetAsync(id, cancellationToken));

    /// <summary>A new Draft. The slug is made from the title when none is sent.</summary>
    [HttpPost]
    [ProducesResponseType<ExhibitionServiceModel>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] ExhibitionCreateViewModel create, CancellationToken cancellationToken)
    {
        var result = await facade.CreateAsync(create, User.UserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return this.ToActionResult(result);
        }

        Response.Headers.ETag = result.Value!.ETag;
        return Created($"/api/v1/cms/exhibitions/{result.Value.Id}", result.Value);
    }

    /// <summary>
    /// An edit. A field left out is unchanged; an empty string clears <c>summary</c> or <c>body</c>; <c>artistIds</c>
    /// replaces the credits in order. Status changes only through the commands below.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<ExhibitionServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] ExhibitionUpdateViewModel update,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken) =>
        WithETag(await facade.UpdateAsync(id, update, ifMatch, User.UserId(), cancellationToken));

    /// <summary>Draft → Published. Refused without a summary, or while the hero has no alt text.</summary>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType<ExhibitionServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> PublishAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        WithETag(await facade.PublishAsync(id, ifMatch, User.UserId(), cancellationToken));

    /// <summary>Published → Draft. The slug stays as it is.</summary>
    [HttpPost("{id:guid}/unpublish")]
    [ProducesResponseType<ExhibitionServiceModel>(StatusCodes.Status200OK)]
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
    [ProducesResponseType<ExhibitionServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ArchiveAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        WithETag(await facade.ArchiveAsync(id, ifMatch, User.UserId(), cancellationToken));

    /// <summary>
    /// Deletes the exhibition, from any status (ADR 0052). It leaves the site, leaves the list, and releases its web
    /// address; an Admin can restore it from the Deleted view. Admin only; audited.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = ApiPolicies.CmsAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        this.ToNoContentResult(await facade.DeleteAsync(id, ifMatch, User.UserId(), cancellationToken));

    /// <summary>
    /// Brings a deleted exhibition back as a Draft, with any schedule cleared (ADR 0052). Admin only; audited.
    /// Refused with <c>cms.slug_taken</c> when another record took its web address while it was deleted.
    /// </summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = ApiPolicies.CmsAdmin)]
    [ProducesResponseType<ExhibitionServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RestoreAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        WithETag(await facade.RestoreAsync(id, ifMatch, User.UserId(), cancellationToken));


    /// <summary>
    /// Draft or Scheduled → Scheduled: the Worker publishes an exhibition when the time comes (ADR 0036). Sending it
    /// again moves the time. Refused for a time already past, or for anything that would stop a publish now.
    /// </summary>
    [HttpPost("{id:guid}/schedule")]
    [ProducesResponseType<ExhibitionServiceModel>(StatusCodes.Status200OK)]
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
    [ProducesResponseType<ExhibitionServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UnscheduleAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        WithETag(await facade.UnscheduleAsync(id, ifMatch, User.UserId(), cancellationToken));

    private IActionResult WithETag(OperationResult<ExhibitionServiceModel> result)
    {
        if (result.Succeeded)
        {
            Response.Headers.ETag = result.Value!.ETag;
        }

        return this.ToActionResult(result);
    }
}
