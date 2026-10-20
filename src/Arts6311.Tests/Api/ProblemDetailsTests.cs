using System.Net;
using System.Text.Json;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Api;

/// <summary>
/// Every failure the API returns is <c>ProblemDetails</c> with a stable <c>code</c> and the <c>correlationId</c>
/// (api-contract.md "Errors"), whether a module raised it or the framework produced it.
/// </summary>
public sealed class ProblemDetailsTests
{
    private static readonly Guid KnownCorrelationId = Guid.Parse("0123456789abcdef0123456789abcdef");

    /// <summary>A role-gated product route that exists in the test host only.</summary>
    private const string ProbeRoute = "/" + CmsAdminProbeController.Route;

    [Theory]
    [InlineData(ErrorCodes.ValidationFailed, 400)]
    [InlineData(ErrorCodes.CursorInvalid, 400)]
    [InlineData(ErrorCodes.IdempotencyKeyRequired, 400)]
    [InlineData(ErrorCodes.IdempotencyKeyInvalid, 400)]
    [InlineData(ErrorCodes.Forbidden, 403)]
    [InlineData(ErrorCodes.NotFound, 404)]
    [InlineData(ErrorCodes.Conflict, 409)]
    [InlineData(ErrorCodes.ConcurrencyConflict, 409)]

    // ADR 0020: the same key with a different body is refused outright, not retried.
    [InlineData(ErrorCodes.IdempotencyKeyReused, 422)]
    public void EachCodeHasOneStatus(string code, int status)
    {
        Assert.Equal(status, ApiProblemDetails.StatusFor(code));
    }

    [Fact]
    public void AnUnmappedCodeIsABadRequest()
    {
        // An OperationError is an expected failure, so the caller is the one who can act on it. A server fault
        // throws instead and becomes a 500.
        Assert.Equal(
            ApiProblemDetails.UnmappedCodeStatus, ApiProblemDetails.StatusFor("some.module.specific_code"));
    }

    [Fact]
    public void AProblemCarriesTheCodeAndCorrelationId()
    {
        var problem = ApiProblemDetails.From(OperationError.Of(ContentErrorCodes.SlugTaken, "That slug is in use."), KnownCorrelationId);

        // Declared 409 since Artists (2.4); the status follows the code.
        Assert.Equal(409, problem.Status);
        Assert.Equal("That slug is in use.", problem.Detail);
        Assert.Equal(ContentErrorCodes.SlugTaken, problem.Extensions["code"]);
        Assert.Equal(KnownCorrelationId.ToString("N"), problem.Extensions["correlationId"]);
    }

    [Fact]
    public void FieldErrorsBecomeAValidationProblemKeyedByCamelCaseField()
    {
        var error = OperationError.Validation("Not valid.", [("Title", "Title is required."), ("StartsAt", "Too early.")]);

        var problem = Assert.IsType<ValidationProblemDetails>(ApiProblemDetails.From(error, KnownCorrelationId));

        Assert.Equal(["Title is required."], problem.Errors["title"]);
        Assert.Equal(["Too early."], problem.Errors["startsAt"]);
        Assert.Equal(ErrorCodes.ValidationFailed, problem.Extensions["code"]);
    }

    [Fact]
    public void AnErrorExtensionCannotOverwriteTheCodeOrCorrelationId()
    {
        // Both are the contract's own fields. A module adding an extension by either name would otherwise be able
        // to misreport which error happened, or which request it happened in.
        var error = new OperationError(
            "shop.item_unavailable",
            "Sold out.",
            new Dictionary<string, string[]>(),
            new Dictionary<string, object?> { ["code"] = "something.else", ["correlationId"] = "forged", ["itemId"] = "abc" });

        var problem = ApiProblemDetails.From(error, KnownCorrelationId);

        Assert.Equal("shop.item_unavailable", problem.Extensions["code"]);
        Assert.Equal(KnownCorrelationId.ToString("N"), problem.Extensions["correlationId"]);
        Assert.Equal("abc", problem.Extensions["itemId"]);
    }

    [Fact]
    public async Task A401CarriesACodeAndTheCorrelationId()
    {
        using var api = Factory();
        using var client = api.CreateJsonClient();

        var response = await client.PostAsync(
            ProbeRoute, content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await Problem(response);
        Assert.Equal(ErrorCodes.Unauthenticated, problem.GetProperty("code").GetString());
        Assert.Equal(KnownCorrelationId.ToString("N"), problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task A403CarriesACodeAndTheCorrelationId()
    {
        using var api = Factory();
        using var client = api.CreateJsonClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, ProbeRoute);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {api.Tokens.UserToken("Member")}");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await Problem(response);
        Assert.Equal(ErrorCodes.Forbidden, problem.GetProperty("code").GetString());
        Assert.Equal(KnownCorrelationId.ToString("N"), problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task AnUnknownRouteIsAProblemToo()
    {
        using var api = Factory();
        using var client = api.CreateJsonClient();

        var response = await client.GetAsync("/api/v1/site/no-such-thing", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.RouteNotFound, (await Problem(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AFacadeFailureIsMappedByItsCode()
    {
        using var api = Factory();
        api.Probe.Error = OperationError.Of(ErrorCodes.Conflict, "Already probed.");
        using var client = api.CreateJsonClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, ProbeRoute);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {api.Tokens.UserToken("Admin")}");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await Problem(response);
        Assert.Equal(ErrorCodes.Conflict, problem.GetProperty("code").GetString());
        Assert.Equal("Already probed.", problem.GetProperty("detail").GetString());
        Assert.Equal(KnownCorrelationId.ToString("N"), problem.GetProperty("correlationId").GetString());
    }

    /// <summary>Pins the correlation id so the body's value can be asserted rather than merely shown to exist.</summary>
    private static ApiFactory Factory(Action<IServiceCollection>? configure = null) => new(
        configureServices: services =>
        {
            services.AddScoped<ICorrelationContext>(_ => new FixedCorrelationContext());
            configure?.Invoke(services);
        });

    private static async Task<JsonElement> Problem(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body).RootElement;
    }

    private sealed class FixedCorrelationContext : ICorrelationContext
    {
        public Guid Current => KnownCorrelationId;
    }
}
