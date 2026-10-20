# 6311arts — Project Rogers Edge

*Project memory written as a SCRUB prompt: Scope, Constraints, Restrictions, Usage, Behavior. Loaded every Claude Code session. Detailed rules live in `.claude/rules/`, step-by-step guidance in `.claude/skills/`, and read-only reviewers in `.claude/agents/`.*

## Scope

This is the rebuild of **6311arts.com**, a nonprofit art center at 6311 N. Clark St., Chicago. It moves the site off WordPress/WooCommerce onto:

- an **Angular SSR public site**
- a **closed mini CMS**
- an **ASP.NET Core API** with **ASP.NET Core Identity**
- a **YARP gateway** acting as the backend-for-frontend
- **two-way sync with Square** for the shop, classes and memberships
- orchestration by **Aspire**

The whole system runs on a developer laptop, offline by default, until it's published to **Azure Container Apps**.

```text
Staff edit content in the CMS ──outbox──> Square catalog (items, sessions, plans)
Square changes price/stock/subscriptions ──webhook → inbox → outbox──> CMS projections ──> public site
Visitors browse the SSR site, pay on-site (Web Payments SDK), join as members (Square subscriptions)
```

### In bounds

- the public site
- the CMS admin
- the API, gateway, worker, domain, migration service and importer
- the Square integration and its local fake
- the Azure publish model
- the `.claude/` toolkit

### Out of bounds unless explicitly requested

- plugins, custom fields or a page builder (the CMS is closed by design)
- a custom payment processor, refunds UI, or order/customer reporting in the CMS (Square owns money)
- Square Appointments/Bookings
- i18n
- native mobile apps
- a public third-party API
- multi-tenancy

## Constraints

### Stack

- **Runtime/orchestration:** .NET 10, Aspire AppHost + ServiceDefaults
- **Backend:** ASP.NET Core Web API, EF Core 10, SQL Server
  - Locally, a container via `AddAzureSqlServer(...).RunAsContainer()`; Azure SQL when published.
- **Edge:** `Arts6311.Gateway`, a YARP BFF and the **only** public endpoint. It's one origin for the site, admin, API, media and webhooks.
- **Identity:**
  - ASP.NET Core Identity in the API.
  - Gateway session tickets in Redis.
  - The gateway signs an ES256 internal token for the API. There's no OAuth/OIDC server.
- **Background work:** `Arts6311.Worker` runs:
  - the transactional **outbox** dispatcher (the SQL outbox *is* the queue; no message broker)
  - the scheduled publisher
  - the Square reconciler
- **Square:** official .NET SDK, referenced **only** by `Arts6311.Square`. `Arts6311.SquareFake` runs locally by default.
- **Storage:** Azurite locally (`media` and `dataprotection` containers); Azure Storage when published.
- **Email:** Mailpit locally; Azure Communication Services when published.
- **Frontend:** an Angular 22 workspace in `src/web`:
  - `projects/site`: SSR, migrated from `spike0`
  - `projects/admin`: CMS SPA
  - `projects/ui`: `@6311/ui` tokens and primitives

  All three are standalone, signals, zoneless, strict.
- **Observability:** OpenTelemetry via ServiceDefaults. One trace runs from gateway → API → SQL → outbox → Worker → Square.

### Solution layout

```text
src/
├── Arts6311.AppHost/          # complete application model, run mode + publish mode
├── Arts6311.ServiceDefaults/  # telemetry, health, resilience, discovery
├── Arts6311.Gateway/          # YARP BFF: session, internal token, edge policy
├── Arts6311.Api/              # controllers only (+ webhook intake, internal session routes)
├── Arts6311.Domain/           # module-first: Modules/<Context>/ + Managers/ shared kernel; ArtsDbContext; migrations
├── Arts6311.Square/           # the only assembly that references the Square SDK; implements ISquare*Gateway
├── Arts6311.SquareFake/       # local Square stand-in + signed webhook trigger (run mode, default)
├── Arts6311.Worker/           # outbox dispatcher, scheduled publish, reconciler
├── Arts6311.MigrationService/ # applies migrations + seeds once before api/worker start
├── Arts6311.Importer/         # WordPress → snapshot → blob + CMS import (explicit start; see .claude/rules/import.md)
├── Arts6311.Tests/
└── web/                       # Angular workspace: projects/site, projects/admin, projects/ui
docs/  plans/  adr/  runbooks/  scrub-prompts.md  prompts/wordpress-import-scrub-prompts.md
artifacts/                     # gitignored: WordPress snapshots
```

### Mandatory seams

```text
HTTP:    Controller → Facade → Business → DataLayer → Repository | Gateway
After a commit: DataLayer stages IOutboxWriter.Enqueue → single SaveChangesAsync → Worker dispatcher → keyed handler → Facade
Webhook: verify signature → SquareWebhookEvent inbox (unique event_id) → stage square.webhook-received → save → 200
```

