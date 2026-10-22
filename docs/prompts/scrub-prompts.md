# 6311arts — SCRUB Prompts (v2)

These prompts drive Claude Code through Project Rogers Edge:

- an Angular SSR site and a closed mini CMS
- an ASP.NET Core API with Identity, behind a YARP BFF gateway
- a transactional outbox drained by a Worker
- two-way sync with Square
- orchestration by Aspire, running fully on a laptop until it's published to Azure Container Apps

The architectural source of truth is `CLAUDE.md` plus `.claude/rules/`, `.claude/skills/` and `.claude/agents/`. Those patterns come from `architect4hire/creatorpantry`: the seam, the module-first Domain, the outbox, idempotency, and the BFF with an internal token. The functional source is `docs/plans/square/square-integration-plan.md` and the Rogers Edge deck. If they conflict, stop and reconcile them before writing code.

## What changed from v1

| v1 | v2 (creatorpantry patterns) | Why |
|---|---|---|
| Azure Service Bus + emulator for outbound and inbound | **SQL transactional outbox is the queue.** The Worker dispatcher uses leases and keyed handlers. Webhooks land in an inbox table and hand off through the outbox. | One durable mechanism instead of two. Atomic with the domain write by construction. One fewer Azure resource. Proven in creatorpantry. |
| Identity cookie issued by the API, Data Protection shared with the gateway | **The gateway is the BFF.** It holds a Redis session ticket and an `__Host-` cookie, and signs an **ES256 internal token** for the API (creatorpantry B-13). | Revocable sessions. The API trusts one scheme. No key ring is shared across hosts. |
| `Arts6311.Data` + `Arts6311.Sync` | **`Arts6311.Domain`** (module-first, shared kernel) + **`Arts6311.Worker`** | Matches the seam and the module-boundary tests. |
| `SyncState` columns on content entities | **`SquareCatalogLink`** integration records. Content entities keep only read-only projections. | Provider ids stay off core entities (`external.md`). |
| Two separate Angular apps | **One Angular workspace:** `projects/site`, `projects/admin`, `projects/ui` | Shared tokens and primitives. One pinned prettier. |

## The SCRUB skeleton

```text
SCOPE:        one observable change and the exact repository seam it touches
CONSTRAINT:   stack, rule files, skills, prerequisites
RESTRICTION:  explicit exclusions and invariants that must not be weakened
UTILIZATION:  skills, read-only reviewers, tools
BEHAVIOR:     inspect, plan, wait for approval, implement, test, report
```

## How to use this file

- **Run in order, one prompt at a time,** with `/clear` between them. One prompt = one branch = one PR.
- **Each prompt is one seam:** one contract, entity, message type, handler, endpoint, component, resource or test group. Prompts that cross layers say so.
- **Every HTTP route** uses `add-endpoint`.
- **Every after-commit effect** uses `add-outbox-message`.
- **Every Square-synced type** uses `add-square-sync`.
- **Every UI** uses `new-component`.
- **Approve the plan, not the code.** Each prompt stops after its plan.
- **Mark a prompt `— done`** only after its stated verification passes.
- **Checkpoint prompts** (✅) end each phase. Don't start the next phase until they're clean.
- **Phase 4 is superseded.** The WordPress import is `prompts/wordpress-import-scrub-prompts.md` (I.1–I.14), and its
  order interleaves with Phase 5 — see the note under Phase 4.

## Prerequisites

| First needed | Requirement |
|---|---|
| Phase 0 | .NET 10 SDK, Node 22.22.3+ / 24.15+, Docker-compatible runtime, Aspire CLI |
| Phase 1 | Nothing external. All secrets are generated on first `aspire run`. |
| Phase 4 (the import series, `prompts/wordpress-import-scrub-prompts.md`) | Public WordPress REST read access; then a read-only WooCommerce key and a read-only Application Password, both created in I.2 and revoked at I.14 |
| Phase 5 (sandbox, opt-in) | Square developer account, sandbox app, access token, webhook signature key; dev tunnel login |
| Phase 6 | Azure subscription, resource groups for dev and prod, GitHub OIDC federated credential |

Everything through Phase 5 runs offline against `Arts6311.SquareFake`. The one exception is the WordPress import
series, which reads the live site — GET only, read-only (`import.md`).

## Repo review findings (2026-10-18)

The repo has `spike0/`, an Angular 22 SSR site that reads WordPress and WooCommerce, plus `docs/plans/square/`. There's no .NET code. What to carry and what to fix:

**Carry forward:**

- `SeoService` and per-page meta
- JSON-LD
- the sitemap and robots routes
- the legacy 301 map
- SSR render modes
- `s-maxage` headers
- the transfer cache
- hydration with event replay
- money in cents
- the mock data (as development seed)

**Fix:**

- `WpApi`, `buyUrl()` and `cartUrl()` point at WooCommerce.
- Express `/api/subscribe` and `/api/contact` collide with the gateway's `/api/**`.
- The logo, OG image and product images are WordPress upload URLs.
- `id: number` doesn't fit Square's string ids or our GUIDs.
- All test schematics are disabled.
- `allowedHosts` is hardcoded.
- There's an empty root `package-lock.json`, a nested `.gitignore`, and a committed CLI analytics id.
- The README says "WordPress stays".

---

# Phase 0 — Repo and toolkit

## 0.1 — Restructure the repo into the target layout

```
SCOPE: Reshape the repo without changing site behavior. Create src/web as an Angular workspace and move
spike0 into it as projects/site (git mv, keep history); keep its package.json scripts working from
src/web (add start:site / build:site). Move spike0/wordpress/6311-headless.php to
src/web/projects/site/legacy/ with a README saying it is importer reference only. Delete the empty root
package-lock.json and spike0/.gitignore; remove the CLI analytics id from angular.json. Rewrite the root
README.md for the target architecture with "run: aspire run" as a placeholder. Create docs/adr/ and
docs/runbooks/ with READMEs.

CONSTRAINT: CLAUDE.md layout. `npm ci && npm run build` in src/web must still produce the same SSR build.

RESTRICTION: Do NOT change Angular source, routes, styles or server.ts. Do NOT add .NET projects, the
admin or ui projects yet.

UTILIZATION: Plan mode; git mv.

BEHAVIOR: Show the moves/deletions, the workspace angular.json diff and the README outline. Wait for
approval. Apply, run the build, report the result and a two-level tree.
```

## 0.2 — Install and verify the toolkit

```
SCOPE: Commit CLAUDE.md and the .claude/ folder as provided (rules, skills, agents, hooks, settings).
Make the hooks executable. Run @skills-evals against the toolkit and the current repo.

CONSTRAINT: .claude/README.md precedence.

RESTRICTION: Do NOT rewrite rules or skills in this step — report needed edits separately. No app code.

UTILIZATION: @skills-evals.

BEHAVIOR: Commit, run the reviewer, and return its findings as a list of proposed toolkit edits for my
approval.
```

## 0.3 — Baseline ADRs

```
SCOPE: Write docs/adr/0001…0010, one short ADR each (context, decision, options considered,
consequences, rules affected):
  0001 Single-origin YARP gateway as BFF        0002 Gateway session + ES256 internal token (no OIDC)
  0003 SQL transactional outbox, no broker       0004 Module-first Domain + seam
  0005 Square field ownership                    0006 Square ids in SquareCatalogLink, not entities
  0007 Square fake by default, sandbox opt-in     0008 Run mode offline / publish to Azure Container Apps
  0009 Redis for session tickets (Azure hosting = DECIDE, see 6.1)
  0010 CSoon template license — DECIDE before launch

CONSTRAINT: Each ADR cites the rule file(s) it backs and the creatorpantry decision it adopts (B-02,
B-13) where relevant.

RESTRICTION: Do NOT decide the DECIDE items; list the options and costs.

UTILIZATION: Read the rules first.

BEHAVIOR: Draft all ten, wait for my review, then commit.
```

---

# Phase 1 — Local platform

## 1.1 — Solution skeleton

