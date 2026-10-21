using Arts6311.AppHost;
using Arts6311.AppHost.Parameters;
using Aspire.Hosting.JavaScript;

var builder = DistributedApplication.CreateBuilder(args);

// ADR 0007. Parsed up front so a bad value stops the AppHost before anything starts. It decides whether
// squarefake or the sandbox credentials exist once the Square prompts add them.
var squareMode = SquareModes.Parse(builder.Configuration[SquareModes.ConfigurationKey]);

// Generated on first run and persisted to this machine's user secrets: nobody chooses these, so nobody is
// prompted for them, and a clean clone starts with zero prompts (.claude/rules/aspire.md).
var sqlPassword = builder.AddParameter("sql-password", new GenerateParameterDefault(), secret: true, persist: true);
var redisPassword = builder.AddParameter("redis-password", new GenerateParameterDefault(), secret: true, persist: true);

// ADR 0002: the gateway signs with this private key; the API gets only the public key derived from it.
var internalTokenSigningKey = builder.AddParameter(
    "internal-token-signing-key", new EcdsaP256PrivateKeyDefault(), secret: true, persist: true);

// Keys the HMAC of idempotent request fingerprints (prompt 1.6); base64 of 32 bytes. The API only: the Worker's
// expired-record sweep needs no key.
var idempotencyFingerprintKey = builder.AddParameter(
    "idempotency-fingerprint-key", new Base64KeyDefault(), secret: true, persist: true);

// The first Admin, seeded by MigrationService (prompt 1.9). The email defaults from appsettings.json
// (Parameters:seed-admin-email); override it with
//   dotnet user-secrets set "Parameters:seed-admin-email" "<you@example.org>" --project src/Arts6311.AppHost
// The password is generated and persisted; read it with `dotnet user-secrets list --project src/Arts6311.AppHost`.
var seedAdminEmail = builder.AddParameter("seed-admin-email");

// Where new artist submissions and contact messages are announced (ADR 0047). Per environment: the default in
// appsettings.json is a non-routable .localhost address, so nothing real is emailed unless an environment says so;
// production alone sets the centre's inbox. Locally every email lands in Mailpit whatever the address.
//   dotnet user-secrets set "Parameters:forms-staff-email" "<inbox>" --project src/Arts6311.AppHost
var formsStaffEmail = builder.AddParameter("forms-staff-email");
var seedAdminPassword = builder.AddParameter(
    "seed-admin-password", new GenerateParameterDefault(), secret: true, persist: true);

// SQL: a SQL Server container in run mode, Azure SQL when published (ADR 0008). The callback runs in run mode
// only, so DbGate, which receives the sa credential, never reaches a published model.
var sql = builder.AddAzureSqlServer("sql")
    .RunAsContainer(container => container
        .WithPassword(sqlPassword)
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume("arts6311-sql-data")
        .WithDbGate(dbGate => dbGate.WithLifetime(ContainerLifetime.Persistent)));
var appdb = sql.AddDatabase("appdb");

// Storage: Azurite in run mode, Azure Storage when published. Containers are declared here so the emulator and
// a deployment both get them; consumers read them through "blobs" and never create them.
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite => azurite
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume("arts6311-storage-data"));
var blobs = storage.AddBlobs("blobs");
storage.AddBlobContainer("media");
storage.AddBlobContainer("dataprotection");

// Redis: gateway session tickets and the API's HybridCache L2 (ADR 0009; Azure hosting is still DECIDE).
// The data volume also turns on Redis persistence, so sessions survive a container restart.
var cache = builder.AddRedis("cache", password: redisPassword)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("arts6311-redis-data");

// Run mode only: DbGate receives the Redis credential. It is the same DbGate container as SQL's, so one browser
// tool shows both. A key deleted there is deleted for every running service.
if (builder.ExecutionContext.IsRunMode)
{
    cache.WithDbGate(dbGate => dbGate.WithLifetime(ContainerLifetime.Persistent));
}

// Mail: Mailpit catches every email locally (its UI is on the dashboard). Publish mode maps this to Azure
// Communication Services in prompt 6.1.
var mail = builder.AddMailPit("mail");

// One-shot: applies EF migrations, runs seeders, exits 0 (or 1 on failure). Dependents WaitForCompletion,
// so they never start against an old schema.
var migrations = builder.AddProject<Projects.Arts6311_MigrationService>("migrations")
    .WithEnvironment("SeedAdmin__Email", seedAdminEmail)
    .WithEnvironment("SeedAdmin__Password", seedAdminPassword)
    .WithReference(appdb)
    .WaitFor(appdb);

