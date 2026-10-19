using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Operations;
using Arts6311.Worker;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// ServiceDefaults may not reference the Domain, so the outbox's consumer spans are added here, with the Worker's own
// job spans. The sampler is the OpenTelemetry default, set explicitly because the poll's silence depends on it: each
// pass runs under an unrecorded activity, and only a parent-based sampler drops that activity's children (outbox.md
// "One trace, no polling noise").
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .AddSource(OutboxDiagnostics.ActivitySourceName, WorkerDiagnostics.ActivitySourceName)
    .SetSampler(new ParentBasedSampler(new AlwaysOnSampler())));

builder.AddSqlServerDbContext<ArtsDbContext>(
    ArtsDbContext.ConnectionName, configureDbContextOptions: ArtsDbContextOptions.Configure);
builder.Services.AddArtsKernel();
builder.Services.AddArtsIdempotencyMaintenance();
builder.Services.AddOperationsModule();

builder.Services.AddHostedService<OutboxDispatcherHostedService>();
builder.Services.AddHostedService<IdempotencyCleanupHostedService>();

var host = builder.Build();

host.Run();
