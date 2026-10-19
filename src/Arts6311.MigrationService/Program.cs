using Arts6311.Domain.Managers.Persistence;
using Arts6311.MigrationService;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<ArtsDbContext>(
    ArtsDbContext.ConnectionName, configureDbContextOptions: ArtsDbContextOptions.Configure);

builder.Services.AddMigrationHost();
builder.Services.AddDbContextMigration<ArtsDbContext>();

var host = builder.Build();

// Resolve before running: RunAsync disposes the host and its service provider.
var runState = host.Services.GetRequiredService<MigrationRunState>();

await host.RunAsync();

return runState.ExitCode;
