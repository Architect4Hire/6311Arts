# Logs, telemetry, the image registry, the Container Apps environment and the vault every secret lives in.

resource "azurerm_log_analytics_workspace" "main" {
  name                = var.names.log_analytics
  resource_group_name = var.resource_group_name
  location            = var.location

  sku               = "PerGB2018"
  retention_in_days = var.log_retention_in_days
  daily_quota_gb    = var.log_daily_quota_gb

  tags = var.tags
}

# Workspace-based Application Insights: the OpenTelemetry ServiceDefaults sets up reports here, and one
# trace is meant to run gateway -> API -> SQL -> outbox -> Worker -> Square (CLAUDE.md "Observability").
resource "azurerm_application_insights" "main" {
  name                = var.names.application_insights
  resource_group_name = var.resource_group_name
  location            = var.location

  application_type = "web"
  workspace_id     = azurerm_log_analytics_workspace.main.id

  retention_in_days   = var.log_retention_in_days
  sampling_percentage = 100

  # Visitors' addresses are not telemetry this site needs.
  ip_masking_enabled = true

  # Left on: the connection string carries an ingestion key, which is how an OTLP exporter authenticates
  # without a credential. Turning it off means the apps must send with their managed identity instead, and
  # that is an application change (docs/infrastructure/README.md, "Gaps in the application").
  local_authentication_enabled = true

  tags = var.tags
}

resource "azurerm_container_registry" "main" {
  name                = var.names.container_registry
  resource_group_name = var.resource_group_name
  location            = var.location

  sku = var.container_registry_sku

  # No admin user. CI pushes with its own federated identity (AcrPush, granted in bootstrap) and each app
  # pulls with its own managed identity (AcrPull, granted in the identity module). There is no registry
  # password anywhere.
  admin_enabled = false

  tags = var.tags
}

resource "azurerm_container_app_environment" "main" {
  name                = var.names.container_apps_env
  resource_group_name = var.resource_group_name
  location            = var.location

  # azurerm 5.x: logs_destination is no longer inferred. Without it the environment is streaming-only and
  # log_analytics_workspace_id is rejected.
  logs_destination           = "log-analytics"
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id

  zone_redundancy_enabled = var.zone_redundancy_enabled

  # A workload-profiles environment with only the Consumption profile. It bills the same as a
  # consumption-only environment and scales to zero, but a dedicated profile can be added later without
  # rebuilding the environment — which a consumption-only environment cannot do.
  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }

  tags = var.tags
}

# Every secret the applications read. They read them as Key Vault references resolved by their own managed
# identity, so a secret value is never an environment variable in a template or a value in a workflow
# (CLAUDE.md "Environments and secrets").
resource "azurerm_key_vault" "main" {
  name                = var.names.key_vault
  resource_group_name = var.resource_group_name
  location            = var.location
  tenant_id           = var.tenant_id

  sku_name = "standard"

  # Required in azurerm 5.x, and the right answer anyway: access is governed by role assignments, not by a
  # second, parallel access-policy model.
  rbac_authorization_enabled = true

  purge_protection_enabled   = var.key_vault_purge_protection_enabled
  soft_delete_retention_days = var.key_vault_soft_delete_retention_days

  public_network_access_enabled = true

  network_acls {
    bypass = "AzureServices"

    # Container Apps on a consumption environment has no stable outbound address to allow, so the vault is
    # reachable and authorisation does the work. Narrowing this needs the environment in a VNet first.
    default_action = "Allow"
  }

  tags = var.tags
}

# The people who run scripts/set-secrets.sh. The CI identity already holds this role at resource group
# scope, from bootstrap.
resource "azurerm_role_assignment" "key_vault_admins" {
  for_each = toset(var.key_vault_admin_object_ids)

  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = each.value
  description          = "Set the values of the secret shells Terraform creates"
}
