using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Operations;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<ArtsDbContext>(
    ArtsDbContext.ConnectionName, configureDbContextOptions: ArtsDbContextOptions.Configure);
builder.Services.AddArtsKernel();
builder.Services.AddArtsIdempotency(builder.Configuration);
builder.Services.AddOperationsModule();

// Versioning conventions, ProblemDetails, OpenAPI and secure-by-default authentication arrive in 1.7.
builder.Services.AddControllers();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapControllers();

app.Run();
