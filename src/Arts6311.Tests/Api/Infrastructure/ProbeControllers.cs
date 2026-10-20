using System.Reflection;
using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace Arts6311.Tests.Api.Infrastructure;

/// <summary>
/// A role-gated product route that exists only in the test host, so the edge tests (internal-token authentication,
/// <c>ProblemDetails</c>, token logging) have one to aim at without depending on whichever CMS route ships first.
/// It took over from the 1.5 ping route when that was deleted in 1.12.
/// </summary>
/// <remarks>
/// Test-only: it lives in the test assembly and reaches a host only through <see cref="ProbeControllersPart"/>, which
/// <c>ApiFactoryBase</c> adds; the API project has no reference to it. Hidden from the OpenAPI document so the contract snapshot describes the real API only. It goes through the
/// shared result mapper, like a real controller, so a facade failure maps exactly as it would in production.
/// </remarks>
[ApiController]
[Route(Route)]
[Authorize(Policy = ApiPolicies.CmsAdmin)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class CmsAdminProbeController(IProbeFacade facade) : ControllerBase
{
    public const string Route = "api/v1/cms/test-probe";

    [HttpPost]
    public IActionResult Post() => this.ToAcceptedResult(facade.Respond());
}

/// <summary>The same probe behind <see cref="DevelopmentOnlyAttribute"/>, for the gate's own tests.</summary>
[ApiController]
[Route(Route)]
[Authorize(Policy = ApiPolicies.CmsAdmin)]
[DevelopmentOnly]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class DevelopmentOnlyProbeController(IProbeFacade facade) : ControllerBase
{
    public const string Route = "api/v1/cms/test-probe/development-only";

    [HttpPost]
    public IActionResult Post() => this.ToAcceptedResult(facade.Respond());
}

/// <summary>
/// The probes' facade. Controllers inject facades through interfaces only (backend.md), and the test doubles here
/// keep to that rule so a probe reads exactly like the controllers it stands in for.
/// </summary>
public interface IProbeFacade
{
    OperationResult<ProbeServiceModel> Respond();
}

/// <summary>What the probes call. One per factory, so a test can set a failure and count calls.</summary>
public sealed class ProbeFacade : IProbeFacade
{
    private int calls;

    public int Calls => Volatile.Read(ref calls);

    /// <summary>Set to make the probe fail with this error instead of answering 202.</summary>
    public OperationError? Error { get; set; }

    public OperationResult<ProbeServiceModel> Respond()
    {
        Interlocked.Increment(ref calls);

        return Error is { } error
            ? OperationResult.Failure<ProbeServiceModel>(error)
            : OperationResult.Success(new ProbeServiceModel("ok"));
    }
}

public sealed record ProbeServiceModel(string Status);

/// <summary>
/// Adds exactly the probe controllers to the API's controller set. Adding the whole test assembly as a part would
/// pull in anything else in it that happens to look like a controller.
/// </summary>
internal sealed class ProbeControllersPart : ApplicationPart, IApplicationPartTypeProvider
{
    public override string Name => "Arts6311.Tests.Probes";

    public IEnumerable<TypeInfo> Types { get; } =
    [
        typeof(CmsAdminProbeController).GetTypeInfo(),
        typeof(DevelopmentOnlyProbeController).GetTypeInfo(),
    ];
}
