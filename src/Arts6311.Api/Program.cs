using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<ArtsDbContext>(
    ArtsDbContext.ConnectionName, configureDbContextOptions: ArtsDbContextOptions.Configure);
builder.Services.AddArtsKernel();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();
