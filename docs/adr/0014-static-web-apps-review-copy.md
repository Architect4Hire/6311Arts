# 0014 — Temporary Static Web Apps review copy of the site

- **Status:** Accepted, **temporary** (remove when the Azure dev environment from prompt 6.2 is live)
- **Date:** 2026-10-19
- **Rules affected:** `.claude/settings.json` (`git push` is an ask rule), `.claude/README.md` (Settings)
- **Related:** ADR 0001 (single public origin), ADR 0008 (Container Apps)

## Context

The client needs to review the site's look and content now, long before the platform runs in Azure. `.github/workflows/azure-static-web-apps-kind-pond-059a85510.yml` builds `src/web` with `npm run build:swa` and publishes the **client-rendered shell** (`dist/site/browser`, `index.csr.html` copied to `index.html`) to Azure Static Web Apps. It runs on every push to `main` and creates preview environments for PRs. Static Web Apps can't run the SSR server, and this copy still reads WordPress.

## Decision

Keep the Static Web Apps workflow **for client review only, as a temporary deployment.**

- It isn't production, it isn't the target architecture, and nothing else may depend on it.
- It's a known exception to "the gateway is the only public endpoint" (ADR 0001) until it's removed.
- Remove it once prompt 6.2 deploys the Azure dev environment through CI:
  - delete the workflow
  - delete the Static Web Apps resource
  - delete the `AZURE_STATIC_WEB_APPS_API_TOKEN_*` repository secret

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Keep as a time-boxed review copy (chosen)** | Free tier; the client sees progress now. A second public host and a CSR-only rendering differ from production. |
| Remove now | Matches the architecture, but there's nothing to review until Phase 6. |
| `workflow_dispatch` only (manual runs) | Fewer accidental deploys, but someone has to remember to run it after each change. |

## Consequences

- **A push to `main` deploys publicly.** That's why `git push` is an ask rule for Claude sessions.
- **SEO and rendering on this copy** (no SSR, no server routes such as `/sitemap.xml`) aren't representative. Don't use it for SEO or performance checks.
- **The `build:swa` script and `staticwebapp.config.json` exist only for this copy.** Remove them with it.