```
SCOPE: Create the .NET 10 solution (Arts6311.slnx) with empty projects: AppHost, ServiceDefaults,
Gateway, Api, Domain, Square, Worker, MigrationService, Tests. Add Directory.Build.props
(nullable, warnings as errors, analyzers), Directory.Packages.props (central package management,
AspireVersion property) and global.json pinning the SDK and Aspire.AppHost.Sdk. Every service calls
AddServiceDefaults().

CONSTRAINT: aspire.md; verify template names and the Aspire version on aspire.dev.

RESTRICTION: No resources, entities, endpoints or UI. No hardcoded ports.

UTILIZATION: aspire-init / aspireify skills; plan mode.

BEHAVIOR: Plan the projects, references (Api→Domain; Worker→Domain; Domain→none; Square→Domain
interfaces only) and commands. Wait. Scaffold, `dotnet build -warnaserror`, report.
```

## 1.2 — AppHost resources and generated secrets

```
SCOPE: Declare run-mode resources in the AppHost: sql (AddAzureSqlServer().RunAsContainer(), persistent
volume) + appdb; storage (RunAsEmulator) + blobs + containers media and dataprotection; cache (Redis,
persistent, password parameter); mail (Mailpit). Add generated, persisted secret parameters with custom
ParameterDefaults ported from creatorpantry: sql-password, redis-password, internal-token-signing-key
(ECDSA P-256; pass only the derived public key to the API), idempotency-fingerprint-key. Add
seed-admin-email / seed-admin-password parameters. Add Square:Mode config (default fake).  Make sure it implement dbgate and any management utilities necessary.

CONSTRAINT: aspire.md; add-aspire-resource skill; verify every API on aspire.dev.

RESTRICTION: No consumers yet besides placeholders. No Azure subscription needed. No literal secrets.

UTILIZATION: add-aspire-resource.

BEHAVIOR: Plan, wait, implement, `aspire run` from empty volumes, report each resource's health and
confirm no prompts appeared for generated secrets.
```

## 1.3 — Domain shared kernel + MigrationService

```
SCOPE: In Arts6311.Domain create the shared kernel Managers/: Persistence (ArtsDbContext, IDataSeeder),
Time (IClock), Results (OperationResult, error codes), Paging (cursor), Caching (HybridCache wrapper
with tag helpers), Audit (AuditEntry + IAuditWriter staged like the outbox). Add ModuleBoundaryTests
(no module references another module's non-facade types; kernel depends on no module). Create
MigrationService: applies migrations, runs seeders, exits; AppHost wires migrations → appdb and
api/worker WaitForCompletion(migrations). Register the DbContext via the Aspire SQL Server EF
integration keyed "appdb".

CONSTRAINT: backend.md; aspire.md.

RESTRICTION: No modules or business entities yet. Create the initial migration but STOP for review.

UTILIZATION: add-aspire-resource; @architecture-reviewer.

BEHAVIOR: Plan, wait, implement, show the migration, wait, apply via aspire run, run tests, report.
```

## 1.4 — Transactional outbox kernel

```
SCOPE: Port creatorpantry's Managers/Outbox into Arts6311.Domain: OutboxMessage, OutboxMessageStatus
(Pending/Leased/Completed/Poisoned), OutboxPolicy, OutboxMessageConfiguration (Status + LeasedBy as
concurrency tokens; indexes on Status+AvailableAt and Status+LeaseExpiresAt), IOutboxWriter/OutboxWriter
(stage on the ambient ArtsDbContext, never save), IOutboxDispatcher/OutboxDispatcher (guarded per-row
claim, lease-expiry reclaim, per-message outcome save, backoff, poison), IOutboxMessageHandler +
OutboxMessageEnvelope, AddOutbox(). Add a CorrelationContext that supplies the request/activity
correlation id to Enqueue. Migration for the table.

CONSTRAINT: outbox.md (authoritative); keep creatorpantry's behavior and comments where they explain
why.

RESTRICTION: No message types or handlers beyond a test-only handler. Do NOT add a broker. Migration
created, not applied, until I approve.

UTILIZATION: add-outbox-message (read for conventions); @outbox-reviewer.

BEHAVIOR: Plan, wait, implement. Tests: two dispatchers racing deliver once; expired lease is
reclaimed; backoff doubles and caps; poison after MaxAttempts; one message's failure doesn't lose
another's outcome; LastError truncated. Report.
```

## 1.5 — Worker host + first end-to-end message

```
SCOPE: Arts6311.Worker: OutboxDispatcherHostedService (PeriodicTimer, fresh scope per pass, logs a
summary only when it claimed work). Add a `system.ping-requested` event + handler (writes an audit
entry) and an Operations endpoint POST /api/v1/cms/ops/ping (CmsAdmin, temporary; stages the message in
a DataLayer). AppHost: worker WithReference(appdb) WaitForCompletion(migrations).

CONSTRAINT: outbox.md; add-outbox-message; add-endpoint.

RESTRICTION: The ping route is development scaffolding — mark it [Obsolete] with a removal note; it is
deleted in 1.12. No other handlers.

UTILIZATION: add-outbox-message; @outbox-reviewer.

BEHAVIOR: Plan, wait, implement, run the atomicity test (failed save → no row, no message), then
aspire run and show me one trace spanning api → SQL → worker → handler in the dashboard.
```

## 1.6 — Idempotency kernel

```
SCOPE: Port creatorpantry's Managers/Idempotency: IdempotencyRecord (+ configuration), HMAC request
fingerprint keyed by idempotency-fingerprint-key, IIdempotentCommandExecutor (facade opt-in, wraps one
Business call, writes through the request DbContext so the record commits with the result), policy
(key length, retention), cleanup sweep in the Worker.

CONSTRAINT: backend.md; api-contract.md (Idempotency-Key semantics: replay → original result; same key,
different body → 422).

RESTRICTION: Not applied automatically to any route. Migration created, not applied, until approved.

UTILIZATION: add-background-job (cleanup sweep); @architecture-reviewer.

BEHAVIOR: Plan, wait, implement; tests for replay, mismatch, concurrent first use, expiry; report.
```

## 1.7 — API host conventions + internal-token authentication

```
SCOPE: Arts6311.Api: controllers only; /api/v1 versioning; ProblemDetails with stable codes +
correlationId; FluentValidation integration; OpenAPI (Development only) + OpenApiContractTests with
the snapshot at Tests/Api/Snapshots/openapi-v1.json; authentication that accepts ONLY the gateway's
ES256 internal token (iss 6311-gateway, aud 6311-api, ≤2 min, token_use) using the public key from the
AppHost; MapAuthenticatedControllers (secure by default); policies CmsEditor, CmsAdmin, Member,
InternalService. Health endpoints via ServiceDefaults.

CONSTRAINT: auth.md; api-contract.md; backend.md.

RESTRICTION: No Identity yet. No other auth scheme, no cookies on the API, no CORS (single origin).

UTILIZATION: add-endpoint (for a /api/v1/site/health-info sample route); @security-reviewer.

BEHAVIOR: Plan, wait, implement; tests: no token 401, wrong aud 401, service token on product route
403, expired 401, [AllowAnonymous] works, snapshot test fails on shape change. Report.
```

## 1.8 — Gateway: YARP, edge policy, internal token transform

```
SCOPE: Arts6311.Gateway with YARP routes from appsettings (gateway.md table; site/admin/api clusters via
service discovery "https+http://<name>"), trusted-header transform (strip Authorization, Cookie after
session read, X-Forwarded-*, internal headers), internal-token transform (sign ES256 per request with
the private key parameter; anonymous requests get no token), 404 for /api/*/internal/** from browsers,
security headers, configurable CSP, named rate-limit policies, request-size limits, correlation id,
compression. AppHost: gateway is the ONLY WithExternalHttpEndpoints(); waits for api, cache.

CONSTRAINT: gateway.md; auth.md; verify YARP + service-discovery APIs on current docs.

RESTRICTION: No business logic or data access in the gateway. No session yet (1.10). Don't use the
Aspire AddYarp container resource (ADR 0001).

UTILIZATION: add-aspire-resource; @security-reviewer.

BEHAVIOR: Plan route table + middleware order, wait, implement; tests per gateway.md; aspire run and
show the API reachable only through the gateway.
```

## 1.9 — Identity module (users, roles, internal session routes)

