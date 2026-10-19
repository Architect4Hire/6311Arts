using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<ArtsDbContext>(
    ArtsDbContext.ConnectionName, configureDbContextOptions: ArtsDbContextOptions.Configure);
builder.Services.AddArtsKernel();

var host = builder.Build();

host.Run();
