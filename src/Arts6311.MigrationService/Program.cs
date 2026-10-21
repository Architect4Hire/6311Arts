using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Classes;
using Arts6311.Domain.Modules.Exhibitions;
using Arts6311.Domain.Modules.Identity;
using Arts6311.Domain.Modules.Instructors;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Pages;
using Arts6311.Domain.Modules.Redirects;
using Arts6311.Domain.Modules.Memberships;
using Arts6311.Domain.Modules.Shop;
using Arts6311.MigrationService;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<ArtsDbContext>(
    ArtsDbContext.ConnectionName, configureDbContextOptions: ArtsDbContextOptions.Configure);

builder.Services.AddMigrationHost();
builder.Services.AddDbContextMigration<ArtsDbContext>();

// The kernel for IClock, ICorrelationContext and IAuditWriter, which the seeder's audit row needs. No token
// provider and no Data Protection: the seeded Admin is created pre-confirmed, so nothing here mints a token.
builder.Services.AddArtsKernel();
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddIdentitySeeder();

// The privacy and terms placeholders, in every environment and as Drafts: the footer links to both, and a legal
// placeholder must never go public by accident (ADR 0034). The kernel above provides the sanitizer they pass through.
builder.Services.AddPagesPlaceholderSeeder();

// The spike's fixed legacy redirects, in every environment, so the site reads them from the API (ADR 0035).
builder.Services.AddLegacyRedirectSeeder();

// The site's own pictures — the logo and the default social image — at fixed ids, published, in every environment
// (prompt 3.3): the site names them by id. So Media, on the "blobs" client, is composed here everywhere now.
builder.AddAzureBlobServiceClient("blobs");
builder.Services.AddMediaModule();
builder.Services.AddScoped<IDataSeeder, SiteImageSeeder>();

// The spike's artist roster, exhibitions, classes and shop, for a clean clone to show. Development only: elsewhere both come from
// the WordPress import (4.x), whose slugs these would collide with. The exhibition and instructor seeds credit artists
// through IArtistsFacade (facade to facade), whose facade asks Media about portraits — composed above (ADR 0033). The
// class seed names instructors through IInstructorsFacade (ADR 0043).
// The roster's seeder is registered first, then the instructors': seeders run in registration order.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddArtistsModule();
    builder.Services.AddInstructorsModule();
    builder.Services.AddArtistsDevelopmentSeeder();
    builder.Services.AddInstructorsDevelopmentSeeder();
    builder.Services.AddExhibitionsDevelopmentSeeder();
    builder.Services.AddClassesDevelopmentSeeder();
    builder.Services.AddShopDevelopmentSeeder();
    builder.Services.AddMembershipsDevelopmentSeeder();
}

var host = builder.Build();

// Resolve before running: RunAsync disposes the host and its service provider.
var runState = host.Services.GetRequiredService<MigrationRunState>();

await host.RunAsync();

return runState.ExitCode;