```
SCOPE: Modules/Identity in the Domain: ASP.NET Core Identity stores on ArtsDbContext, roles Admin /
Editor / Member, lockout, confirmed email required for staff, staff password policy (≥ 14 characters,
offline check against a bundled common/breached-password list; ADR 0018). Internal routes (InternalService policy
only): POST /api/v1/internal/sessions/credentials (password check → user summary), POST
/internal/sessions/validate (security-stamp check). Email confirmation and password reset tokens are
sent with an `email.send-requested` outbox message (add-notification) delivered via Mailpit. Seed the
first Admin in MigrationService from the seed-admin parameters.

CONSTRAINT: auth.md; add-endpoint; add-notification; add-outbox-message.

RESTRICTION: No browser-facing auth endpoints in the API. No user enumeration. No secrets in config.
Migration reviewed before apply.

UTILIZATION: add-endpoint, add-notification; @security-reviewer, @outbox-reviewer.

BEHAVIOR: Plan, wait, implement; tests: credential check, lockout, unconfirmed staff refused, short or
listed staff password refused, reset email in Mailpit exactly once on duplicate delivery. Report.
```

## 1.10 — BFF session in the gateway

```
SCOPE: Gateway /bff endpoints: login (rate limited; calls the internal credential route with a service
token), logout (delete ticket + clear cookie), session (display name + roles only), antiforgery.
Server-side ticket store in Redis; cookie __Host-6311.session (HttpOnly, Secure, SameSite=Lax);
sliding expiry; periodic security-stamp revalidation; antiforgery enforced on every unsafe request
except /webhooks/square. Coarse route authorization: /admin/** and /api/v1/cms/** need a CmsEditor
session. Staff sessions (Admin/Editor) expire at 30 min idle / 8 h absolute (config, ADR 0018).
"Log out everywhere": /bff/logout-everywhere calls an internal service route that rotates the
security stamp, then deletes this ticket; other sessions end at their next revalidation. Staff
sign-ins (success and failure) are audited.

CONSTRAINT: auth.md (creatorpantry B-02/B-13); gateway.md.

RESTRICTION: No tokens to the browser, ever. No API cookies.

UTILIZATION: @security-reviewer.

BEHAVIOR: Plan the flow (sequence diagram), wait, implement; tests: login/logout, copied cookie dead
after logout, stamp rotation ends session, log out everywhere ends a second session, staff idle and
absolute expiry, missing antiforgery 400, CMS route without session 401/302.
```

## 1.11 — ~~Staff two-factor~~ (removed)

Removed by ADR 0018: no 2FA for anyone. "Log out everywhere" moved to 1.10 and the staff password
policy to 1.9. The number is kept so later prompt numbers don't change; skip this slot.

## 1.12 — ✅ Platform checkpoint + CI

```
SCOPE: Remove the 1.5 ping route. Add an Aspire.Hosting.Testing end-to-end test: AppHost starts from
empty volumes, gateway healthy, /api/v1/internal/** 404, staff login via BFF, an outbox message
travels to a handler. Add GitHub Actions CI: dotnet build -warnaserror, tests (with containers), npm
ci/build/test in src/web, the Aspire E2E job.

CONSTRAINT: aspire.md; CLAUDE.md commands.

RESTRICTION: Fix only wiring blocking the flow; no features.

UTILIZATION: @architecture-reviewer, @outbox-reviewer, @security-reviewer, @test-gap-analyzer.

BEHAVIOR: Report a health table, CI result, and every reviewer's BLOCKERs. Ask before fixing anything
beyond BLOCKERs.
```

---

# Phase 2 — CMS core

## 2.1 — `@6311/ui` design tokens and primitives

```
SCOPE: Create projects/ui (@6311/ui) in the Angular workspace: tokens.scss and global.scss extracted
from the spike's styles.scss (no visual change to the site), primitives used by both apps: button,
icon, field, notice, status chip (glyph + word), dialog, empty state. Switch the site to import tokens
from @6311/ui.

CONSTRAINT: design-system.md; frontend.md; new-component.

RESTRICTION: Site must render pixel-identical (compare screenshots of 5 routes before/after). No new
visual direction.

UTILIZATION: new-component; playwright-cli for screenshots; @design-review.

BEHAVIOR: Plan the token map and primitive APIs, wait, implement, show before/after screenshots, report.
```

## 2.2 — Admin app shell

```
SCOPE: projects/admin (SPA, base href /admin/, zoneless, signals): SessionService over /bff/session,
role guards, XSRF interceptor (cookie __Host-6311.csrf, header X-XSRF-TOKEN), ProblemDetails toast surface, layout
(Dashboard, Classes, Exhibitions, Artists, Shop, Memberships, Pages, Media, Forms, Redirects, Sync),
unsaved-changes guard. AppHost AddJavaScriptApp("admin"); gateway /admin route.
Plus the staff sign-in page on projects/site at /signin (noindex, single step, no 2FA, posts to /bff/login,
honours ?returnUrl): /admin/** is gated, so a login screen inside the admin bundle could not load its own
scripts and would redirect to itself forever (ADR 0024).

CONSTRAINT: frontend.md; design-system.md; auth.md; ADR 0024.

RESTRICTION: No CRUD screens. No browser storage of session data. No HttpClient in components.

UTILIZATION: new-component; @design-review, @security-reviewer.

BEHAVIOR: Plan, wait, implement, npm test, walk me through /signin → dashboard through the gateway URL.
```

## 2.2a — Password reset and email confirmation: Identity routes + BFF endpoints

Owed since ADR 0023 and deferred again by ADR 0024/0027: the emailed links already point at
`/account/confirm-email#…` and `/account/reset-password#…`, and nothing answers them.

```
SCOPE: IIdentityFacade ResetPasswordAsync(userId, token, newPassword) and ConfirmEmailAsync(userId, token)
(StaffPasswordRule applied to Admin/Editor targets, breached-list check for everyone, security stamp rotated on
reset so live sessions end at revalidation, audit rows); internal routes
POST /api/v1/internal/identity/password-reset, /password-reset/complete, /email-confirmation/complete
(InternalService token only). Gateway /bff/forgot-password (bff-login-style rate limit + a per-address cooldown
that behaves identically for known and unknown addresses; always 202), /bff/reset-password,
/bff/confirm-email, each antiforgery-protected and answering one non-enumerating ProblemDetails on failure.
Decide and record the reset-request timing oracle ADR 0023 left open.

CONSTRAINT: auth.md; add-endpoint; ADR 0018, 0023, 0024.

RESTRICTION: Tokens only ever in request bodies, never in a URL, a log line, a trace attribute or an outbox
payload. No account enumeration by status, body, timing or cooldown. No Identity code in the gateway.

UTILIZATION: add-endpoint; @security-reviewer, @outbox-reviewer.

BEHAVIOR: Plan, wait, implement; tests: reset with a valid token works once and ends a second live session,
expired/reused token refused, short staff password refused, cooldown identical for unknown address, confirm
works once, reset email in Mailpit. Report.
```

## 2.2b — Password reset and email confirmation: site pages

```
SCOPE: projects/site pages /account/forgot-password, /account/reset-password, /account/confirm-email
(noindex, Cache-Control no-store), plus a "Forgot your password?" link on /signin. The reset and confirm pages
read userId and token from location.hash in the browser only (never during SSR), clear the fragment with
history.replaceState before anything else runs, and POST them to 2.2a's /bff routes through a typed service.
Success sends staff on to /signin; failure offers a fresh request.

CONSTRAINT: frontend.md; design-system.md; auth.md; ADR 0027 (sign-in page patterns, returnUrl rules).

RESTRICTION: The token never reaches the server in a URL, the SSR render, browser storage or console output.
The forgot-password answer is identical for registered and unregistered addresses. No HttpClient in components.

UTILIZATION: new-component; @design-review, @security-reviewer.

BEHAVIOR: Plan, wait, implement, npm test, walk me through forgot → Mailpit → reset → /signin → dashboard
through the gateway URL.
```

## 2.3 — Media module

```
SCOPE: Modules/Media: MediaAsset entity (media.md metadata), upload endpoint (stream, signature check,
limits, EXIF GPS strip, checksum) storing originals in the media container; `media.rendition-requested`
outbox message staged in the same save; Worker handler producing 400/800/1600 WebP+JPEG renditions
(content-addressed, idempotent); delivery GET /media/{id}/{rendition} with ETag/long cache (draft-only
assets to CMS sessions only); alt-text update; delete blocked while referenced. Admin: media library
grid, uploader with progress, alt-text editor, reusable picker.  Add the ability to compress images using native .net technologies - no external paid nugets or plugins

CONSTRAINT: media.md; add-media-feature; add-outbox-message; add-endpoint.

RESTRICTION: No public blob access. No SVG. Image library choice + license in the plan for approval.

UTILIZATION: add-media-feature, add-outbox-message, new-component; @outbox-reviewer.

BEHAVIOR: Plan, wait, implement; tests per add-media-feature + outbox atomicity; demo upload →
renditions appear.
```

