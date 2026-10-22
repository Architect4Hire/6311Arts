using Arts6311.Api.Authentication;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Caching;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Classes;
using Arts6311.Domain.Modules.Exhibitions;
using Arts6311.Domain.Modules.Forms;
using Arts6311.Domain.Modules.Identity;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Instructors;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Memberships;
using Arts6311.Domain.Modules.Pages;
using Arts6311.Domain.Modules.Preview;
using Arts6311.Domain.Modules.Site;
using Arts6311.Domain.Modules.Redirects;
using Arts6311.Domain.Modules.Shop;
using Arts6311.Domain.Modules.Operations;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Kestrel:Limits from appsettings.json: 1 MiB for every request body, raised only where a route says so (YARP's
// MaxRequestBodySize, [RequestSizeLimit]). Kestrel does not bind its limits from configuration by itself (ADR 0030).
builder.WebHost.ConfigureKestrel(kestrel => builder.Configuration.GetSection("Kestrel:Limits").Bind(kestrel.Limits));

builder.AddSqlServerDbContext<ArtsDbContext>(
    ArtsDbContext.ConnectionName, configureDbContextOptions: ArtsDbContextOptions.Configure);

// The IDistributedCache HybridCache picks up as its L2 (aspire.md). It has to be registered before
// AddArtsKernel's AddArtsCaching so HybridCache sees it, and it is what makes a revalidation in the Worker reach
// this host's entries: with L1 alone each process would only ever invalidate its own copy (ADR 0036). The tag
// itself arrives on the invalidation bus, because HybridCache never re-reads a tag's time from L2 (ADR 0049).
builder.AddRedisDistributedCache("cache");

builder.Services.AddArtsKernel();
builder.Services.AddArtsCacheInvalidation();
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

// The people who teach (content.md, ADR 0043). Its facade asks Media about portraits and Artists about the artist an
// instructor also is, so it comes after both; Classes asks it about each run's instructor, so it comes before Classes.
builder.Services.AddInstructorsModule();

// Exhibitions (content.md). Its facade asks Media about heroes and Artists about credits, so it comes after both.
builder.Services.AddExhibitionsModule();

// Pages (content.md): sanitized HTML bodies. Its facade asks Media about the pictures a body shows.
builder.Services.AddPagesModule();

// Classes (content.md, ADR 0037, 0044): offerings, their runs and the runs' sessions. Its facade asks Media about the
// hero and the pictures a description shows, and Instructors about each run's instructor, so it comes after both.
builder.Services.AddClassesModule();

// The shop's artwork (content.md, ADR 0038). Its facade asks Media about the photographs and Artists about the
// credit, so it comes after both.
builder.Services.AddShopModule();

// Its CSV import (ADR 0053), an idempotent command: here only, beside AddArtsIdempotency above.
builder.Services.AddShopImport();

// Membership tiers (content.md, ADR 0040). It asks no other module anything: a plan credits nobody and shows no
// library image.
builder.Services.AddMembershipsModule();

// Redirects (content.md): the CMS's list and the site's. Its slug-change handler is the Worker's (ADR 0035).
builder.Services.AddRedirectsModule();

// The public forms and the CMS inbox (ADR 0047). A submission's pictures go through Media's facade, so it comes
// after AddMediaModule. Its emails are composed and sent by the Worker.
builder.Services.AddFormsModule(builder.Configuration);

// Previews (ADR 0036): staff mint a signed, expiring link here and the site reads it back. After the content
// modules, whose facades it asks for the record, and after Data Protection, which signs the token.
builder.Services.AddPreviewModule();

// The site's reads that span modules (ADR 0042): the artist directory and pages, which need Shop's work, and the
// sitemap, which needs every content type. After the content modules, whose facades it asks.
builder.Services.AddSiteModule();

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
