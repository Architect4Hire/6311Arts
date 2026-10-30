# The platform half of the Azure environment: everything the five applications and the migrations job run
# on, but not the applications themselves. ADR 0008 makes Arts6311.AppHost the single source of truth for
# the application model, and that stays true — the apps are deployed from the Aspire model, bound to the
# resources named in this module's outputs. See docs/infrastructure/README.md, "Who owns what".
#
# Nothing in here is reachable from a Claude session: `aspire deploy` and `azd` are denied in
# .claude/settings.json, and an apply runs in CI or from a person's laptop (CLAUDE.md "Hygiene").

data "azurerm_client_config" "current" {}

# Created by bootstrap, so that the identity CI runs as can be scoped to this one group.
data "azurerm_resource_group" "main" {
  name = var.resource_group_name
}

module "platform" {
  source = "./modules/platform"

  names               = local.names
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location
  tags                = local.tags
  tenant_id           = data.azurerm_client_config.current.tenant_id

  log_retention_in_days   = var.log_retention_in_days
  log_daily_quota_gb      = var.log_daily_quota_gb
  container_registry_sku  = var.container_registry_sku
  zone_redundancy_enabled = var.container_apps_zone_redundancy_enabled

  key_vault_purge_protection_enabled   = var.key_vault_purge_protection_enabled
  key_vault_soft_delete_retention_days = var.key_vault_soft_delete_retention_days
  key_vault_admin_object_ids           = var.key_vault_admin_object_ids
}

module "data" {
  source = "./modules/data"

  names               = local.names
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location
  tags                = local.tags
  tenant_id           = data.azurerm_client_config.current.tenant_id

  sql_admin_group_name            = var.sql_admin_group_name
  sql_admin_group_object_id       = var.sql_admin_group_object_id
  sql_sku_name                    = var.sql_sku_name
  sql_min_capacity                = var.sql_min_capacity
  sql_auto_pause_delay_in_minutes = var.sql_auto_pause_delay_in_minutes
  sql_max_size_gb                 = var.sql_max_size_gb
  sql_zone_redundant              = var.sql_zone_redundant
  sql_short_term_retention_days   = var.sql_short_term_retention_days
  sql_long_term_retention         = var.sql_long_term_retention
  sql_allow_azure_services        = var.sql_allow_azure_services

  storage_replication_type    = var.storage_replication_type
  storage_blob_retention_days = var.storage_blob_retention_days

  redis_sku_name                  = var.redis_sku_name
  redis_persistence               = var.redis_persistence
  redis_eviction_policy           = var.redis_eviction_policy
  redis_high_availability_enabled = var.redis_high_availability_enabled
}

module "email" {
  source = "./modules/email"

  names               = local.names
  resource_group_name = data.azurerm_resource_group.main.name
  tags                = local.tags

  data_location = var.acs_data_location
  custom_domain = var.email_custom_domain
}

module "identity" {
  source = "./modules/identity"

  prefix              = local.prefix
  apps                = local.apps
  resource_group_name = data.azurerm_resource_group.main.name
  location            = data.azurerm_resource_group.main.location
  tags                = local.tags

  container_registry_id = module.platform.container_registry_id
  key_vault_id          = module.platform.key_vault_id
  storage_account_id    = module.data.storage_account_id
}