## 2.4 — Artists

```
SCOPE: Modules/Artists via add-content-entity (entity, lifecycle, slug, row version, LegacyWordPressId)
and add-endpoint for /api/v1/cms/artists (list cursor, get, create, patch with If-Match, publish /
unpublish / archive commands) — admin list + edit screens (media picker for portrait).

CONSTRAINT: content.md; backend.md; api-contract.md.

RESTRICTION: Site reads come in Phase 3. Archive/delete is CmsAdmin only and audited.

UTILIZATION: add-content-entity, add-endpoint, new-component; @architecture-reviewer,
@api-contract-checker.

BEHAVIOR: Plan the slice, wait, implement bottom-up, tests per layer incl. 409 conflict, demo.
```

## 2.5 — Exhibitions

```
SCOPE: Same slice as 2.4 for Exhibitions (dates, reception, artists M:N via the Artists facade, hero).

CONSTRAINT/RESTRICTION/UTILIZATION/BEHAVIOR: as 2.4. Cross-module reads of artists go facade to facade.
```

## 2.6 — Pages + HTML sanitizer

```
SCOPE: Shared-kernel HTML sanitizer (allow-list) and Modules/Pages slice (slug, title, body). Seed
privacy/terms placeholders.

CONSTRAINT: content.md; as 2.4.

RESTRICTION: Never store script/style/on* attributes; sanitizer has its own test suite with known XSS
payloads.

UTILIZATION: add-content-entity, add-endpoint, new-component; @security-reviewer.

BEHAVIOR: as 2.4.
```

## 2.7 — Redirects

```
SCOPE: Modules/Redirects: Redirect entity (from, to, code, source Imported/Manual), CMS CRUD (CmsAdmin,
audited), public GET /api/v1/site/redirects (cached), and a Redirects facade method other modules call
when a published slug changes.

CONSTRAINT: content.md; add-endpoint.

RESTRICTION: Reject loops and chains longer than one hop at write time.

UTILIZATION: add-endpoint, new-component.

BEHAVIOR: as 2.4 plus tests for loop/chain rejection and slug-change redirect creation.
```

## 2.8 — Publishing lifecycle, revalidation, scheduling, previews

```
SCOPE: (a) Every status command stages `site.revalidate-requested {entityType, entityId, paths}` in the
same save; its Worker handler invalidates HybridCache tags in the API (via an internal facade) — no
HTTP to the site. (b) Scheduled publisher in the Worker (every minute, guarded claim, publishes through
the module facade). (c) Signed expiring preview tokens: GET /api/v1/site/preview/{token} returns the
draft ServiceModel; site renders noindex.

CONSTRAINT: content.md; outbox.md; add-background-job; add-outbox-message.

RESTRICTION: Preview tokens grant no write access and never enter the sitemap.

UTILIZATION: add-outbox-message, add-background-job; @outbox-reviewer.

BEHAVIOR: Plan, wait, implement; tests: schedule fires once across two workers, revalidation handler
idempotent, expired preview 404. Report.
```

## 2.9 — Classes (content only)

```
SCOPE: Modules/Classes: ClassOffering (starting price) + ClassSession (start, end, display zone, SKU,
capacity) via add-content-entity. SKU is CMS-owned and unique across the CMS (unique index shared with
ShopItem SKUs; suggested on create as `CLS-<offering seq>-<n>`, editable). Read-only projection columns
SeatsRemaining, SoldOut, PriceCents (populated later by SquareSync). CMS slice + admin screens (sessions
editor with SKU and capacity; before first sync the starting price is a plain field; after it, a
placeholder for the 5.4a/5.9 Change actions).

CONSTRAINT: content.md; square.md (ownership, ADR 0032).

RESTRICTION: No Square calls. No tax fields. Write models MUST NOT contain the projection fields — add a
test that proves it. Duplicate SKU → 409 with a problem code.

UTILIZATION: add-content-entity, add-endpoint, new-component; @architecture-reviewer.

BEHAVIOR: as 2.4.
```

## 2.10 — Shop items: artwork (content only)

```
SCOPE: Modules/Shop: ShopItem = artwork only (artist via Artists facade, categories, gallery, SKU,
one-of-a-kind, starting price, starting quantity — forced to 1 when one-of-a-kind) + projections
(PriceCents, StockOnHand, SoldOut). SKU suggested as `ART-<seq>`, editable, unique (shared index with
class sessions). CMS slice + admin.

CONSTRAINT/UTILIZATION/BEHAVIOR: as 2.9.

RESTRICTION: as 2.9. No merch type, category or flag — merch is POS-only (ADR 0032).
```

## 2.11 — Membership plans (content only)

```
SCOPE: Modules/Memberships: MembershipPlan (name, perks, discount %, featured, order, starting price) +
projections (PriceCents, Cadence). CMS slice + admin. Seed from the spike's MEMBERSHIPS array.

CONSTRAINT/RESTRICTION/UTILIZATION/BEHAVIOR: as 2.9.
```

## 2.12 — Forms: submissions, newsletter, contact

```
SCOPE: Modules/Forms: POST /api/v1/forms/submissions (with images into Media), /forms/newsletter
(consent timestamp), /forms/contact — anonymous, `forms` rate limit, honeypot, validation; confirmation
and staff-notification emails via `email.send-requested`. Admin: submissions inbox (New / Reviewed /
Accepted / Declined, notes), newsletter list with CSV export.

CONSTRAINT: add-endpoint; add-notification; external.md (newsletter vendor = DECIDE, export only now).

RESTRICTION: No vendor integration. No attachments in emails.

UTILIZATION: add-endpoint, add-notification, new-component; @security-reviewer, @outbox-reviewer.

BEHAVIOR: Plan, wait, implement; tests incl. honeypot, rate limit, forged image; demo emails in Mailpit.
```

## 2.13 — ✅ CMS checkpoint

```
SCOPE: Audit Phase 2. Run every reviewer; fix BLOCKERs only.

UTILIZATION: @architecture-reviewer, @outbox-reviewer, @api-contract-checker, @design-review,
@security-reviewer, @test-gap-analyzer.

BEHAVIOR: Report findings grouped by reviewer; wait for my go before fixing; re-run until clean.
```

---

# Phase 3 — Public site on the platform

## 3.1 — Site read API

```
SCOPE: /api/v1/site/* read endpoints (anonymous, published only): classes (+ upcoming runs with their
instructor, sessions and seats/sold out; ?instructor= filter, ADR 0044), instructors (ADR 0043), exhibitions,
artists (+ works), shop (filters, cursor), memberships, pages, redirects, sitemap entries. HybridCache with tags invalidated by the 2.8 revalidation handler.

CONSTRAINT: api-contract.md; add-endpoint.

RESTRICTION: Never expose drafts, staff fields or Square ids.

UTILIZATION: add-endpoint; @api-contract-checker.

BEHAVIOR: Plan route list + ServiceModels mapped to the spike's models, wait, implement, test cache
hit/miss/invalidate. Report.
```

## 3.1b — Publishing an image

```
SCOPE: media.publish-requested (ADR 0042, ADR 0030 option 1): every publish, scheduled publish included,
stages it in the same save with the image ids the record shows (hero, portrait, gallery, body images); a
keyed Media handler marks those assets Published, one way. Backfill records published before this lands.

CONSTRAINT: outbox.md; media.md; add-outbox-message.

RESTRICTION: Payload is ids only. Never turn an image back to Draft. No cross-module read at delivery.

UTILIZATION: add-outbox-message; @outbox-reviewer, @architecture-reviewer.

BEHAVIOR: Plan, wait, implement; tests incl. failed save leaves neither row nor message, idempotent handler,
malformed payload poisons, anonymous 200 for a published record's rendition and 404 for a draft's. Report.
```

## 3.2 — Replace WpApi with SiteApi

```
SCOPE: projects/site: SiteApi service + models mirroring 3.1 (string ids, cents, media URLs); SSR uses
the gateway origin injected by Aspire (server env), browser uses relative URLs; mock fallback behind a
flag; allowedHosts from config; re-enable spec schematics; tests for SiteApi and three pages. AppHost:
AddJavaScriptApp("site") with the gateway route /**.

CONSTRAINT: frontend.md (SSR keep-list).

RESTRICTION: SEO output identical where data is equal (compare <head> of 5 pages before/after). No
visual change. Buy/cart links untouched until Phase 5.

UTILIZATION: new-component; @api-contract-checker, @design-review.

BEHAVIOR: Plan, wait, implement, show the head diffs, npm test/build, aspire run.
```

