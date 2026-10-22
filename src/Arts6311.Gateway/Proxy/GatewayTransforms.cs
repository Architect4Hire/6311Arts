using System.Diagnostics;
using Arts6311.Gateway.Authentication;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Arts6311.Gateway.Proxy;

internal static class GatewayTransforms
{
    /// <summary>
    /// The only routes that may carry an internal token.
    /// </summary>
    /// <remarks>
    /// Named routes, not the <c>api</c> cluster: <c>/webhooks/square</c> also targets that cluster, and a webhook
    /// is Square calling us with a signature, never a signed-in person. If a browser ever posted there carrying a
    /// session, the gateway would otherwise sign a user token for it.
    /// <para>
    /// A route that requires a session must be in this list, or its requests would arrive at the API with no
    /// credential and be refused there for a reason that points nowhere near the cause.
    /// <c>GatewayRouteTableTests</c> asserts exactly that, so adding an authorized route cannot miss this list.
    /// </para>
    /// </remarks>
    internal static readonly string[] TokenBearingRoutes = ["cms", "cms-media-upload", "cms-shop-import", "api", "media"];

    /// <summary>Headers a client must never be able to set on a request the API will trust.</summary>
    /// <remarks>
    /// A deny-list rather than YARP's <c>RequestHeadersAllowed</c> allow-list. An allow-list is stronger, but it
    /// would mean enumerating every legitimate header — <c>Accept</c>, <c>Content-Type</c>, <c>If-Match</c>,
    /// <c>Idempotency-Key</c>, and each one a later prompt adds — and a forgotten entry breaks a route silently.
    /// ADR 0022 records the allow-list as later hardening, once the full header surface is known.
    /// </remarks>
    private static readonly string[] StrippedHeaders =
    [
        // The gateway is the only thing that may present a credential to the API. A client-supplied Authorization
        // header would otherwise arrive alongside, or instead of, the one we sign.
        "Authorization",

        // Read by the gateway for its session (1.10) and never forwarded: the API takes no cookies at all.
        "Cookie",

        // Internal headers. A client setting these could claim to be the gateway acting on someone's behalf.
        "X-Internal-Token",
        "X-Gateway-User",
        "X-Gateway-Session",

        // Platform identity headers. Azure Container Apps' built-in auth uses these, so a client that could set
        // them might be taken for an authenticated principal by something downstream.
        "X-MS-CLIENT-PRINCIPAL",
        "X-MS-CLIENT-PRINCIPAL-ID",
        "X-MS-CLIENT-PRINCIPAL-IDP",
        "X-MS-CLIENT-PRINCIPAL-NAME",

        // Trace context a client supplied. AttachTrace replaces traceparent and tracestate from the current
        // activity, but nothing would otherwise stop arbitrary baggage reaching the API as though we set it.
        "baggage",
        "Request-Id",

        // URL-rewriting headers some stacks honour, which can move a request to a different path than the one the
        // edge authorized.
        "X-Original-URL",
        "X-Rewrite-URL",
    ];

    // Client-supplied X-Forwarded-* and Forwarded are removed inbound by TrustedHeadersMiddleware, not here: YARP's
    // own X-Forwarded transform owns those names on the proxy request and overrides a remove transform for the same
    // name.

    /// <summary>
    /// The transforms every proxied request goes through (<c>gateway.md</c> "Headers").
    /// </summary>
    /// <remarks>
    /// Applied to all routes rather than per route. A header that must not reach one destination must not reach
    /// another either, and an opt-in list is the kind of thing a new route forgets.
    /// </remarks>
    public static IReverseProxyBuilder AddGatewayTransforms(this IReverseProxyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddTransforms(context =>
        {
            foreach (var header in StrippedHeaders)
            {
                context.AddRequestHeaderRemove(header);
            }

            // Writes the true values. Whatever the client claimed is already gone (TrustedHeadersMiddleware), so
            // this is the only source of the X-Forwarded-* the destination sees.
            context.AddXForwarded(ForwardedTransformActions.Set);

            // The trace goes everywhere: one trace spanning the gateway and whatever served the request.
            context.AddRequestTransform(transform =>
            {
                AttachTrace(transform);
                return ValueTask.CompletedTask;
            });

            // The token goes only to the routes that serve a signed-in person. Decided here, at build time, from the
            // route id rather than per request: the site and admin apps have no use for an internal token, and a
            // credential handed to a process that does not need it is one more place it can be logged, stored or
            // replayed — a 2-minute token with the user's roles would be enough to call the API as them.
            if (TokenBearingRoutes.Contains(context.Route.RouteId, StringComparer.Ordinal))
            {
                context.AddRequestTransform(transform =>
                {
                    AttachInternalToken(transform);
                    return ValueTask.CompletedTask;
                });
            }
        });
    }

    /// <summary>
    /// Carries this request's trace to the destination.
    /// </summary>
    /// <remarks>
    /// YARP forwards through a bare <c>SocketsHttpHandler</c>, with no <c>DiagnosticsHandler</c>, so it does not
    /// inject <c>traceparent</c> the way <c>HttpClient</c> would. Without this the gateway's span and the API's are
    /// separate traces, and CLAUDE.md's "one trace runs from gateway → API → SQL" would not hold. Any inbound
    /// value is overwritten: ASP.NET Core has already parsed it into this activity, so the activity is the
    /// authority.
    /// </remarks>
    private static void AttachTrace(RequestTransformContext transform)
    {
        if (Activity.Current is not { } activity)
        {
            return;
        }

        DistributedContextPropagator.Current.Inject(activity, transform.ProxyRequest, static (carrier, key, value) =>
        {
            if (carrier is HttpRequestMessage request)
            {
                request.Headers.Remove(key);
                request.Headers.TryAddWithoutValidation(key, value);
            }
        });
    }

    /// <summary>
    /// Attaches the gateway-signed internal token when, and only when, there is a signed-in person behind the
    /// request (ADR 0002).
    /// </summary>
    /// <remarks>
    /// An anonymous request gets no token at all, rather than a token saying "anonymous": the API is secure by
    /// default and its <c>[AllowAnonymous]</c> routes decide what a caller with no credential may read (ADR 0021).
    /// <para>
    /// Before 1.10 there is no authentication scheme in the gateway, so nothing is ever authenticated and this
    /// never signs. The branch exists now so 1.10 adds the session without touching the proxy, and
    /// <c>InternalTokenContractTests</c> proves the signer's output is accepted by the API meanwhile.
    /// </para>
    /// </remarks>
    private static void AttachInternalToken(RequestTransformContext transform)
    {
        var httpContext = transform.HttpContext;
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var signer = httpContext.RequestServices.GetRequiredService<IInternalTokenSigner>();
        if (signer.SignForUser(httpContext.User) is { Length: > 0 } token)
        {
            transform.ProxyRequest.Headers.Remove("Authorization");
            transform.ProxyRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }
    }
}
