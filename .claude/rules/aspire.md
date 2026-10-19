---
paths:
  - src/Arts6311.AppHost/**
  - src/Arts6311.ServiceDefaults/**
---
# Aspire and Local Composition

`Arts6311.AppHost` is the single source of truth for the application model, for both **run mode** (laptop) and **publish mode** (Azure Container Apps). Nothing required to run the system lives in a manual script or a developer-specific file.

## Resource graph

```text
sql (AddAzureSqlServer → RunAsContainer locally, Azure SQL when published) ─ appdb
  ├─> migrations (run-once: apply EF migrations + seed) ─completed─> api, worker
cache (Redis container locally; ADR decides Azure) ─> gateway (session tickets), api (HybridCache L2)
storage (AddAzureStorage → RunAsEmulator/Azurite locally) ─ blobs: media, dataprotection ─> api, worker
mail (Mailpit locally; Azure Communication Services when published) ─> worker
squarefake (run mode, square-mode=fake) ─> api, worker
api ─> gateway          worker (outbox dispatcher, reconciler, scheduled publish)
site (AddJavaScriptApp, Angular SSR) ─> gateway
admin (Angular SPA: dev server locally, static bundle host when published) ─> gateway
gateway (only external endpoint)    devtunnel (opt-in, sandbox webhooks) ─> gateway
importer (explicit start only)
dbgate (run mode only: one DbGate container connected to sql + cache; holds their credentials, never published)
```

Stable logical names: `sql`, `appdb`, `cache`, `storage`, `blobs`, `mail`, `squarefake`, `migrations`, `api`, `worker`, `site`, `admin`, `gateway`, `importer`, `dbgate`.

**Local tools** exist in run mode only: DbGate (via `WithDbGate()` inside `RunAsContainer` for SQL, and under `IsRunMode` for Redis) and Mailpit's own UI. Azurite has no web UI in the model; use Azure Storage Explorer.

## Dependency order

- `migrations` waits for `appdb` and **completes** before `api` and `worker` start (`WaitForCompletion`).
- `api` waits for `appdb`, `cache` and `blobs`.
- `worker` waits for `appdb`, `blobs` and `mail`.
- `gateway` waits for `api`, `site`, `admin` and `cache`.
- Use `WaitFor` and health checks. No sleeps.

## Configuration

- **No literal hosts or ports in application code.** Use references and service discovery.
- **Generated secrets.** These are generated on first run and persisted to user-secrets (custom `ParameterDefault`s, as in creatorpantry):
  - `sql-password`, `redis-password`
  - `internal-token-signing-key` (an ECDSA P-256 pair; the API receives only the derived public key)
  - `idempotency-fingerprint-key`
  - `seed-admin-password` (read it with `dotnet user-secrets list --project src/Arts6311.AppHost`)
- **Seed admin email.** `seed-admin-email` is a plain parameter that defaults from AppHost `appsettings.json` (`Parameters:seed-admin-email`). Override it in user-secrets.
- **Developer-supplied secrets.** Only needed for sandbox mode: `square-access-token`, `square-webhook-signature-key`. They're created only when `Square:Mode=sandbox`.
- **Never print a parameter's value** while checking secrets. List names only, for example from `dotnet user-secrets list --json`. PEM values span several lines, so a line-based `cut` leaks them.
- **`Square:Mode`** (`fake` | `sandbox`) is AppHost configuration, not a dashboard prompt. It decides which resources exist. The default is `fake`, so a clean clone runs offline with zero prompts.
- **AppHost stays declarative.** No domain logic or HTTP calls.
- **ServiceDefaults** owns OpenTelemetry, health checks, resilience and service discovery for every .NET project.

## Run mode vs publish mode

- **Run mode** never needs an Azure subscription. Azure-shaped resources run as containers or emulators. Nothing uses `AsExisting`/`PublishAsExisting` in run mode.
- **Publish mode** (`AddAzureContainerAppEnvironment`) maps:
  - `sql` → Azure SQL
  - `storage` → Azure Storage
  - secrets → Key Vault references
  - `mail` → Azure Communication Services

  Only `gateway` gets external ingress. `squarefake` and the dev tunnel exist only in run mode.
- **Verify APIs on aspire.dev first.** Verify every Aspire API (`AddAzureSqlServer`, `RunAsContainer`, `RunAsEmulator`, `AddJavaScriptApp`, `AddAzureContainerAppEnvironment`, dev tunnels, Mailpit) against https://aspire.dev before use. They move between versions. One version property in `Directory.Packages.props` plus the `global.json` SDK pin, and they move together.

## Verification

Run `aspire run` from a clean data volume and check the dashboard:

- all resources healthy
- migrations completed
- one trace from gateway → api → SQL
- the site and admin load only through the gateway URL
