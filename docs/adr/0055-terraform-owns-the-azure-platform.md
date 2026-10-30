# 0055 — Terraform owns the Azure platform, Aspire owns the applications

- **Status:** Accepted
- **Date:** 2026-10-30
- **Rules affected:** `.claude/rules/aspire.md` ("Run mode vs publish mode"), CLAUDE.md "Environments and secrets"
- **Related:** ADR 0008 (run offline, publish to Container Apps), ADR 0026 (the end-to-end test)

## Context

ADR 0008 makes `Arts6311.AppHost` the single source of truth for both modes, and `aspire publish` generates
reviewable infrastructure from it. That is the right answer for the application topology and the wrong
answer for the stateful resources around it: a database with a 35-day restore window, a storage account
holding the only copy of every uploaded image, and a key vault with purge protection all have lifecycles a
deploy should never touch. They need a plan a person reads before anything changes, drift detection, and a
`prevent_destroy` that refuses a replacement outright.

CI/CD also needs a place for the things Aspire has no opinion about at all: the remote state, the resource
group boundary that scopes the CI identity, the GitHub OIDC federated credentials, and resource provider
registration.

## Decision

Split the Azure environment in two, by lifecycle.

- **Terraform** (`infra/terraform/`) owns the platform: the Container Apps environment, the container
  registry, Log Analytics and Application Insights, Azure SQL and `appdb`, the storage account and its
  `media` and `dataprotection` containers, Managed Redis, Communication Services, Key Vault and every
  secret the applications read, and **one managed identity per application with only the roles that
  application needs**.
- **`Arts6311.AppHost`** keeps owning the applications: the five Container Apps, the `migrations` and
  `importer` jobs, ingress, scaling, revisions and environment variables. Publish mode binds them to the
  resources Terraform created rather than provisioning its own.
- **`terraform output` is the contract** between the two. The deploy step reads it; the mapping from output
  to Aspire call is tabulated in `docs/infrastructure/README.md`.
- **State** lives in its own resource group and storage account, Entra-only, with shared-key access
  switched off, created once by `infra/terraform/bootstrap`. Bootstrap also creates each environment's
  resource group, so the CI identity can be scoped to one group rather than the subscription.
- **A pull request plans; only `workflow_dispatch` applies**, against a GitHub environment whose federated
  credential subject *is* that environment — so the approval gate cannot be bypassed, only broken.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Terraform for the platform, Aspire for the apps (chosen)** | Two tools and one handshake to keep honest. In exchange, the application topology stays described once, in C#, tested by the end-to-end test; and the stateful resources get plans, drift detection and `prevent_destroy`. |
| Everything in Terraform | One tool, one plan. But the app topology is then described twice — once in the AppHost the E2E test exercises, once in HCL — and the two drift silently. Would supersede ADR 0008. |
| Everything from `aspire publish` / `azd` | No second tool and no handshake. But a deploy becomes able to recreate the database, there is no plan to read before it does, and the state and OIDC bootstrap still have to live somewhere. |
| Bicep instead of Terraform for the platform | First-party, and the same shape as what `aspire publish` emits. Weaker drift story and no equivalent of `prevent_destroy`. |

## Consequences

- **The handshake is a real seam and can rot.** An app added to the AppHost needs an entry in the root
  module's `apps` map, or it starts with no identity and cannot pull its image. Adding a resource the apps
  reference means adding it to Terraform *and* to the publish-mode model.
- **The platform can be applied before the application can be deployed,** and currently is: publish mode
  does not exist yet (prompt 6.1). `docs/infrastructure/README.md` §8 lists every gap that fails a deploy.
- **Two things Terraform cannot do are scripted beside it,** and a new environment is not usable until both
  have run: setting the Key Vault secret values (`scripts/set-secrets.sh`) and creating the SQL database
  users for the managed identities (`scripts/grant-sql-identities.sh`).
- **No application secret enters Terraform state.** Terraform creates each as a shell and ignores its value
  for ever. Values Azure itself produces — the Redis access key among them — *are* in state, which is why
  read access to the state container is read access to secrets.
- **Deploys still never run from a Claude session.** `aspire deploy` and `azd` stay denied in
  `.claude/settings.json`; this ADR adds `terraform apply` to the same category.