// Every mode: MigrationService seeds the site's own pictures into Media (prompt 3.3), and in Development the content
// seed credits exhibitions through the Artists facade, which composes Media too (ADR 0033).
migrations.WithReference(blobs).WaitFor(blobs);

// The API receives only the derived public key. Its cache, blobs and gateway references arrive with the
// prompts that use them. Its launch profile carries no ports, only ASPNETCORE_ENVIRONMENT (without it the app
// runs as Production and ServiceDefaults maps no /health), so the endpoint here is dynamic: there are no
// fixed ports in the repo.
var apiService = builder.AddProject<Projects.Arts6311_Api>("api")
    .WithHttpEndpoint()
    .WithEnvironment(async context =>
    {
        var privateKeyPem = await internalTokenSigningKey.Resource.GetValueAsync(context.CancellationToken)
            ?? throw new InvalidOperationException("internal-token-signing-key did not resolve to a value.");
        context.EnvironmentVariables["InternalToken__PublicKeyPem"] = InternalTokenKeys.DerivePublicKeyPem(privateKeyPem);
    })
    .WithEnvironment("Idempotency__FingerprintKey", idempotencyFingerprintKey)
    .WithReference(appdb)
    .WaitFor(appdb)

    // HybridCache's L2 (aspire.md). Shared with the Worker, which is what lets a revalidation there stop this
    // host serving a cached page: with L1 alone each process would only invalidate its own copy (ADR 0036).
    .WithReference(cache)
    .WaitFor(cache)

    // The Data Protection key ring, shared with the Worker: it mints Identity's confirmation and reset tokens,
    // this host validates them, and separate rings would fail every emailed link (ADR 0023).
    .WithReference(blobs)
    .WaitFor(blobs)
    .WaitForCompletion(migrations)
    .WithHttpHealthCheck("/health");

// The two Angular apps, from the one workspace in src/web (frontend.md). Run mode starts each app's dev server
// through `npm run start:<app>`, which hands Aspire's PORT to `ng serve` (scripts/ng-serve.mjs); nobody runs
// ng serve by hand. Neither endpoint is external: the browser reaches both only through the gateway (ADR 0001).
//
// AddJavaScriptApp gives each app an installer (`npm install` in its directory) that the app waits for, so a clean
// clone needs no manual npm step. Both apps share src/web's one node_modules, and two installs in one folder at once
// corrupt it, so the admin's installer waits for the site's to finish; the second finds everything in place.
//
// The site arrived ahead of 3.2 because the staff sign-in page is a site route (ADR 0024, 0027); 3.2 wired its reads
// (below, after the gateway). Publish-mode shapes (a Node server for the site, a static bundle for the admin) are 6.1's.
const string webWorkspace = "../web";

// `--no-save`: an install at startup must never rewrite package-lock.json, which changes only by a person's hand.
string[] installArgs = ["--no-save", "--no-audit", "--no-fund"];

var site = builder.AddJavaScriptApp("site", webWorkspace, "start:site")
    .WithNpm(installArgs: installArgs)
    .WithHttpEndpoint(env: "PORT")

    // Served by server.ts without rendering a page, so it says "the SSR server is up" and nothing more.
    .WithHttpHealthCheck("/robots.txt");

var admin = builder.AddJavaScriptApp("admin", webWorkspace, "start:admin")
    .WithNpm(installArgs: installArgs)
    .WithHttpEndpoint(env: "PORT")

    // The SPA's base href (angular.json). The dev server serves the app under it and nothing at "/".
    .WithHttpHealthCheck("/admin/");

builder.CreateResourceBuilder<ExecutableResource>(InstallerOf(admin).Name)
    .WaitForCompletion(builder.CreateResourceBuilder<ExecutableResource>(InstallerOf(site).Name));

