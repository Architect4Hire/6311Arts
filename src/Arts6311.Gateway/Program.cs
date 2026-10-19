using System.Net;
using Arts6311.Gateway.Authentication;
using Arts6311.Gateway.Edge;
using Arts6311.Gateway.Proxy;
using Arts6311.Gateway.Session;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Bound eagerly, so a missing rate-limit policy or signing key stops the host at startup rather than failing one
// route at request time (the pattern 1.7 set for InternalTokenOptions).
var gatewayOptions = new GatewayOptions();
builder.Configuration.GetSection(GatewayOptions.SectionName).Bind(gatewayOptions);
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));

var internalTokenOptions = new InternalTokenOptions();
builder.Configuration.GetSection(InternalTokenOptions.SectionName).Bind(internalTokenOptions);

// Same reason: a session with a nonsensical lifetime would be a session that never expires, and the symptom
// would be nothing at all.
var sessionOptions = new BrowserSessionOptions();
builder.Configuration.GetSection(BrowserSessionOptions.SectionName).Bind(sessionOptions);
sessionOptions.Validate();
builder.Services.AddSingleton(sessionOptions);

// The clock. The Domain's AddArtsTime would normally register this, but the gateway holds no domain code and so
// does not reference it; DateTime.UtcNow is banned repo-wide (src/BannedSymbols.txt).
builder.Services.AddSingleton(TimeProvider.System);

// Imports the key here, so a bad one is a startup failure. The private half exists only in this process (ADR 0002).
builder.Services.AddSingleton(internalTokenOptions);
builder.Services.AddSingleton<IInternalTokenSigner, InternalTokenSigner>();

// Redis: the session ticket store (ADR 0009), and with it the gateway's only piece of state. Aspire's client
// brings the retry policy, the health check and the telemetry, so Redis being unreachable shows as an unhealthy
// gateway rather than as sign-ins that hang.
builder.AddRedisClient("cache");
builder.Services.AddSingleton<ISessionKeyValueStore, RedisSessionKeyValueStore>();
builder.Services.AddSingleton<ISessionTicketStore, SessionTicketStore>();
builder.Services.AddScoped<ISessionManager, SessionManager>();

// The internal routes the BFF calls on its own behalf. The address is a service-discovery name, so no host or
// port appears here (aspire.md); ServiceDefaults has already added discovery and resilience to every client.
builder.Services.AddHttpClient<IIdentityApiClient, IdentityApiClient>(
    client => client.BaseAddress = new Uri("https+http://api"));

// Antiforgery keys have to outlive the process and be the same in every replica, or a token minted by one
// gateway is refused by the next (ADR 0024). Redis, not the blob ring the API and Worker share: that ring is
// reached through the Domain's helper, and the gateway holds no domain code (ADR 0022).
builder.Services.AddDataProtection().SetApplicationName(GatewayDataProtection.ApplicationName);

// The repository is configured through options rather than PersistKeysToStackExchangeRedis, because that method
// wants a multiplexer or a Func<IDatabase> in hand and ours is in DI, registered by AddRedisClient above.
builder.Services.AddOptions<KeyManagementOptions>().Configure<IServiceProvider>((options, services) =>
    options.XmlRepository = new RedisXmlRepository(
        () => services.GetRequiredService<IConnectionMultiplexer>().GetDatabase(),
        GatewayDataProtection.KeyRingKey));

// The session is read by one scheme and nothing else: no cookie-authentication middleware, no OIDC handler.
builder.Services.AddAuthentication(GatewaySessionDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, GatewaySessionHandler>(GatewaySessionDefaults.Scheme, _ => { });

// No fallback policy: a YARP route that names no policy stays anonymous, which is what the public site and the
// anonymous API reads are. The gate is declared per route, in the route table (gateway.md).
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(GatewayPolicies.CmsEditor, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(GatewayRoles.Admin, GatewayRoles.Editor))
    .AddPolicy(GatewayPolicies.Session, policy => policy.RequireAuthenticatedUser());

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = AntiforgeryCookie.CookieTokenName;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.Cookie.IsEssential = true;

    // The header, and only the header: validating a form field would mean reading the request body at the edge,
    // which would break streaming for everything proxied through.
    options.HeaderName = AntiforgeryCookie.HeaderName;

    // The CSP already carries frame-ancestors 'none' (SecurityHeadersMiddleware). Letting antiforgery add its own
    // X-Frame-Options as well would mean two headers saying the same thing from two places.
    options.SuppressXFrameOptionsHeader = true;
});

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

// Reads the session cookie and the ticket behind it, which is what makes HttpContext.User non-anonymous and so
// what makes the proxy's internal-token transform fire (ADR 0022).
app.UseAuthentication();

// After authentication and before authorization, and both halves of that matter. An antiforgery token is bound
// to the signed-in identity, so validating it before the session is read compares it against an anonymous
// principal and refuses every token a signed-in person holds. Before authorization, so that a missing token on
// a protected route is the 400 auth.md's test list calls for rather than a 401 (ADR 0024).
app.UseArtsAntiforgery();

// This is what applies a YARP route's AuthorizationPolicy, and the policy on the BFF's own endpoints.
app.UseAuthorization();

app.MapDefaultEndpoints();

// The BFF's own endpoints. Mapped before the proxy for readability only — precedence, not order, is what puts
// /bff/** ahead of the site catch-all.
app.MapBff();

app.MapReverseProxy();

app.Run();
