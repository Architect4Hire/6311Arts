using System.Net;
using Arts6311.Gateway.Authentication;
using Arts6311.Gateway.Edge;
using Arts6311.Gateway.Proxy;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Bound eagerly, so a missing rate-limit policy or signing key stops the host at startup rather than failing one
// route at request time (the pattern 1.7 set for InternalTokenOptions).
var gatewayOptions = new GatewayOptions();
builder.Configuration.GetSection(GatewayOptions.SectionName).Bind(gatewayOptions);
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));

var internalTokenOptions = new InternalTokenOptions();
builder.Configuration.GetSection(InternalTokenOptions.SectionName).Bind(internalTokenOptions);

// The clock. The Domain's AddArtsTime would normally register this, but the gateway holds no domain code and so
// does not reference it; DateTime.UtcNow is banned repo-wide (src/BannedSymbols.txt).
builder.Services.AddSingleton(TimeProvider.System);

// Imports the key here, so a bad one is a startup failure. The private half exists only in this process (ADR 0002).
builder.Services.AddSingleton(internalTokenOptions);
builder.Services.AddSingleton<IInternalTokenSigner, InternalTokenSigner>();

// Every failure the edge produces leaves as ProblemDetails with a code and the correlation id (gateway.md). The
// customiser covers the ones the framework writes for itself — an unhandled exception, or Kestrel refusing an
// oversized body with a 413 — which would otherwise carry neither.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;

    context.ProblemDetails.Extensions.TryAdd("code", GatewayProblem.CodeFor(status));
    context.ProblemDetails.Extensions.TryAdd(
        "correlationId", System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty);
});

builder.Services.AddArtsRateLimiter(gatewayOptions);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;

    // Nothing is trusted unless configuration names it. The defaults trust loopback, which would mean believing
    // an X-Forwarded-For from anything that reached us over localhost.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var proxy in gatewayOptions.TrustedProxies)
    {
        if (proxy.Contains('/', StringComparison.Ordinal))
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(proxy));
        }
        else
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }
    }
});

builder.Services.AddResponseCompression(options =>
{
    // Only over HTTPS, and the middleware skips anything the destination already encoded.
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

// YARP: the route table is configuration (gateway.md), and destinations resolve through Aspire service discovery
// rather than naming a host or port anywhere in this repo.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddGatewayTransforms();

var app = builder.Build();

// Order is the substance of this file; each line earns its position (ADR 0022).

// Registers an OnStarting callback rather than setting headers here, so they survive the exception handler
// clearing the response (see SecurityHeadersMiddleware). Position is therefore not what makes this work, but it
// stays first so the callback is registered before anything can fail.
app.UseSecurityHeaders();

// A fault or an empty error status from anywhere below still answers ProblemDetails.
app.UseExceptionHandler();
app.UseStatusCodePages();

// Only when a proxy is actually trusted. With KnownProxies and KnownIPNetworks both empty, this middleware was
// observed to honour a spoofed X-Forwarded-For and set RemoteIpAddress from it — empty lists do not mean "trust
// nobody". Rather than depend on exactly which condition causes that, the middleware that interprets these headers
// is simply absent until configuration names a proxy to trust (ADR 0022).
if (gatewayOptions.TrustedProxies.Count > 0)
{
    app.UseForwardedHeaders();
}

// Whatever an untrusted caller sent is removed, after any trusted proxy's values have been consumed above.
app.UseTrustedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseResponseCompression();

// Explicit, so the two routing-aware middlewares below are unambiguous rather than relying on where minimal
// hosting would insert routing for us.
app.UseRouting();

// After routing: it reads the matched route's RateLimiterPolicy metadata.
app.UseRateLimiter();

// After edge policy, before the proxy: an internal path is refused having had the security headers applied, and
// without reaching a destination.
app.UseInternalPathBlock();

app.MapDefaultEndpoints();

app.MapReverseProxy();

app.Run();
