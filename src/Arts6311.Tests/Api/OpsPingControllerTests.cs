using Arts6311.Api.Controllers.Operations;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Operations.Facade;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Api;

// The controller is [Obsolete] on purpose (1.5 scaffolding, deleted in 1.12); these tests go with it.
#pragma warning disable CS0618

/// <summary>
/// The controller seam only: the result mapping. The route's authentication, policy and Development-only
/// existence are covered through the real pipeline by <see cref="InternalTokenAuthenticationTests"/>,
/// <see cref="ApiRouteConventionTests"/> and <see cref="DevelopmentOnlyRouteTests"/>.
/// </summary>
public sealed class OpsPingControllerTests
{
    private static readonly Guid CorrelationId = Guid.Parse("fedcba9876543210fedcba9876543210");

    [Fact]
    public async Task ItStagesAPingAndReturns202WithItsIds()
    {
        var facade = new FakeFacade();
        var controller = Controller(facade);

        var result = await controller.RequestPingAsync(TestContext.Current.CancellationToken);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(facade.Returned, accepted.Value);
        Assert.Equal(1, facade.Calls);
    }

    [Fact]
    public async Task AFailedResultIsAProblemNotAnEmpty202()
    {
        // An unmapped code is a 400, not a 500: an OperationError is an expected failure the caller can act on
        // (ApiProblemDetails). The ping has no expected failure, so this only proves the mapping is the shared one.
        var facade = new FakeFacade { Fails = true };
        var controller = Controller(facade);

        var result = await controller.RequestPingAsync(TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        var details = Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Equal("test.failed", details.Extensions["code"]);
        Assert.Equal(CorrelationId.ToString("N"), details.Extensions["correlationId"]);
    }

    /// <summary>
    /// A controller with the ambient context it has in production: the shared result mapper reads the correlation
    /// id from request services, so a bare controller would have nowhere to read it from.
    /// </summary>
    private static OpsPingController Controller(IOperationsFacade facade)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICorrelationContext>(_ => new FixedCorrelationContext());

        return new OpsPingController(facade)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() },
            },
        };
    }

    private sealed class FixedCorrelationContext : ICorrelationContext
    {
        public Guid Current => CorrelationId;
    }

    private sealed class FakeFacade : IOperationsFacade
    {
        public PingRequestedServiceModel Returned { get; } = new(Guid.NewGuid(), Guid.NewGuid());

        public int Calls { get; private set; }

        public bool Fails { get; init; }

        public Task<OperationResult<HealthInfoServiceModel>> GetHealthInfoAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<PingRequestedServiceModel>> RequestPingAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Fails
                ? OperationResult.Failure<PingRequestedServiceModel>(OperationError.Of("test.failed", "Failed."))
                : OperationResult.Success(Returned));
        }

        public Task<OperationResult> RecordPingHandledAsync(
            Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