## 3.3 — server.ts on the platform

```
SCOPE: Sitemap and legacy redirects read from /api/v1/site (keep the spike map as fallback); delete
Express /api/subscribe and /api/contact; newsletter and contact components post to /api/v1/forms/*;
native submission page replaces the Google Form link; logo/OG image served from /media.

CONSTRAINT: frontend.md; api-contract.md.

RESTRICTION: Every legacy URL in the spike's map still 301s. Robots unchanged.

UTILIZATION: new-component; playwright-cli to crawl redirects.

BEHAVIOR: Plan, wait, implement, run a redirect crawl report, report.
```

## 3.4 — ✅ Publish-to-page checkpoint

```
SCOPE: End-to-end test: edit + publish a class in admin → outbox revalidation → site page shows the
change within the cache window; unpublish → 404 on the site.

UTILIZATION: playwright-cli; @test-gap-analyzer.

BEHAVIOR: Report timings and any fix needed (wiring only).
```

---

# Phase 4 — WordPress import

> **⛔ Superseded — do not run 4.1 or 4.2.** Both are replaced by
> [`prompts/wordpress-import-scrub-prompts.md`](wordpress-import-scrub-prompts.md) (I.1–I.14), which reaches the same
> place — a dry-run report, then idempotent apply — in atomic steps.
>
> **Order:** run **I.1 → I.8 now** (they need only Phase 2 and 3.1 and never touch Square), then **Phase 5 through
> 5.3**, then **I.9 → I.14**. I.9, the Square catalog match, is the one join: it writes `SquareCatalogLink` rows, so it
> needs 5.1 for the table and 5.3 for the read-only catalog gateway.
>
> **Nothing imported is published until I.9 has matched it.** The match pass is what links an imported piece to the
> Square object 6311 may already sell; publishing first makes 5.3 create a duplicate (`import.md`, "Square matching").
> Everything imports as Draft, so this is a discipline across the seam, not a code gate — the damage would be a bulk
> publish between I.8 and I.9.
>
> Why these two are dead rather than merely old:
>
> - 4.1 reads `6311/v1/exhibitions`. The mu-plugin that registers those routes — **and the `exhibition` post type
>   itself** — was never installed on the live site (`src/web/projects/site/legacy/6311-headless.php`), so neither the
>   endpoint nor, probably, the content exists. I.1 is what finds out.
> - 4.1 reads FooEvents date meta and exact stock. Neither is on the public Store API; both need the read-only
>   WooCommerce key that I.2 creates.
> - 4.1 is one prompt for seven content types. `add-import-mapping` wants a mapping table and golden fixtures per
>   type, which is what I.7 is.

## 4.1 — Importer (dry run) — ⛔ superseded by I.1–I.8

```
SCOPE: Arts6311.Importer (AppHost explicit-start resource): read WordPress public endpoints (artists
from product categories, 6311/v1/exhibitions, FooEvents classes using the legacy plugin's logic,
WooCommerce Store API products, privacy/terms pages, every referenced image). Map to CMS facades (never
DbContext). --dry-run writes docs/runbooks/import-report-<date>.md: counts, skips with reasons, missing
alt text, slug collisions, redirect plan.

CONSTRAINT: content.md (Imported = Draft, LegacyWordPressId, never overwrite staff edits);
external.md.

RESTRICTION: Read-only against WordPress. No prices or stock imported. No --apply in this prompt.

UTILIZATION: add-external-integration; Explore over projects/site/legacy.

BEHAVIOR: Plan the field mapping table, wait, implement, run --dry-run, show the report.
```

## 4.2 — Importer (apply) — ⛔ superseded by I.7, I.8 and I.13

```
SCOPE: --apply: idempotent upserts on LegacyWordPressId through facades; images via the Media upload
path (renditions via outbox); Redirect rows (source Imported) for every old URL. Re-run is a no-op.

CONSTRAINT/RESTRICTION: as 4.1; apply only after I approve the dry-run report.

UTILIZATION: @outbox-reviewer.

BEHAVIOR: Run --apply locally, re-run to prove idempotency, spot-check 10 pages on the local site.
```

---

# Phase 5 — Square two-way integration

**Order against the import series:** 5.1 and 5.3 must land before I.9, and nothing imported may be published until
I.9 has run — see the note under Phase 4. 5.3 onwards is worth running against imported content rather than the
development seed: real SKUs (or none), entity-encoded names, full galleries, a `price: 0` class and missing alt text
are the cases a catalog push actually breaks on.

Field ownership is fixed (`square.md`, ADR 0032): the CMS creates artwork and classes, owns their SKU,
and changes their price and quantity only through commands; Square holds the record; merch and any
object without a CMS id are POS-only and ignored. Every prompt passes against `Arts6311.SquareFake`
before sandbox.

## 5.1 — SquareSync integration records

```
SCOPE: Modules/SquareSync: SquareCatalogLink (entityType, entityId, squareObjectId, variationIds,
squareVersion, contentHash, syncStatus Synced/Pending/NeedsAttention/NeedsReview, lastErrorCode,
lastPushedAt, lastPulledAt), SquareChangeRequest (kind Price/Quantity/Capacity, entityType, entityId,
variation, requestedValue, expectedValue, occurredAt, status Pending/Applied/Rejected/NeedsAttention,
reason code, outboxMessageId, requestedBy), SquareSyncCursor, SquareWebhookEvent (unique EventId,
Type, ObjectIds, ReceivedAt, ProcessedAt). Facade for link lookup and projection updates (the ONLY
writer of Square-owned projection columns on content entities, via each module's facade).

CONSTRAINT: square.md; backend.md.

RESTRICTION: No Square ids on content entities. Migration reviewed before apply.

UTILIZATION: add-endpoint (read-only status endpoint for admin later); @integration-reviewer.

BEHAVIOR: Plan, wait, implement, migration review, tests. Report.
```

## 5.2 — Square gateways (anti-corruption layer)

```
SCOPE: Domain interfaces ISquareCatalogGateway, ISquareInventoryGateway, ISquareOrdersGateway,
ISquarePaymentsGateway, ISquareCustomersGateway, ISquareSubscriptionsGateway, ISquareWebhookVerifier
with Domain-owned request/result records; Arts6311.Square implements them with the official SDK
(base URL and `Square:LocationId` from config), one money mapper, inventory supports PHYSICAL_COUNT
(with occurred_at) and ADJUSTMENT at the configured location, variations carry `sku`, no tax ids, every write requires an idempotency key parameter. A shared
contract test suite (abstract base) runs against any implementation.

CONSTRAINT: square.md; external.md. Verify SDK package, client construction, base-URL override,
SearchCatalogObjects/BatchUpsert/Inventory/Orders/Payments/Customers/Cards/Subscriptions shapes and the
signature helper on developer.squareup.com; cite pages in comments.

RESTRICTION: Nothing outside Arts6311.Square references the SDK (add an architecture test).

UTILIZATION: add-external-integration; @integration-reviewer.

BEHAVIOR: Plan the interface list, wait, implement. Report.
```

## 5.3 — Square fake + mode wiring

```
SCOPE: Arts6311.SquareFake: minimal API implementing the HTTP endpoints the SDK calls for the 5.2
subset (in-memory, realistic ids/versions, idempotency-key replay, version conflicts, one location,
inventory computed as latest PHYSICAL_COUNT + later adjustments by occurred_at, a way to seed POS-only
items with no CMS id, injectable faults: timeout-after-send, 429), plus POST /_fake/webhooks/{type} that sends a correctly signed webhook to a
configured URL, and a stub Web Payments tokenizer script. AppHost: in fake mode api/worker reference
squarefake; in sandbox mode they get Square's sandbox base URL + secret parameters. Run the 5.2
contract suite against the fake (always) and sandbox (env-flag opt-in).

CONSTRAINT: square.md; aspire.md.

RESTRICTION: Fake exists only in run mode. No production base URL anywhere in run mode (test it).

UTILIZATION: add-aspire-resource; @integration-reviewer.

BEHAVIOR: Plan the endpoint list, wait, implement, run the suite on the fake (and sandbox if I set the
flag). Report.
```

