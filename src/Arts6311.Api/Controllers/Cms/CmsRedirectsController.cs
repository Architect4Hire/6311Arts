using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Redirects.Facade;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Cms;

/// <summary>
/// Redirects (content.md): list, read, create, edit and delete. Every route is Admin-only and every change audited:
/// redirect changes are a sensitive operation (auth.md). Loops and chains longer than one hop are refused at write time.
/// </summary>
[ApiController]
[Route("api/v1/cms/redirects")]
[Authorize(Policy = ApiPolicies.CmsAdmin)]
public sealed class CmsRedirectsController(IRedirectsFacade facade) : ControllerBase
{
    /// <summary>Redirects by old address, cursor-paged, optionally by source or part of either address.</summary>
    [HttpGet]
    [ProducesResponseType<CursorPage<RedirectServiceModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "source")] RedirectSource? source,
        [FromQuery(Name = "search")] string? search,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.ListAsync(new RedirectListViewModel(cursor, limit, source, search), cancellationToken));

    /// <summary>One redirect. The <c>ETag</c> header is what a write sends back as <c>If-Match</c>.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<RedirectServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        WithETag(await facade.GetAsync(id, cancellationToken));

    /// <summary>A new redirect, recorded as Manual. Both addresses come back in their normal form.</summary>
    [HttpPost]
    [ProducesResponseType<RedirectServiceModel>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] RedirectCreateViewModel create, CancellationToken cancellationToken)
    {
        var result = await facade.CreateAsync(create, User.UserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return this.ToActionResult(result);
        }

        Response.Headers.ETag = result.Value!.ETag;
        return Created($"/api/v1/cms/redirects/{result.Value.Id}", result.Value);
    }

    /// <summary>An edit. A field left out is unchanged.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<RedirectServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] RedirectUpdateViewModel update,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken) =>
        WithETag(await facade.UpdateAsync(id, update, ifMatch, User.UserId(), cancellationToken));

    /// <summary>Deletes the redirect: its old address stops leading anywhere.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> DeleteAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        this.ToNoContentResult(await facade.DeleteAsync(id, ifMatch, User.UserId(), cancellationToken));

    private IActionResult WithETag(OperationResult<RedirectServiceModel> result)
    {
        if (result.Succeeded)
        {
            Response.Headers.ETag = result.Value!.ETag;
        }

        return this.ToActionResult(result);
    }
}
