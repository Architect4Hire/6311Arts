using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Api;

/// <summary>
/// The HTTP half of idempotent commands, which ADR 0020 leaves to the API: reading the <c>Idempotency-Key</c>
/// header and marking a replayed response.
/// </summary>
/// <remarks>
/// No route is idempotent until checkout pay in 5.14, so this is tested at the controller seam rather than over
/// HTTP. The key's own rules live in <c>IdempotencyBusiness</c> and are tested with it; what is proven here is the
/// wiring and that <c>idempotency.key_reused</c> reaches a 422.
/// </remarks>
public sealed class ApiIdempotencyTests
{
    [Fact]
    public void TheKeyIsReadFromTheHeader()
    {
        var controller = Controller(("Idempotency-Key", "abc-123"));

        Assert.Equal("abc-123", controller.IdempotencyKey());
    }

    [Fact]
    public void NoHeaderMeansNoKey()
    {
        Assert.Null(Controller().IdempotencyKey());
    }

    [Fact]
    public void AnEmptyHeaderMeansNoKey()
    {
        // Passed on as absent rather than as "", so the command's KeyRequired decides, and the error says the key
        // is missing rather than malformed.
        Assert.Null(Controller(("Idempotency-Key", "")).IdempotencyKey());
    }

    [Fact]
    public void SeveralKeysMeanNoKey()
    {
        // Ambiguous. Picking one could replay the wrong caller's result, so neither is used.
        var controller = Controller();
        controller.Request.Headers["Idempotency-Key"] = new[] { "first", "second" };

        Assert.Null(controller.IdempotencyKey());
    }

    [Fact]
    public void AReplayIsMarkedOnTheResponse()
    {
        var controller = Controller();
        var outcome = new IdempotentOutcome<string>(OperationResult.Success("done"), Replayed: true);

        var result = controller.ToActionResult(outcome);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("true", controller.Response.Headers[IdempotencyPolicy.ReplayedHeader]);
    }

    [Fact]
    public void AFreshRunIsNotMarked()
    {
        var controller = Controller();
        var outcome = new IdempotentOutcome<string>(OperationResult.Success("done"), Replayed: false);

        controller.ToActionResult(outcome);

        Assert.False(controller.Response.Headers.ContainsKey(IdempotencyPolicy.ReplayedHeader));
    }

    [Fact]
    public void AReusedKeyIs422()
    {
        // api-contract.md: the same key with a different body is refused, not retried and not replayed.
        var controller = Controller();
        var outcome = new IdempotentOutcome<string>(
            OperationResult.Failure<string>(OperationError.Of(ErrorCodes.IdempotencyKeyReused, "Already used.")),
            Replayed: false);

        var result = Assert.IsType<ObjectResult>(controller.ToActionResult(outcome));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(ErrorCodes.IdempotencyKeyReused, problem.Extensions["code"]);
    }

    [Fact]
    public void AMissingKeyIs400()
    {
        var controller = Controller();
        var outcome = new IdempotentOutcome<string>(
            OperationResult.Failure<string>(
                OperationError.Of(ErrorCodes.IdempotencyKeyRequired, "This request requires a key.")),
            Replayed: false);

        var result = Assert.IsType<ObjectResult>(controller.ToActionResult(outcome));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public void TheHeaderNamesAreTheOnesTheKernelDeclares()
    {
        // One spelling, shared by the kernel and the edge: two constants would drift.
        Assert.Equal("Idempotency-Key", IdempotencyPolicy.KeyHeader);
        Assert.Equal("Idempotent-Replayed", IdempotencyPolicy.ReplayedHeader);
    }

    private static TestController Controller(params (string Name, string Value)[] headers)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICorrelationContext, StubCorrelationContext>();

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        foreach (var (name, value) in headers)
        {
            httpContext.Request.Headers[name] = value;
        }

        return new TestController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private sealed class TestController : ControllerBase;

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public Guid Current { get; } = Guid.NewGuid();
    }
}
