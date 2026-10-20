using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Cms;

/// <summary>
/// Classes and workshops (content.md): list, read, create, edit, and the publish / unpublish / schedule / archive
/// commands.
/// </summary>
/// <remarks>
/// Every write after the create carries the <c>ETag</c> it read as <c>If-Match</c> — the status commands too, so
/// nobody publishes a version they have not seen. Archive is Admin-only (auth.md "Sensitive operations") and audited.
/// <para>
/// A session's SKU and capacity are edited here because the CMS owns them; its price, seats remaining and sold-out
/// are Square's record and are read-only in every response (square.md, ADR 0032). The starting price is a CMS field
/// only until Square reports a price, which <c>startingPriceEditable</c> says.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/cms/classes")]
[Authorize(Policy = ApiPolicies.CmsEditor)]
public sealed class CmsClassesController(IClassesFacade facade) : ControllerBase
{
    /// <summary>Classes by title, cursor-paged, optionally by status or part of a title.</summary>
    [HttpGet]
    [ProducesResponseType<CursorPage<ClassSummaryServiceModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "status")] PublishStatus? status,
        [FromQuery(Name = "search")] string? search,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.ListAsync(new ClassListViewModel(cursor, limit, status, search), cancellationToken));

    /// <summary>One class, with its sessions. The <c>ETag</c> header is what a write sends back as <c>If-Match</c>.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ClassServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        WithETag(await facade.GetAsync(id, cancellationToken));

    /// <summary>
    /// A new Draft. The slug is made from the title when none is sent, and each session gets a suggested SKU
    /// (<c>CLS-&lt;offering&gt;-&lt;n&gt;</c>) when none is sent. A SKU another record already uses is a 409
    /// (<c>cms.sku_taken</c>).
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ClassServiceModel>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] ClassCreateViewModel create, CancellationToken cancellationToken)
    {
        var result = await facade.CreateAsync(create, User.UserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return this.ToActionResult(result);
        }

        Response.Headers.ETag = result.Value!.ETag;
        return Created($"/api/v1/cms/classes/{result.Value.Id}", result.Value);
    }

    /// <summary>
    /// An edit. A field left out is unchanged; an empty string clears a text field; <c>sessions</c> replaces the
    /// sittings. Status changes only through the commands below.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<ClassServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] ClassUpdateViewModel update,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken) =>
        WithETag(await facade.UpdateAsync(id, update, ifMatch, User.UserId(), cancellationToken));

    /// <summary>
    /// Draft → Published. Refused without a summary, a starting price or a session, or while the hero has no alt text.
    /// </summary>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType<ClassServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> PublishAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        WithETag(await facade.PublishAsync(id, ifMatch, User.UserId(), cancellationToken));

    /// <summary>Published → Draft. The slug stays as it is.</summary>
    [HttpPost("{id:guid}/unpublish")]
    [ProducesResponseType<ClassServiceModel>(StatusCodes.Status200OK)]
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
    [ProducesResponseType<ClassServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ArchiveAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        WithETag(await facade.ArchiveAsync(id, ifMatch, User.UserId(), cancellationToken));

    /// <summary>
    /// Draft or Scheduled → Scheduled: the Worker publishes a class when the time comes (ADR 0036). Sending it again
    /// moves the time. Refused for a time already past, or for anything that would stop a publish now.
    /// </summary>
    [HttpPost("{id:guid}/schedule")]
    [ProducesResponseType<ClassServiceModel>(StatusCodes.Status200OK)]
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
    [ProducesResponseType<ClassServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UnscheduleAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        WithETag(await facade.UnscheduleAsync(id, ifMatch, User.UserId(), cancellationToken));

    private IActionResult WithETag(OperationResult<ClassServiceModel> result)
    {
        if (result.Succeeded)
        {
            Response.Headers.ETag = result.Value!.ETag;
        }

        return this.ToActionResult(result);
    }
}