- **Controllers** live only in the API and inject facades only. Everything below lives in the Domain.
- **Each layer calls only the next one,** through interfaces. Cross-module traffic is facade to facade.
- **ViewModels in, ServiceModels out.** EF entities and Square DTOs never cross the API boundary.
- **Every side effect after a commit is an outbox message.** That includes Square writes, emails, renditions and revalidation. Nothing calls a provider inline after a save.

### Field ownership (Square)

- **The CMS owns content:** names, descriptions, artist, images, categories, publish state, session dates, plan copy.
- **Square owns money and stock:** price after creation, stock/seats/sold-out, orders, payments, subscriptions, member status.
- **The CMS sends a starting price and stock exactly once,** at creation.

## Restrictions

### Security and payments

- **Never store, log or proxy card data.**
  - The Web Payments SDK tokenizes in the browser.
  - The source token is used once.
  - Amounts are never accepted from the client.
- **Never write a Square-owned field from a CMS path.** Never put a Square id on a core entity; use `SquareCatalogLink`.
- **Never call Square except through `ISquare*Gateway`, and never without an idempotency key** (the outbox message id).
- **Session secrets stay out of the browser.**
  - No tokens in browser storage, URLs or logs.
  - The browser holds only the `__Host-` session cookie.
  - Every unsafe request carries antiforgery, except the signature-verified webhook.
- **The API accepts only the gateway's internal token.** `/api/v1/internal/**` is unreachable from browsers.

### Environments and secrets

- **No hardcoded connection strings, hosts, ports, tokens or keys.**
  - Generated secrets are persisted AppHost parameters.
  - Developer-supplied secrets go in user-secrets.
  - Azure secrets are Key Vault references.
- **Run mode never needs an Azure subscription and never reaches Square production.** `Square:Mode` is `fake` (default) or `sandbox`.

### Hygiene

- **Never hand-edit generated migrations.** Never commit `bin/`, `obj/`, `node_modules/`, `dist/`, `.angular/`, `.azure/`, `.env*` or secrets.
- **Never run `ng serve` by hand.** Aspire launches the Angular apps.
- **Never deploy from a Claude session.** `aspire deploy` and `azd` are denied in settings; deploys run in CI or by a person.

## Usage

- **Whole system:** `aspire run` (dashboard). Browse only through the gateway endpoint. `Square:Mode=sandbox` plus the dev tunnel gives real Square sandbox webhooks.
- **Backend:**
  - `dotnet build -warnaserror`
  - tests: **`dotnet test`** from the repo root (Microsoft.Testing.Platform via `global.json`). Verified 2026-10-19 with SDK 10.0.402 and xunit.v3 4.0.2: all tests discovered and run. The xunit.v3 4.0.1 "Zero tests ran" bug creatorpantry hit doesn't reproduce. If it ever comes back, `dotnet run --project src/Arts6311.Tests` runs the same suite.
  - The suite includes the Aspire end-to-end test (`Category=E2E`, ADR 0026), which needs Docker. CI splits it: `dotnet test --filter-not-trait "Category=E2E"`, then `dotnet test --filter-trait "Category=E2E"`.
- **Migrations:**
  - `dotnet ef migrations add <Name> --project src/Arts6311.Domain --startup-project src/Arts6311.Api`
  - Restart `aspire run` so MigrationService applies it.
  - While `aspire run` holds the build output, redirect the build output path (`-p:BaseOutputPath=<scratch>/`).
- **Frontend** (`src/web`): `npm ci`, `npm run build`, `npm test` (Vitest; runs `ui`, `site` and `admin`). `aspire run` starts both apps' dev servers and installs packages itself (`--no-save`); browse them only through the gateway, and refresh to see changes (no live reload, ADR 0027).
- **Staff sign-in** is `/signin` on the site; the seeded admin is `Parameters:seed-admin-email` (default `admin@6311arts.localhost`) with the generated `seed-admin-password` from AppHost user-secrets.
- **Skills and reviewers:** use the matching skill before implementing a seam, and run the relevant read-only reviewers after.
- **Keep work atomic:** one route, entity, message type, handler, component or resource per prompt.

## Behavior

- **Plan before acting.** Inspect before editing. Plan any change that crosses more than one layer, and wait for approval on non-trivial work.
- **Follow the nearest rule and the matching skill.** Don't improvise a parallel architecture.
- **Verify fast-moving APIs** (Aspire, YARP, Square SDK, Angular) against current first-party docs before using them, and cite the page for Square behaviour.
- **Tests come with every behaviour change.** Run focused tests first, then the full suite. Anything touching Square passes against `Arts6311.SquareFake`.
- **Record each architectural decision as an ADR** in `docs/adr/`. Mark open decisions `DECIDE`.
- **Stop and propose a safe design** when a request would weaken field ownership, outbox atomicity, payment safety, session security or secret handling.
