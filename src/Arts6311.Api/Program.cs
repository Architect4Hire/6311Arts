using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Exhibitions;
using Arts6311.Domain.Modules.Identity;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Pages;
using Arts6311.Domain.Modules.Redirects;
using Arts6311.Domain.Modules.Operations;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Kestrel:Limits from appsettings.json: 1 MiB for every request body, raised only where a route says so (YARP's
// MaxRequestBodySize, [RequestSizeLimit]). Kestrel does not bind its limits from configuration by itself (ADR 0030).
builder.WebHost.ConfigureKestrel(kestrel => builder.Configuration.GetSection("Kestrel:Limits").Bind(kestrel.Limits));

builder.AddSqlServerDbContext<ArtsDbContext>(
    ArtsDbContext.ConnectionName, configureDbContextOptions: ArtsDbContextOptions.Configure);
builder.Services.AddArtsKernel();
builder.Services.AddArtsIdempotency(builder.Configuration);
builder.Services.AddOperationsModule();

// The same key ring the Worker uses. The Worker mints Identity's confirmation and reset tokens when it sends the
// email; this host is where the link is validated, so a separate ring would fail every one (ADR 0023).
builder.AddAzureBlobServiceClient("blobs");
builder.Services.AddArtsDataProtection(DataProtectionApplicationName.Value);

// The media library and /media/** delivery, on the same "blobs" client: the `media` container (media.md).
builder.Services.AddMediaModule();

// The artist roster (content.md). Its facade asks Media about portraits, so it comes after AddMediaModule.
builder.Services.AddArtistsModule();

// Exhibitions (content.md). Its facade asks Media about heroes and Artists about credits, so it comes after both.
builder.Services.AddExhibitionsModule();

// Pages (content.md): sanitized HTML bodies. Its facade asks Media about the pictures a body shows.
builder.Services.AddPagesModule();

// Redirects (content.md): the CMS's list and the site's. Its slug-change handler is the Worker's (ADR 0035).
builder.Services.AddRedirectsModule();

// The credential store (ADR 0002). AddIdentityCore only: no cookie scheme and no SignInManager, because the
// gateway owns the browser session and the API takes no cookies at all.
builder.Services.AddIdentityModule(builder.Configuration)
    .AddTokenProvider<DataProtectorTokenProvider<ArtsUser>>(TokenOptions.DefaultProvider);

// The framework default lifetime is 24 hours, which leaves several reset links live at once and none superseded
// by a newer request. Configured here because the options type ships in the same framework assembly as the
// provider (ADR 0023).
builder.Services.Configure<DataProtectionTokenProviderOptions>(
    options => options.TokenLifespan = IdentityPolicy.EmailTokenLifetime);

// Every failure leaves as ProblemDetails with a stable code and the correlation id (api-contract.md).
builder.Services.AddArtsProblemDetails();

// The gateway's ES256 internal token is the only credential the API accepts: no cookies, no second scheme, and
// no CORS, because the gateway is the only origin (ADR 0001, ADR 0002).
builder.Services.AddInternalTokenAuthentication(builder.Configuration);
builder.Services.AddArtsAuthorization();

builder.Services.AddArtsOpenApi();

// The Development-only gate is applied here, to the controller set, so a scaffolding route does not exist at all
// outside Development. An environment check inside an action runs after authorization, which would answer 401.
builder.Services.AddControllers()
    .AddDevelopmentOnlyControllerGate(builder.Environment.IsDevelopment());
builder.Services.AddArtsFormatters();
builder.Services.AddArtsApiBehavior();

var app = builder.Build();

// Before routing: an exception or an empty error status anywhere downstream still comes back as ProblemDetails.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

// Development only. In Azure the gateway proxies this path, also only in Development (gateway.md "API docs").
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapAuthenticatedControllers();

app.Run();
