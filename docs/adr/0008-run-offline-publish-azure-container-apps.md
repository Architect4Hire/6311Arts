# 0008 — Run mode offline / publish to Azure Container Apps

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/aspire.md`, `.claude/rules/media.md` (storage), `.claude/rules/external.md` (email), CLAUDE.md "Environments and secrets"

## Context

The system is built on a laptop for months before it goes live, by a small team, for a nonprofit with a modest hosting budget. One application model should describe both "run it here" and "what Azure gets", so environments don't drift and nobody keeps a manual setup script.

## Decision

- **`Arts6311.AppHost` is the single source of truth for both modes.**
- **Run mode needs no Azure subscription.** SQL runs as a container (`AddAzureSqlServer(...).RunAsContainer()`), storage runs as Azurite, mail goes to Mailpit, Redis runs as a container, and Square is the fake (ADR 0007). Generated secrets persist to user-secrets.
- **Publish mode targets Azure Container Apps** (`AddAzureContainerAppEnvironment`): Azure SQL, Azure Storage, Key Vault references for every secret, Azure Communication Services email, Application Insights, and a managed identity per app. Only the gateway gets external ingress. Migrations and the importer run as Container Apps jobs. The Square fake and dev tunnel are excluded.
- **Environments:**
  - dev scales to zero, except the gateway
  - prod keeps a minimum of 1 for gateway, api, site and worker
- **Deploys run only in CI** (GitHub OIDC) or by a person, never from a Claude session.

## Options considered

| Option | Cost / trade-off (rough, from `docs/architecture/architecture.md`) |
|---|---|
| **Azure Container Apps from the Aspire model (chosen)** | About $20/mo dev and $85–100/mo prod, plus Redis (ADR 0009). Scale to zero in dev; Aspire generates the infrastructure. |
| Azure App Service per app | Mature, but no scale to zero, a plan per tier, and it fits a multi-container model less naturally. |
| AKS | Full control, but cluster operations and cost are far beyond this workload. |
| Single VM + Docker Compose | Cheapest, but patching, TLS, backups and scaling become manual work. |

## Consequences

- Every resource is declared in the AppHost, with a local stand-in and an Azure mapping. Nothing uses `AsExisting`/`PublishAsExisting` in run mode.
- Aspire APIs move between versions. Verify them on aspire.dev before use, and pin them in one place.
- `aspire publish` output is reviewable, but `aspire deploy`, `azd` and the related commands are denied in `.claude/settings.json`.
