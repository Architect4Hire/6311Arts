using Arts6311.Api.Conventions;
using Arts6311.Domain.Modules.Instructors.Facade;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Controllers.Site;

/// <summary>
/// <c>GET /api/v1/site/instructors</c> and <c>/{slug}</c>: the published instructors, and one of them (ADR 0042,
/// 0043). A public read, so it opts out of authentication explicitly; a short list, so not paged. What an instructor
/// teaches is at <c>/site/classes?instructor={slug}</c>.
/// </summary>
/// <remarks>
/// Cached in the API (HybridCache, cleared by <c>site.revalidate-requested</c> for an instructor or an artist) and for
/// a minute by anything in front of it (<c>Cache-Control: public</c>).
/// </remarks>
[ApiController]
[Route("api/v1/site/instructors")]
[AllowAnonymous]
public sealed class SiteInstructorsController(IInstructorsFacade facade) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SiteInstructorServiceModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.ListForSiteAsync(cancellationToken));

    [HttpGet("{slug}")]
    [ProducesResponseType<SiteInstructorServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync([FromRoute(Name = "slug")] string slug, CancellationToken cancellationToken) =>
        this.ToSiteReadResult(await facade.GetForSiteAsync(slug, cancellationToken));
}
