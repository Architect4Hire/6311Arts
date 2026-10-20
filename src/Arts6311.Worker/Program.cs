using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Email;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Revalidation;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Classes;
using Arts6311.Domain.Modules.Exhibitions;
using Arts6311.Domain.Modules.Identity;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Pages;
using Arts6311.Domain.Modules.Redirects;
using Arts6311.Domain.Modules.Shop;
using Arts6311.Domain.Modules.Operations;
using Arts6311.Worker;
using Arts6311.Worker.Email;
using Microsoft.AspNetCore.Identity;
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

// The same Redis the API uses as its HybridCache L2. This host writes to it rather than reads from it: the
// site.revalidate-requested handler tells the shared cache to stop serving what a change affects, which is the
// only way an invalidation here reaches the API's entries (ADR 0036). Before AddArtsKernel, so HybridCache sees it.
builder.AddRedisDistributedCache("cache");

builder.Services.AddArtsKernel();
builder.Services.AddArtsIdempotencyMaintenance();
builder.Services.AddOperationsModule();

// The key ring for Identity's reset and confirmation tokens. This host mints them; the API validates them, so
// both must share it or every emailed link fails (ADR 0023).
builder.AddAzureBlobServiceClient("blobs");
builder.Services.AddArtsDataProtection(DataProtectionApplicationName.Value);

// The media.rendition-requested and media.purge-requested handlers, and the image library they use (ADR 0030).
builder.Services.AddMediaModule();

// The redirects.slug-changed handler: a published record's old address leads to its new one (ADR 0035).
builder.Services.AddRedirectsModule();

// The site.revalidate-requested handler: a content change stops the cache serving what it affects (ADR 0036).
builder.Services.AddArtsRevalidation();

// The content modules, for the scheduled publisher: it publishes what is due through each module's facade, and
// those facades ask Media about images, so they come after AddMediaModule. Artists before Exhibitions, whose
// facade asks Artists about credits (ADR 0033).
builder.Services.AddArtistsModule();
builder.Services.AddExhibitionsModule();
builder.Services.AddPagesModule();
builder.Services.AddClassesModule();
builder.Services.AddShopModule();

// The token provider is added by the host, not the module: it lives in the ASP.NET Core framework assembly, which
// the Domain deliberately does not reference.
builder.Services.AddIdentityModule(builder.Configuration)
    .AddTokenProvider<DataProtectorTokenProvider<ArtsUser>>(TokenOptions.DefaultProvider);

// The framework default lifetime is 24 hours, which leaves several reset links live at once and none superseded
// by a newer request. Configured here because the options type ships in the same framework assembly as the
// provider (ADR 0023).
builder.Services.Configure<DataProtectionTokenProviderOptions>(
    options => options.TokenLifespan = IdentityPolicy.EmailTokenLifetime);

// Email: the one email.send-requested handler, and the only IEmailSender in the system.
builder.Services.AddArtsEmail(builder.Configuration);
builder.Services.AddOptions<SmtpOptions>()
    .Configure<IConfiguration>((options, configuration) =>
        options.ConnectionString = configuration.GetConnectionString("mail") ?? string.Empty)
    .Validate(options =>
    {
        options.Validate();
        return true;
    })
    .ValidateOnStart();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

builder.Services.AddHostedService<OutboxDispatcherHostedService>();
builder.Services.AddHostedService<IdempotencyCleanupHostedService>();

// Publishes what staff scheduled, once a minute, guarded by each record's row version (ADR 0036).
builder.Services.AddHostedService<ScheduledPublishHostedService>();

var host = builder.Build();

host.Run();
