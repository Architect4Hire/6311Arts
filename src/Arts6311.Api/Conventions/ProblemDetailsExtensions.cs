using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Arts6311.Api.Conventions;

internal static class ProblemDetailsExtensions
{
    /// <summary>
    /// Makes every failure the API returns look the same: <c>ProblemDetails</c> with a stable <c>code</c> and the
    /// request's <c>correlationId</c> (<c>api-contract.md</c> "Errors").
    /// </summary>
    /// <remarks>
    /// Two paths produce a problem, and both are covered here:
    /// <list type="bullet">
    /// <item>a controller mapping an <see cref="OperationError"/> (see <see cref="ControllerResultExtensions"/>);</item>
    /// <item>the framework writing one for an empty 401 or 403, a missing route or an unhandled exception, through
    /// <c>UseStatusCodePages</c> and <c>UseExceptionHandler</c>. Those never see an <see cref="OperationError"/>, so
    /// the customiser below gives them a code too — a bare status with no body would otherwise be the one failure a
    /// client could not identify or trace.</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddArtsProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;

            problem.Status ??= status;
            problem.Title ??= ApiProblemDetails.TitleFor(status);

            if (!problem.Extensions.ContainsKey(ApiProblemDetails.CodeExtension))
            {
                problem.Extensions[ApiProblemDetails.CodeExtension] = ApiProblemDetails.CodeForStatus(status);
            }

            ApiProblemDetails.Stamp(problem, context.HttpContext.CorrelationId());
        });

        return services;
    }

    /// <summary>
    /// Points MVC's model-state response at the same factory, so a binding failure (a malformed route id, an
    /// unparsable body) is byte-identical to a FluentValidation failure raised in a facade. A client has one
    /// validation shape to handle, not two.
    /// </summary>
    /// <summary>
    /// JSON only. MVC's defaults also offer <c>text/plain</c> (for a bare string) and <c>text/json</c>, which land
    /// in the OpenAPI document for every response and let a generated client type a body as <c>string</c>. The API
    /// speaks <c>application/json</c>, and errors <c>application/problem+json</c>; removing a media type once a
    /// client exists is a narrowing, so it happens now, while there is no client.
    /// </summary>
    public static IServiceCollection AddArtsFormatters(this IServiceCollection services)
    {
        services.Configure<MvcOptions>(options =>
        {
            options.OutputFormatters.RemoveType<StringOutputFormatter>();
            options.OutputFormatters.RemoveType<StreamOutputFormatter>();

            foreach (var formatter in options.OutputFormatters.OfType<SystemTextJsonOutputFormatter>())
            {
                formatter.SupportedMediaTypes.Remove("text/json");
            }

            foreach (var formatter in options.InputFormatters.OfType<SystemTextJsonInputFormatter>())
            {
                formatter.SupportedMediaTypes.Remove("text/json");
            }
        });

        return services;
    }

    public static IServiceCollection AddArtsApiBehavior(this IServiceCollection services)
    {
        services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = context =>
            {
                var error = OperationError.Validation(
                    "One or more fields are not valid.",
                    context.ModelState
                        .SelectMany(entry => entry.Value!.Errors.Select(modelError => (
                            Field: entry.Key,
                            Error: ValidationMessage(modelError))))
                        .ToList());

                var problem = ApiProblemDetails.From(error, context.HttpContext.CorrelationId());
                return new ObjectResult(problem) { StatusCode = problem.Status };
            });

        return services;
    }

    /// <summary>
    /// Model binding puts the raw exception on the entry when it cannot convert a value. Its message can name
    /// types and internals, so an empty message becomes a fixed phrase rather than the exception's text.
    /// </summary>
    private static string ValidationMessage(ModelError modelError) =>
        string.IsNullOrWhiteSpace(modelError.ErrorMessage) ? "The value is not valid." : modelError.ErrorMessage;

    /// <summary>
    /// The request's correlation id: the W3C trace id (ADR 0019), so a problem body and the dashboard's trace
    /// search use the same value.
    /// </summary>
    public static Guid CorrelationId(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.RequestServices.GetRequiredService<ICorrelationContext>().Current;
    }

    /// <summary>Resolves the ambient correlation id inside a controller.</summary>
    public static Guid CorrelationId(this ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return controller.HttpContext.CorrelationId();
    }
}
