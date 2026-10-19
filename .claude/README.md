# 6311arts Claude Toolkit

This folder turns the root `CLAUDE.md` into path-scoped rules, step-by-step skills, read-only reviewers and lightweight enforcement hooks.

It's adapted from `architect4hire/creatorpantry`:

- **Kept as-is:**
  - the layered seam
  - the module-first Domain
  - the transactional outbox
  - the idempotency kernel
  - the BFF and gateway-signed internal token
  - the generic Aspire and Playwright skills
- **Dropped:** tenancy, AI and recipe material.
- **Added for this app:** Square, CMS content, the single-origin gateway, and Azure Container Apps.

## Precedence

1. The explicit user instruction for the current task
2. The root `CLAUDE.md`
3. The most specific rule under `.claude/rules/`
4. The matching skill under `.claude/skills/`
5. Existing repository conventions and nearby code

When instructions conflict, stop and surface the conflict. Never silently choose the weaker:

- security rule
- field-ownership rule
- outbox rule
- payment rule

## Rules (auto-load by path)

| File | Purpose |
|---|---|
| `backend.md` | The seam, module ownership, layer duties, transactions, per-layer testing, defects |
| `outbox.md` | Transactional outbox: staging, dispatch, leases, handlers, poison, inbound webhooks |
| `square.md` | Field ownership, `ISquare*Gateway` isolation, idempotency, webhooks, payments, reconciliation |
| `auth.md` | Identity, roles, the BFF session, the gateway → API internal token, CSRF, sensitive operations |
| `gateway.md` | YARP route table, header stripping, rate limits, CSP (Square SDK), security headers |
| `api-contract.md` | `/api/v1` route families, shapes, errors, ETags, idempotency keys, OpenAPI snapshot |
| `aspire.md` | Resource graph, dependency order, generated secrets, run mode vs publish mode |
| `content.md` | CMS content types, lifecycle, slugs, previews, sanitizing, the importer |
| `media.md` | Blob storage, renditions via the outbox, delivery, alt text, Square images |
| `external.md` | Non-Square provider adapters, secrets, webhooks, imports |
| `frontend.md` | The Angular workspace (`site` SSR, `admin`, `ui`), HTTP, SSR keep-list, admin states |
| `import.md` | WordPress import pipeline: extract → snapshot → stage → load via facades → verify; Square matching; personal data |
| `design-system.md` | `@6311/ui` tokens and primitives, visual direction, template license |

## Skills

Use one primary skill per seam. Load supporting skills only where the primary one says so.

| Skill | Use it for |
|---|---|
| `add-endpoint` | Any HTTP route, through the full seam |
| `add-content-entity` | A CMS content type and its lifecycle |
| `add-outbox-message` | Any work that must happen after a commit |
| `add-square-sync` | Making a content type sync with a Square catalog type |
| `add-webhook-handler` | A new Square webhook event |
| `add-background-job` | Scheduled or staff-triggered long work in the Worker |
| `add-external-integration` | Non-Square providers (email, newsletter vendor, import source) |
| `add-media-feature` | Uploads, renditions, delivery, alt text |
| `add-import-mapping` | Mapping one WordPress/WooCommerce source type into a CMS type |
| `add-notification` | Transactional email through the outbox |
| `new-component` | Angular components in `site`, `admin` or `@6311/ui` |
| `add-aspire-resource`, `aspireify` | AppHost wiring |
| `aspire`, `aspire-init`, `aspire-orchestration`, `aspire-monitoring` | Generic Aspire CLI skills, unchanged from creatorpantry |
| `aspire-deployment` | Generic Aspire deploy skill, **restricted**: see its 6311arts override (plan + `aspire publish` only) |
| `playwright-cli` | Browser verification, unchanged from creatorpantry |

## Review agents (read-only)

| Agent | What it reviews |
|---|---|
| `@architecture-reviewer` | Seam and module boundaries |
| `@outbox-reviewer` | Staging, payloads, idempotent handlers, poison, tests |
| `@integration-reviewer` | Square and other providers |
| `@security-reviewer` | BFF, internal token, CSRF, CSP, payments, secrets |
| `@api-contract-checker` | Versioning, shapes, OpenAPI snapshot, Angular model drift |
| `@design-review` | `@6311/ui`, SSR safety, accessibility |
| `@test-gap-analyzer` | Behaviour and risk without tests |
| `@skills-evals` | Whether this toolkit is still accurate |

## Hooks

- **`secret-guard.sh` (PreToolUse, fails closed).** Best-effort pattern match on Write/Edit/MultiEdit content. It does not see Bash-tool writes. Blocks:
  - PEM private keys (RSA, EC, OPENSSH, ENCRYPTED, PKCS#8)
  - connection-string `Password=` / `Pwd=` values and Azure account keys / SAS
  - quoted `password` / `pwd` / `api_key` / `client_secret` / `signature_key` values, including JSON and C# forms
  - Square access tokens and application secrets, WooCommerce keys, GitHub tokens, literal bearer tokens
- **`format.sh` (PostToolUse).** Runs `dotnet format` on C# files and the workspace-pinned prettier on Angular files. It never blocks an edit.

Hooks are a backstop, not the architecture.

## Settings

`settings.json` pre-allows the canonical build, test and migration commands. Rules are evaluated deny → ask → allow, and each Bash rule has a `PowerShell(...)` twin because Bash rules don't cover the PowerShell tool.

- **Denies** force-push, hard reset, database drop, the deploy/teardown commands (`aspire deploy|destroy|do`, `azd up|deploy|provision|down`, `az deployment|containerapp|webapp deploy|group delete`, `swa deploy`), and reading `.env`, `secrets.json` and the user-secrets store.
- **Asks** before `git push` (a push to `main` deploys the temporary Static Web Apps review copy), `gh pr merge`, `gh workflow run` and `dotnet ef database update`.
- Rules match command text, so they are a backstop: `git -C . push` or a deploy hidden in a script isn't caught. Deploys are run by a person, or by CI.

Hook commands use `"${CLAUDE_PROJECT_DIR}"` so they resolve from any working directory, and run under Git Bash on Windows. The secret guard sets `"onFailure": "block"` (Claude Code ≥ 2.1.295), so a guard that can't run blocks the write instead of letting it through.

## Workflow

1. Read `CLAUDE.md` and the nearest rule.
2. Pick the matching skill.
3. Inspect existing code and tests.
4. State your assumptions and plan the seam. Wait for approval.
5. Implement the smallest complete vertical slice.
6. Run focused tests, then the full suite.
7. Run the relevant reviewer agents.
8. Report:
   - the files changed
   - the behaviour added
   - the outbox messages added
   - the verification you ran
   - open decisions (`DECIDE`)
