using Arts6311.Api.Controllers.Operations;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Operations.Facade;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Arts6311.Tests.Api;

// The controller is [Obsolete] on purpose (1.5 scaffolding, deleted in 1.12); these tests go with it.
#pragma warning disable CS0618

/// <summary>
/// The controller seam only. Endpoint tests through <c>WebApplicationFactory</c> with the internal token arrive with
/// the API conventions in 1.7.
/// </summary>
public sealed class OpsPingControllerTests
{
    [Fact]
    public async Task InDevelopmentItStagesAPingAndReturns202WithItsIds()
    {
        var facade = new FakeFacade();
        var controller = new OpsPingController(facade, Environment(Environments.Development));

        var result = await controller.RequestPingAsync(TestContext.Current.CancellationToken);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(facade.Returned, accepted.Value);
        Assert.Equal(1, facade.Calls);
    }

    [Fact]
    public async Task AFailedResultIsAProblemNotAnEmpty202()
    {
        var facade = new FakeFacade { Fails = true };
        var controller = new OpsPingController(facade, Environment(Environments.Development));

        var result = await controller.RequestPingAsync(TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task OutsideDevelopmentTheRouteDoesNotExist(string environmentName)
    {
        var facade = new FakeFacade();
        var controller = new OpsPingController(facade, Environment(environmentName));

        var result = await controller.RequestPingAsync(TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, facade.Calls);
    }

    private static HostingEnvironment Environment(string name) => new() { EnvironmentName = name };

    private sealed class FakeFacade : IOperationsFacade
    {
        public PingRequestedServiceModel Returned { get; } = new(Guid.NewGuid(), Guid.NewGuid());

        public int Calls { get; private set; }

        public bool Fails { get; init; }

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