## 5.4 — Outbound push: artwork (shop items)

```
SCOPE: ShopItem publish/update stages `square.catalog-push-requested {ShopItem, id}`; handler per
add-square-sync §3 (reload, hash skip, create with starting price + starting quantity at the configured
location, SKU on the variation, version-aware merge that keeps Square's current price, images first,
idempotency key = message id, link update, NeedsAttention on last attempt). Once linked, the starting
fields are frozen (409 on edit).

CONSTRAINT: add-square-sync; add-outbox-message; square.md ownership table (in the plan).

RESTRICTION: The content push never sends price or stock after creation (5.4a/5.4b do that). No tax
ids. Never block the CMS save on Square.

UTILIZATION: add-square-sync, add-outbox-message; @integration-reviewer, @outbox-reviewer.

BEHAVIOR: Ownership table first, wait, implement; tests: create (price, quantity, SKU in Square),
content update, SKU change, image change, no-op skip, version conflict, timeout-after-send
reconciliation, duplicate delivery. Demo against the fake.
```

## 5.4a — Change price command (artwork + class runs)

```
SCOPE: POST /api/v1/cms/shop/{id}/price-changes and
POST /api/v1/cms/classes/{id}/runs/{rid}/price-changes {newPriceCents, expectedPriceCents}
(CmsEditor, Idempotency-Key, audited): only for linked items; writes a SquareChangeRequest (Price,
Pending) and stages `square.price-change-requested {requestId}` in one save. Handler: retrieve the
variation; current price ≠ expected → Rejected (reason price_changed_in_square); else full-object
upsert with the last version (re-read once on conflict); timeout → read back. The inbound pull (5.7)
marks it Applied when Square shows the value. Admin: price shows Square's figure + Change dialog +
request state; for a class, "apply to all upcoming runs" sends one request per run (ADR 0044).

CONSTRAINT: ADR 0032; square.md; add-endpoint; add-outbox-message; add-square-sync §5. Verify catalog
upsert/version behaviour on developer.squareup.com and cite it.

RESTRICTION: Never write PriceCents from this path. Never overwrite a price that differs from expected.
Validate the amount (positive, Square's minimum).

UTILIZATION: add-endpoint, add-outbox-message, new-component; @integration-reviewer, @outbox-reviewer,
@security-reviewer.

BEHAVIOR: Plan, wait, implement; tests: applied, stale expected → Rejected, replayed Idempotency-Key,
duplicate delivery, timeout-after-send, unlinked item → 409, wrong role → 403. Demo on the fake.
```

## 5.4b — Set quantity command (artwork)

```
SCOPE: POST /api/v1/cms/shop/{id}/quantity {quantity} (CmsEditor, Idempotency-Key, audited): only for
linked, non-one-of-a-kind items; SquareChangeRequest (Quantity, occurredAt = now) + stage
`square.inventory-change-requested {requestId}` in one save. Handler: BatchChangeInventory
PHYSICAL_COUNT at `Square:LocationId` with occurred_at = request.occurredAt, idempotency key = message
id. Admin: quantity shows Square's figure + Set dialog + request state.

CONSTRAINT: ADR 0032; square.md; add-endpoint; add-outbox-message. Verify the PHYSICAL_COUNT /
occurred_at semantics on developer.squareup.com (inventory-api/how-it-works) and cite it.

RESTRICTION: Never write StockOnHand from this path. No quantity command for one-of-a-kind (409).

UTILIZATION: as 5.4a.

BEHAVIOR: Plan, wait, implement; tests: set count applied, a fake sale dated after the request still
subtracts, replay, duplicate delivery, one-of-a-kind refused. Demo on the fake.
```

## 5.5 — Outbound archive and delete

```
SCOPE: Two CMS acts reach Square through one message, `square.catalog-delete-requested {entityType,
entityId, intent}`:
  - **Archive** → the ADR'd archive policy (delete vs. mark unavailable: write ADR 0011 with the
    trade-offs first).
  - **Delete** (ADR 0052) → the Square object goes. The DataLayer's DeleteAsync stages it in the same
    save as the soft delete and the SKU release, for ShopItem and ClassOffering (a run's variations with
    the item) — the admin's second confirm dialog already tells staff this will happen
    (`shared/delete-confirm/delete-confirm.ts`).
Handler: reload through the facade past the query filter (the record is deleted by the time it runs —
add a `FindDeletedAsync`-backed facade read for the handler, or carry what it needs on
`SquareCatalogLink`), delete/archive in Square under the message id as the idempotency key, then clear
the link row. Already gone in Square counts as success. On the last attempt record NeedsAttention.

CONSTRAINT/UTILIZATION: as 5.4; outbox.md (handlers reload current state, at-least-once, poison on a
malformed payload); ADR 0052 for what a delete means on this side.

RESTRICTION: Never hard-delete CMS content because of Square. A delete is staged by the CMS delete only,
never by the inbound pull. Square-owned projections are untouched.

BEHAVIOR: ADR draft (0011, plus the amendment ADR 0052 asks for), wait, implement, tests on the fake —
archive, delete, a replay that finds the object already gone, a delete whose link row was never created —
then **delete the "Until the Square sync ships" sentence from `delete-confirm.ts`** and its spec
assertion, because the dialog's promise is true from here on.
```

## 5.6 — Webhook intake

```
SCOPE: POST /webhooks/square in the API (anonymous, `webhooks` rate limit, 256 KB): raw body, verify
signature over the configured public notification URL + body (constant time), allow-list types, insert
SquareWebhookEvent (unique EventId; duplicate = 200 no-op), stage `square.webhook-received {inboxId}`,
single save, 200. Metrics for accepted / duplicate / ignored / bad-signature.

CONSTRAINT: add-webhook-handler §2; outbox.md; gateway.md (route exempt from antiforgery only).

RESTRICTION: No Square API calls in the request. Bad signature stores nothing.

UTILIZATION: add-webhook-handler; @security-reviewer, @outbox-reviewer.

BEHAVIOR: Plan, wait, implement; tests via the fake's signed trigger incl. bad signature and
duplicate. Report.
```

## 5.7 — Inbound catalog handler

```
SCOPE: `square.webhook-received` dispatcher + catalog.version.updated branch: SearchCatalogObjects
since the cursor (ITEM, ITEM_VARIATION, IMAGE, CATEGORY), apply Square-owned projections through the
SquareSync facade, echo suppression (version/hash), objects without a CMS id skipped (POS-only),
linked object deleted in Square → Archived + NeedsReview, Pending price requests whose value now shows
→ Applied, stage `site.revalidate-requested`, advance cursor after
commit, mark inbox processed.

CONSTRAINT: add-webhook-handler §3; add-square-sync §4.

RESTRICTION: Never overwrite CMS-owned fields. Never trust payload state.

UTILIZATION: add-webhook-handler; @integration-reviewer, @outbox-reviewer.

BEHAVIOR: Plan, wait, implement; tests: CMS price change (5.4a) reaches the site, echo ignored,
out-of-order, redelivery after crash, POS-only item ignored, deleted in Square. Demo.
```

## 5.8 — Inbound inventory handler

```
SCOPE: inventory.count.updated branch: batch-retrieve counts for named variations of linked items
only, update StockOnHand / SeatsRemaining / SoldOut, mark matching Pending quantity/capacity requests
Applied, stage revalidation.

CONSTRAINT/RESTRICTION/UTILIZATION: as 5.7.

BEHAVIOR: tests: stock → 0 shows "Sold out" on the site; stale event can't resurrect stock. Demo.
```

## 5.9 — Classes sync + seat adjustment

```
SCOPE: add-square-sync for ClassOffering (ITEM) / ClassRun (ITEM_VARIATION with SKU, starting price,
inventory = capacity at create; ADR 0044 — sessions are only times). After linking, editing a run's capacity
in the class editor (the normal run save: CmsEditor, row version, audited) writes a SquareChangeRequest (Capacity) and
stages `square.inventory-change-requested` as an ADJUSTMENT of newCapacity − oldCapacity in the same
save. A reduction larger than SeatsRemaining → 409; the handler re-reads the count and sets
NeedsAttention rather than go negative. Runs added later are created as new variations by the push.

CONSTRAINT: ADR 0032; add-square-sync; add-endpoint; add-outbox-message. Verify the ADJUSTMENT
from/to states for adding and removing seats on developer.squareup.com and cite them.

RESTRICTION: Never recompute seats locally from orders. Never write SeatsRemaining from this path.

UTILIZATION: as 5.4.

BEHAVIOR: tests: create with 3 runs (SKUs, price, seats in the fake), sell out in the fake, raise
capacity (seats reappear), lower below seats remaining refused, duplicate delivery replay-safe. Demo.
```

