using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Preview.Facade;
using Arts6311.Domain.Modules.Preview.Managers.ServiceModels;
using Arts6311.Domain.Modules.Preview.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Cms;

/// <summary>
/// <c>POST /api/v1/cms/preview</c>: a signed, expiring link to one unpublished record, for staff to open or send
/// to a colleague (ADR 0036).
/// </summary>
/// <remarks>
/// One route for every content type, because one token resolves to any of them. It makes nothing and changes
/// nothing: the link grants a single read of a record the editor can already open, which is why it carries no
/// <c>If-Match</c> and writes no audit row.
/// </remarks>
[ApiController]
[Route("api/v1/cms/preview")]
[Authorize(Policy = ApiPolicies.CmsEditor)]
public sealed class CmsPreviewController(IPreviewFacade facade) : ControllerBase
{
    /// <summary>The token, where to open it, and when it expires.</summary>
    [HttpPost]
    [ProducesResponseType<PreviewLinkServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] PreviewLinkRequestViewModel request, CancellationToken cancellationToken) =>
        this.ToActionResult(await facade.CreateLinkAsync(request, cancellationToken));
}
