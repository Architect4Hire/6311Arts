using Arts6311.AppHost;
using Arts6311.AppHost.Parameters;

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
builder.AddParameter("seed-admin-email");
builder.AddParameter("seed-admin-password", new GenerateParameterDefault(), secret: true, persist: true);

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
storage.AddBlobs("blobs");
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
builder.AddMailPit("mail");

// One-shot: applies EF migrations, runs seeders, exits 0 (or 1 on failure). Dependents WaitForCompletion,
// so they never start against an old schema.
var migrations = builder.AddProject<Projects.Arts6311_MigrationService>("migrations")
    .WithReference(appdb)
    .WaitFor(appdb);

// The API receives only the derived public key. Its cache, blobs and gateway references arrive with the
// prompts that use them. Its launch profile carries no ports, only ASPNETCORE_ENVIRONMENT (without it the app
// runs as Production and ServiceDefaults maps no /health), so the endpoint here is dynamic: there are no
// fixed ports in the repo.
builder.AddProject<Projects.Arts6311_Api>("api")
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
    .WaitForCompletion(migrations)
    .WithHttpHealthCheck("/health");

// The outbox dispatcher, scheduler and reconciler host (prompts 1.4+). No HTTP endpoint, so it reports a
// state, not a health status.
builder.AddProject<Projects.Arts6311_Worker>("worker")
    .WithReference(appdb)
    .WaitFor(appdb)
    .WaitForCompletion(migrations);

builder.Build().Run();