## 5.10 — Membership plans sync

```
SCOPE: add-square-sync for MembershipPlan → SUBSCRIPTION_PLAN + SUBSCRIPTION_PLAN_VARIATION (annual,
starting price once); price/cadence pulled back. Record Square limits (no ACH, $1 minimum) in ADR 0012.

CONSTRAINT: add-square-sync; verify plan/variation/phase shapes on Square docs.

Also settle what ADR 0052 left open: **may a plan Square has subscribers for be deleted at all?** Decide
in ADR 0012 — refuse the delete with `cms.in_use` while a subscription references it (the Members module
answers through an `IContentReferenceSource`, as Exhibitions answers for artists), or allow it and leave
billing alone. Whichever it is, a plan's delete must not strand a live subscription. Until this lands,
deleting a plan is a local act and the dialog says so.

RESTRICTION: No subscriptions created here. Never cancel a subscription as a side effect of a CMS delete.

UTILIZATION: as 5.4.

BEHAVIOR: Plan, wait, implement, tests on the fake, demo the site memberships page from synced plans.
```

## 5.11 — Admin sync experience + operations screen

```
SCOPE: Admin: sync chip + last error code + Retry (stages a new push) on synced edit screens; Square
values with their Change actions and the latest change request's state (Pending/Applied/Rejected +
reason). Sync screen (CmsAdmin): outbox counts by status/type, Poisoned list with Replay
(reset to Pending, attempts kept, audited) and Discard (audited, reason required), webhook inbox recent
events, catalog cursor, NeedsAttention / NeedsReview queues, Pending/NeedsAttention change requests.
A deleted record whose Square delete never landed (5.5) shows here too, named and restorable, because
nothing else in the CMS lists it any more (ADR 0052).

CONSTRAINT: outbox.md (documented recovery path); add-endpoint; new-component.

RESTRICTION: Replay/discard are Admin-only and audited. No payload contents shown beyond ids.

UTILIZATION: add-endpoint, new-component; @security-reviewer, @design-review.

BEHAVIOR: Plan, wait, implement, demo a poisoned message replayed after fixing the fault in the fake.
```

## 5.12 — Reconciler

```
SCOPE: Worker job (add-background-job): every 15 min compare links changed since last pass; nightly
full compare. Fix drift on Square-owned projections; re-stage pushes for CMS changes never acked;
resolve stale Pending change requests by reading Square (Applied or NeedsAttention); skip objects with
no CMS id; audit each correction; Operation record for on-demand full runs (202).

CONSTRAINT: square.md (Reconciliation); add-background-job.

RESTRICTION: Never deletes CMS content; never writes CMS-owned fields to Square except by staging an
outbox message.

UTILIZATION: add-background-job, add-outbox-message; @integration-reviewer, @outbox-reviewer.

BEHAVIOR: Plan, wait, implement; tests that inject drift in the fake and assert correction + audit.
```

## 5.13 — Cart + quote

```
SCOPE: Modules/Commerce: server-side cart keyed by an HttpOnly cart cookie set via the gateway (no
browser storage), add/remove/update lines by catalog variation id; POST /api/v1/checkout/quote
calculates via Square Orders (catalog_object_ids only). Site: cart drawer + cart page.

CONSTRAINT: square.md (Payments); add-endpoint; new-component.

RESTRICTION: Prices only from Square; client never supplies amounts.

UTILIZATION: add-endpoint, new-component; @integration-reviewer.

BEHAVIOR: Plan, wait, implement; tests: tampered price ignored, sold-out line flagged. Demo.
```

## 5.14 — Checkout pay (Web Payments SDK)

```
SCOPE: Site checkout page loads the Web Payments SDK in the browser only (host from config; fake
tokenizer in fake mode), card + Apple Pay + Google Pay, buyer verification. POST /api/v1/checkout/pay
(Idempotency-Key required): create order + payment through gateways with idempotency keys, record the
order link, stage `email.send-requested` (confirmation) and revalidation. Gateway CSP allows Square
origins only on checkout routes.

CONSTRAINT: square.md; auth.md; gateway.md; add-endpoint; add-notification.

RESTRICTION: Source token used once, never logged or stored; no amounts from the client; no refunds UI.

UTILIZATION: add-endpoint, add-notification, new-component; @security-reviewer, @integration-reviewer.

BEHAVIOR: Sequence diagram first, wait, implement; tests: happy path, decline, double submit (replay),
sold out between quote and pay, tampered amount. Demo on the fake; sandbox opt-in.
```

## 5.15 — Class rosters

```
SCOPE: Admin roster per class run: on-demand read of Square orders containing that variation via
ISquareOrdersGateway (cached briefly), CSV export.

CONSTRAINT: square.md.

RESTRICTION: No local order store; Square remains the record.

UTILIZATION: add-endpoint, new-component.

BEHAVIOR: Plan, wait, implement, demo.
```

## 5.16 — Member accounts

```
SCOPE: Public-site member registration/login through the BFF (Member role, email confirmation via
outbox), member area shell. Modules/Members links user ↔ Square customer id (integration record).

CONSTRAINT: auth.md; add-endpoint; add-notification.

RESTRICTION: Members not admitted to /admin. No enumeration.

UTILIZATION: add-endpoint, new-component; @security-reviewer.

BEHAVIOR: Plan, wait, implement, tests, demo.
```

## 5.17 — Membership signup

```
SCOPE: POST /api/v1/members/signup (Member, Idempotency-Key): find/create Square customer, store card
on file from the source token, create subscription for the plan variation, link ids; member status
stays Pending until Square confirms.

CONSTRAINT: square.md (Payments, members); verify Customers/Cards/Subscriptions request shapes.

RESTRICTION: Status never set from assumptions; no card data stored.

UTILIZATION: add-endpoint; @integration-reviewer, @security-reviewer.

BEHAVIOR: Plan, wait, implement, tests incl. replay and failure mid-way (customer created, card failed).
```

## 5.18 — Subscription webhooks + member area

```
SCOPE: Webhook branches: subscription.created/updated → member status (Active, Paused, Canceled,
Delinquent) by retrieving the subscription; invoice.payment_made / payment.updated FAILED → renewal
failure flag + `email.send-requested`. Member area: status, renewal date, update card, cancel at
period end (idempotent commands).

CONSTRAINT: add-webhook-handler; add-notification.

RESTRICTION: Status only from Square reads.

UTILIZATION: add-webhook-handler, add-endpoint, new-component; @integration-reviewer, @outbox-reviewer.

BEHAVIOR: tests: failed renewal, cancel, out-of-order subscription events. Demo on the fake.
```

## 5.19 — Member class discount

```
SCOPE: Apply the plan's class discount at quote/pay time. ADR 0013 compares Square-side options
(discount/pricing rules) vs. order-level discount line; implement the approved one.

CONSTRAINT: square.md (prices from Square).

RESTRICTION: Discount eligibility from member status only.

UTILIZATION: add-endpoint; @integration-reviewer.

BEHAVIOR: ADR first, wait, implement, tests.
```

## 5.20 — ✅ Square sandbox end-to-end

```
SCOPE: Sandbox mode with an Aspire dev tunnel exposing the gateway's /webhooks/square; runbook
docs/runbooks/square-sandbox.md (sandbox app, webhook subscription URL = tunnel URL, user-secrets).
Run the full flow: create artwork with SKU, price and quantity in the CMS → publish → appears in Square
with that SKU/price/stock → change price and set quantity in the CMS → Square and site update → class
with runs (ADR 0044): raise a run's capacity in the CMS → seats in Square → buy with a sandbox card → stock drops →
POS-only item created in the Square dashboard is ignored → membership signup → subscription webhook.
The admin never opens the Square dashboard for website items.

CONSTRAINT: aspire.md (verify dev tunnel integration); square.md.

RESTRICTION: Sandbox only.

UTILIZATION: playwright-cli; all reviewers; @test-gap-analyzer.

BEHAVIOR: Report each step pass/fail with traces, every reviewer's BLOCKERs, and the gap list.
```

---

# Phase 6 — Azure