// The edge (ADR 0001): the ONLY resource with an external endpoint, locally and in Azure. Everything the browser
// sees comes from this one origin, which is what cookies, antiforgery, CSP and Square webhooks all depend on.
// WithReference is what service discovery resolves "https+http://api", "https+http://site" and
// "https+http://admin" against; there is no host or port in the gateway's configuration.
//
// Run mode serves HTTPS on the ASP.NET Core developer certificate: the session and antiforgery cookies are Secure and
// __Host- prefixed, and antiforgery with SecurePolicy=Always refuses a plain-http request outright, so over http every
// /bff call and every unsafe request was a 500 (found by the 1.12 end-to-end test). Publish mode keeps a plain HTTP
// target port, because Azure Container Apps terminates TLS at its ingress and the container has no certificate; making
// the gateway see those requests as HTTPS (trusted forwarded headers) is prompt 6.1's.
var gatewayScheme = builder.ExecutionContext.IsRunMode ? "https" : "http";
var gateway = builder.AddProject<Projects.Arts6311_Gateway>("gateway");

// The endpoint first, then marked external: WithExternalHttpEndpoints only promotes endpoints that already exist, so
// on its own it would leave the gateway with none. No port here either — nothing in the repo is fixed.
gateway = builder.ExecutionContext.IsRunMode ? gateway.WithHttpsEndpoint() : gateway.WithHttpEndpoint();
gateway
    .WithExternalHttpEndpoints()
    .WithEnvironment("InternalToken__SigningKeyPem", internalTokenSigningKey)
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(site)
    .WaitFor(site)
    .WithReference(admin)
    .WaitFor(admin)
    .WithHttpHealthCheck("/health", endpointName: gatewayScheme);

// The site's server-side reads (3.2, frontend.md): SSR asks the gateway, the only way into the API (ADR 0001), at the
// origin given here; the browser asks relatively and never sees it. A reference, not a WaitFor — the gateway already
// waits on the site, so waiting back would be a cycle, and SiteApi treats a read before the gateway is up as a failed
// one (a 503, not a cached page). The developer certificate is trusted explicitly because run mode's gateway is HTTPS
// on it and Node trusts only its own CA list otherwise; Aspire hands Node the certificate through NODE_EXTRA_CA_CERTS,
// in run mode only (https://learn.microsoft.com/en-us/dotnet/aspire/app-host/certificate-trust).
//
// NG_ALLOWED_HOSTS is Angular's own variable: the hosts the SSR engine will render for, merged with angular.json's
// build-time list (frontend.md, "security.allowedHosts comes from configuration"). Run mode's is the gateway's host;
// publish mode adds the Container Apps domain and 6311arts.com in 6.1. SITE_MOCK_FALLBACK serves the bundled sample
// data when a read fails, for design work only; it is off by default.
var siteAllowedHosts = builder.AddParameter("site-allowed-hosts");
var siteMockFallback = builder.AddParameter("site-mock-fallback");
site
    .WithEnvironment("SITE_API_ORIGIN", gateway.GetEndpoint(gatewayScheme))
    .WithEnvironment("NG_ALLOWED_HOSTS", siteAllowedHosts)
    .WithEnvironment("SITE_MOCK_FALLBACK", siteMockFallback)
    .WithDeveloperCertificateTrust(true);

// The outbox dispatcher, scheduler and reconciler host (prompts 1.4+). No HTTP endpoint, so it reports a
// state, not a health status.
builder.AddProject<Projects.Arts6311_Worker>("worker")
    .WithReference(appdb)
    .WaitFor(appdb)

    // The same HybridCache L2 the API reads: the site.revalidate-requested handler invalidates tags there, which
    // is the only path from a published change to the API's cached pages (ADR 0036).
    .WithReference(cache)
    .WaitFor(cache)

    // Every email in the system is an outbox message, so this is the only host that sends one (ADR 0023).
    .WithReference(mail)
    .WaitFor(mail)

    // The Data Protection key ring it shares with the API, for the tokens it mints.
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithEnvironment("Email__FromAddress", "no-reply@6311arts.test")

    // Links in email point at the gateway, the only public endpoint (ADR 0001). A reference, not a WaitFor: the
    // Worker needs the address to build a link, not the gateway to be up before it starts — waiting would invert
    // the dependency, since the gateway already waits on the API.
    .WithEnvironment("Email__SiteOrigin", gateway.GetEndpoint(gatewayScheme))

    // The staff inbox for the forms' notifications; only this host sends email.
    .WithEnvironment("Forms__StaffAddress", formsStaffEmail)
    .WaitForCompletion(migrations);

builder.Build().Run();

// The installer resource AddJavaScriptApp created for an app, found through the annotation it links them with.
static ExecutableResource InstallerOf(IResourceBuilder<JavaScriptAppResource> app) =>
    app.Resource.Annotations.OfType<JavaScriptPackageInstallerAnnotation>().Single().Resource;
