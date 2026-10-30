# Terraform — the Azure platform

The plan, the reasoning and the open decisions are in
[`docs/infrastructure/README.md`](../../docs/infrastructure/README.md). This file is the command sheet.

Terraform owns the **platform**: the resource group's contents, the Container Apps environment, the image
registry, Azure SQL, storage, Key Vault, Managed Redis, Communication Services, Log Analytics and
Application Insights, plus one managed identity per application. It does **not** own the applications —
`Arts6311.AppHost` is still the single source of truth for those (ADR 0008), and they are deployed against
the resources in `terraform output`.

```text
bootstrap/        run once, by a person, local state: remote state account, the resource groups, GitHub OIDC
./               the root module, one state file per environment
  modules/platform  logs, telemetry, registry, Container Apps environment, Key Vault
  modules/data      Azure SQL, storage (media + dataprotection), Managed Redis
  modules/email     Communication Services email
  modules/identity  one managed identity per app, and only the roles that app needs
  envs/             <env>.backend.hcl (where state lives) + <env>.tfvars (how big, how safe)
  scripts/          the two things Terraform cannot do: set secret values, create SQL users
```

## Prerequisites

- Terraform >= 1.9 (CI pins 1.17.1), `az`, `openssl`; `sqlcmd` for the SQL grants.
- An Entra group for the SQL administrators, and its object id.
- A subscription where you can register resource providers and create role assignments — bootstrap needs
  Owner or User Access Administrator. Nothing after bootstrap does.

## First time

```bash
# 1. Bootstrap. Creates the state account, rg-6311arts-dev, rg-6311arts-prod and the CI identities.
cd infra/terraform/bootstrap
cp terraform.tfvars.example terraform.tfvars   # fill in subscription, repo, unique_suffix
az login && az account set --subscription <id>
terraform init
terraform plan -out bootstrap.tfplan           # read it
terraform apply bootstrap.tfplan
terraform output                               # the values the next steps need

# 2. Fill in the placeholders. Every CHANGEME in envs/ comes from that output.
cd ..
$EDITOR envs/dev.backend.hcl envs/dev.tfvars

# 3. The platform itself.
terraform init -backend-config=envs/dev.backend.hcl
terraform plan -var-file=envs/dev.tfvars -out dev.tfplan
terraform apply dev.tfplan

# 4. The two things Terraform cannot do.
./scripts/set-secrets.sh --vault "$(terraform output -raw key_vault_name)"
./scripts/grant-sql-identities.sh
```

Then set these as GitHub **variables** (not secrets — they are identifiers):
`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_PLAN_CLIENT_ID_DEV` at the repository level, and
`AZURE_CLIENT_ID` on each of the `dev` and `prod` GitHub environments, with reviewers on `prod`.

## Every time after

```bash
terraform init -backend-config=envs/dev.backend.hcl    # once per clone, or after a module changes
terraform plan -var-file=envs/dev.tfvars -out dev.tfplan
terraform apply dev.tfplan
terraform fmt -recursive                               # before committing
```

A pull request that touches `infra/terraform/**` plans dev automatically, read-only. An apply runs from the
**Infrastructure** workflow (`workflow_dispatch`), against a GitHub environment, which is where the approval
gate lives. Nothing applies on a push.

## Things that will bite

- **`terraform fmt -recursive` has never been run against these files.** They were written without a
  Terraform binary available, so the first `fmt -check` in CI may fail on whitespace. Run it once.
- **A first apply can 403 writing Key Vault secrets.** The role assignment is inherited from the resource
  group and takes a minute or two to propagate. Re-run the apply.
- **`prevent_destroy` is set on `appdb` and the media storage account.** A plan that wants to replace
  either will refuse. That is the point — read why before removing it.
- **Secrets are shells.** Terraform writes the literal `REPLACE-ME ...` and then ignores the value for
  ever. A revision deployed before `set-secrets.sh` has run fails to start rather than running with a
  guessable key.
- **Azure SQL serverless does not pause while the Worker is running,** because the outbox dispatcher polls
  (ADR 0003). In dev that is the biggest line on the bill; scale the worker to zero when idle.
