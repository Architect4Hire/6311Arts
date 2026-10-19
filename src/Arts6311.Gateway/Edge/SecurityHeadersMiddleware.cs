using Microsoft.Extensions.Options;

namespace Arts6311.Gateway.Edge;

/// <summary>
/// The security headers every response leaves with (<c>gateway.md</c> "Security headers"). One origin means one
/// place to set them, which is the point of the single-origin rule (ADR 0001).
/// </summary>
/// <remarks>
/// <para>
/// Applied through <see cref="HttpResponse.OnStarting(Func{Task})"/>, not by setting the headers and calling
/// <c>next()</c>. Setting them up front is not enough: <c>ExceptionHandlerMiddleware</c> clears the response
/// headers before writing its own body, so on the 500 and 413 paths the headers vanish however early they were
/// written — moving this middleware earlier in the pipeline does not help, which was confirmed by watching a real
/// 413 come back bare. An <c>OnStarting</c> callback runs immediately before the headers are flushed, which is
/// after any such clearing and before the proxy has streamed anything.
/// </para>
/// <para>
/// The callback is registered on the way in so that it covers responses this request never returns from normally.
/// </para>
/// </remarks>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptions<GatewayOptions> options)
{
    private readonly CspOptions csp = options.Value.Csp;

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var policy = csp.For(context.Request.Path);

        context.Response.OnStarting(static state =>
        {
            var (response, referrer, contentSecurityPolicy) = ((HttpResponse, string, string))state;
            var headers = response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = referrer;

            // frame-ancestors 'none' in the CSP is the modern control; X-Frame-Options is kept for the browsers
            // that only understand that one.
            headers["X-Frame-Options"] = "DENY";

            if (contentSecurityPolicy.Length > 0)
            {
                headers["Content-Security-Policy"] = contentSecurityPolicy;
            }

            return Task.CompletedTask;
        },
        (context.Response, csp.Referrer, policy));

        return next(context);
    }
}

internal static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
