using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /api/v1/site/classes</c> and <c>/{slug}</c>: published classes with their upcoming runs (ADR 0042, 0044). A
/// public read, so it opts out of authentication explicitly; only what is upcoming, so not paged. <c>?instructor=</c>
/// narrows the list to the runs one published instructor teaches, for their page.
/// </summary>
/// <remarks>
/// Cached in the API (HybridCache, cleared by <c>site.revalidate-requested</c>) and for a minute by anything in front
/// of it (<c>Cache-Control: public</c>). Prices and seats are Square's record, read-only here.
/// </remarks>
[ApiController]
[Route("api/v1/site/classes")]
[AllowAnonymous]
public sealed class SiteClassesController(IClassesFacade facade) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SiteClassSummaryServiceModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery(Name = "instructor")] string? instructor, CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.ListForSiteAsync(new SiteClassListViewModel(instructor), cancellationToken));

    [HttpGet("{slug}")]
    [ProducesResponseType<SiteClassServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync([FromRoute(Name = "slug")] string slug, CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.GetForSiteAsync(slug, cancellationToken));
}