## 6.1 — Publish model

```
SCOPE: AddAzureContainerAppEnvironment("aca"); publish mappings per aspireify/references/apphost-wiring
(Azure SQL, Storage, Key Vault references for every secret parameter, ACS email, Application Insights,
managed identity per app; gateway the only external ingress; squarefake + devtunnel excluded;
migrations as a run-once job). Resolve ADR 0009 (Redis in Azure: managed vs container app — costs) with
me. Dev = scale-to-zero except gateway; prod = min 1 for gateway/api/site/worker.
Email per environment (ADR 0047): forms-staff-email is the centre's inbox in production only; every other
environment keeps a test inbox, and Azure Communication Services outside production is restricted (or sends
to a sink) so a visitor's form confirmation never reaches a real stranger from dev.
Site in publish mode: SITE_API_ORIGIN = the gateway's internal endpoint; site-allowed-hosts = the Container Apps
domain + 6311arts.com + www (ADR 0046).
Gateway rate limiting for SSR (ADR 0046, open): the gateway partitions by client IP, so every server-side read
from the site container shares one `api` budget (300/min) and the global 600/min — a crawl or a busy evening
429s the whole site. Give the site's own reads their own partition, keyed on a trusted identity rather than an
address or a header a browser can send: the site presents a credential the gateway verifies (a generated secret
parameter / Key Vault reference, or the site's managed identity), only on GET /api/v1/site/**, with its own
generous limit; everything else (and anything presenting no or a bad credential) keeps the per-IP limits.
Forwarded headers from ACA ingress are trusted only from the ingress, so real visitors keep per-IP partitions.

CONSTRAINT: aspire.md; gateway.md; auth.md; aspire-deployment skill; verify publish APIs on aspire.dev.

RESTRICTION: No `aspire deploy` (denied). Run mode unchanged — prove aspire run still needs no Azure. The SSR
credential is never sent to or readable by a browser, never logged, and never widens anything beyond the site
read routes' rate limit (no auth bypass, no internal routes).

UTILIZATION: aspire-deployment; @security-reviewer.

BEHAVIOR: Plan, wait, implement, `aspire publish` to a temp folder, summarize generated resources,
ingress, identities and secret references. Tests: SSR reads with the credential land in the site partition;
a browser request with a forged or missing credential stays per-IP and gets 429 at the old limit; a spoofed
X-Forwarded-For from outside the ingress doesn't change a partition.
```

## 6.2 — Dev environment via CI

```
SCOPE: GitHub Actions: build + test, then deploy dev using GitHub OIDC (no stored cloud secrets);
smoke tests (gateway health, site home, admin login page, unsigned webhook 401, /internal 404).
docs/runbooks/azure-dev.md. Register the dev gateway URL as the Square sandbox webhook URL (manual).

CONSTRAINT: aspire-deployment skill.

RESTRICTION: Show every Azure-modifying command for approval; the workflow identity is scoped to the
dev resource group only; I run the first deploy.

UTILIZATION: @security-reviewer on the workflow.

BEHAVIOR: Plan, wait, write workflow + runbook, then run smoke tests after my deploy.
```

## 6.3 — ✅ Production hardening + cutover

```
SCOPE: Prod via the same workflow with a manual approval gate; custom domains + managed certificates
on the gateway; Azure SQL PITR confirmed; alerts (outbox Poisoned > 0, outbox age > 5 min, webhook
signature failures, 5xx rate); Square production app + webhook subscription; final import --apply;
redirect crawl of the old site; Lighthouse mobile on key pages. docs/runbooks/cutover.md: freeze,
import, Square catalog verification, DNS switch, rollback (DNS back to WordPress), post-cutover checks.

CONSTRAINT: everything above; Square production credentials only in prod Key Vault.

RESTRICTION: Do NOT switch DNS or enable Square production without my explicit go in-session. No
WordPress data deletion.

UTILIZATION: all reviewers; playwright-cli.

BEHAVIOR: Checklist with owners (me vs. Claude); execute Claude-owned items; stop at every
irreversible step.
```

---

# Part B — Operational templates

## Template A — Feature slice

```
SCOPE: Deliver <feature>: <API change>, <admin change>, <site change>.
CONSTRAINT: .claude/rules/; match existing patterns.
RESTRICTION: No unrelated changes or contract changes; no new dependencies without asking; post-commit
effects via outbox only; no Square-owned writes.
UTILIZATION: add-endpoint / new-component (+ add-outbox-message); @architecture-reviewer,
@api-contract-checker.
BEHAVIOR: Plan the slice, wait, implement bottom-up, tests green, reviewers, summarize.
```

## Template B — Migration

```
SCOPE: Schema/data change: <describe>.
CONSTRAINT: backend.md; migration in Arts6311.Domain; applied by MigrationService.
RESTRICTION: Created, not applied, until approved; no drops/renames without a reversible plan; never
alter OutboxMessages or SquareSync tables without @outbox-reviewer / @integration-reviewer.
UTILIZATION: plan mode.
BEHAVIOR: Show model change, migration file, rollback story; wait; apply; tests.
```

## Template C — Refactor

```
SCOPE: Refactor <target> to <goal>; no behavior change; list every file and why.
CONSTRAINT/RESTRICTION: contracts and tests stable; ModuleBoundaryTests stay green; no scope creep.
UTILIZATION: Explore subagent; @architecture-reviewer.
BEHAVIOR: Impact map, wait, test-green steps, re-plan if the blast radius grows.
```

## Template D — Debug / harden

```
SCOPE: Fix <symptom> + regression test (or harden <area>).
RESTRICTION: Minimal root-cause fix; no new dependencies.
UTILIZATION: Explore; @test-gap-analyzer; @security-reviewer for harden.
BEHAVIOR: Reproduce, state hypothesis with evidence, wait, fix, test, summarize.
```

## Template E — New outbox message type

```
SCOPE: After <commit point>, do <effect> via message `<area.event>`.
CONSTRAINT: outbox.md; add-outbox-message.
RESTRICTION: Ids-only payload; staged in the DataLayer before the single save; handler reloads via a
facade, idempotent, sanitized errors, visible last-attempt state.
UTILIZATION: add-outbox-message; @outbox-reviewer.
BEHAVIOR: Show event record + staging point + handler outline; wait; implement; the seven required
tests; summarize.
```

## Template F — New Square-synced type or webhook event

```
SCOPE: Sync <content type> with <Square type> / handle <event>.
CONSTRAINT: add-square-sync / add-webhook-handler; square.md.
RESTRICTION: Ownership table approved first; no Square-owned writes from CMS; idempotency key = message
id; never trust payloads.
UTILIZATION: @integration-reviewer, @outbox-reviewer.
BEHAVIOR: Ownership table / event doc link first; wait; implement; fake-based tests; summarize.
```

## Template G — Poisoned message / sync incident

```
SCOPE: Investigate <symptom> (e.g. Poisoned `square.catalog-push-requested`, site price ≠ Square).
CONSTRAINT: square.md; outbox.md; read-only until diagnosis is agreed.
RESTRICTION: Do NOT replay, discard, reconcile or edit data until I approve; nothing in Square
production.
UTILIZATION: dashboard / App Insights traces by CorrelationId, Sync screen, Explore.
BEHAVIOR: Timeline (CMS save → outbox row → attempts → Square → webhook → inbox → handler), root cause
with evidence, proposed fix + data repair + replay plan; wait; apply; regression test.
```

## Template H — Checkpoint audit

```
SCOPE: Audit <phase or area> before moving on.
UTILIZATION: @architecture-reviewer, @outbox-reviewer, @integration-reviewer, @security-reviewer,
@api-contract-checker, @design-review, @test-gap-analyzer, @skills-evals.
BEHAVIOR: Findings by reviewer and severity; wait; fix BLOCKERs; re-run until clean; record toolkit
edits separately.
```

---

## Pro tips

- **The outbox is the backbone.** If you're about to call Square, send email or touch blobs after `SaveChangesAsync`, stop. That's a message.
- **Approve ownership tables like contracts.** Phase 5 mistakes are data mistakes, not code mistakes.
- **The fake first, always.** Sandbox needs internet and a tunnel; the fake makes every test deterministic.
- **One origin is load-bearing.** Browse only through the gateway, or cookies, CSRF, CSP and webhooks all lie to you.
- **Promote repeats to skills.** If you fill in Template E or F twice the same way, fold that shape into the skill.
