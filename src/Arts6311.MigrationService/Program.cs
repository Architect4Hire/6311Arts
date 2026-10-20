using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Identity;
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

// The spike's artist roster, for a clean clone to show. Development only: elsewhere artists come from the WordPress
// import (4.x), whose slugs these would collide with.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddArtistsDevelopmentSeeder();
}

var host = builder.Build();

// Resolve before running: RunAsync disposes the host and its service provider.
var runState = host.Services.GetRequiredService<MigrationRunState>();

await host.RunAsync();

return runState.ExitCode;
