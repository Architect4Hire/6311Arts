---
name: add-aspire-resource
description: >
  Add a resource (database, cache, storage, emulator, container, dev tunnel or project) to the 6311arts
  Aspire AppHost and wire it into the services that use it — local in run mode, Azure-shaped in publish
  mode, always through service discovery.
---

# Add an Aspire Resource

Work in `src/Arts6311.AppHost/`. Declare the resource, then reference it. Never hardcode connection details.

1. **Add the hosting package** with `aspire add <resource>`. Confirm the exact package and API on https://aspire.dev first; names move between versions.
2. **Declare it.** Run mode is a local container, emulator or runtime. Publish mode is the Azure-shaped equivalent with the same logical name.
3. **Wire consumers** with `.WithReference(x)` and `.WaitFor(x)`, or `.WaitForCompletion(x)` for run-once resources like `migrations`.
4. **Consume it** through the matching Aspire client integration, keyed to the resource name.
5. **Verify** with `aspire run`: the resource is healthy and the consumer connected. If it's Azure-shaped, also check that `aspire publish` maps it correctly.

## The shapes this repo uses

These are illustrative. Verify each API before use.

```csharp
// SQL: container locally, Azure SQL when published
var sql = builder.AddAzureSqlServer("sql").RunAsContainer(c => c
        .WithLifetime(ContainerLifetime.Persistent).WithDataVolume("arts6311-sql-data"));
var appdb = sql.AddDatabase("appdb");
// consume: builder.AddSqlServerDbContext<ArtsDbContext>("appdb");

// Storage: Azurite locally
var storage = builder.AddAzureStorage("storage").RunAsEmulator(a => a
        .WithLifetime(ContainerLifetime.Persistent).WithDataVolume("arts6311-storage-data"));
var blobs = storage.AddBlobs("blobs");
blobs.AddBlobContainer("media"); blobs.AddBlobContainer("dataprotection");

// Redis: gateway session tickets + HybridCache L2
var cache = builder.AddRedis("cache", password: redisPassword)
        .WithLifetime(ContainerLifetime.Persistent).WithDataVolume("arts6311-redis-data");

// Generated, persisted secrets (custom ParameterDefault classes, as in creatorpantry)
var signingKey = builder.AddParameter("internal-token-signing-key",
        new EcdsaP256PrivateKeyDefault(), secret: true, persist: true);

// Square fake: run mode + Square:Mode=fake only
if (builder.ExecutionContext.IsRunMode && squareMode == "fake")
    squareFake = builder.AddProject<Projects.Arts6311_SquareFake>("squarefake");

// Angular apps
var site = builder.AddJavaScriptApp("site", "../web", runScriptName: "start:site")
        .WithReference(api).WithHttpEndpoint(env: "PORT");
```

## Rules

- **Run mode never needs an Azure subscription.** No `AsExisting` in run mode.
- **Credentials are `AddParameter(..., secret: true)`.** Generate and persist them when no human needs to choose them.
- **Only `gateway` gets `WithExternalHttpEndpoints()`.**
- **The AppHost stays declarative.** Gate resources on configuration flags, not on logic.

## Checklist before done

- [ ] Declared with a stable logical name; local in run mode, mapped in publish mode
- [ ] Consumers wired with `WithReference` + `WaitFor` / `WaitForCompletion`
- [ ] Consumed through the Aspire client integration, keyed by name
- [ ] Credentials are secret parameters; nothing literal
- [ ] Dashboard healthy; `aspire publish` output checked if Azure-shaped
