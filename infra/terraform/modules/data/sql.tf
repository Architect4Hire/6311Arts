# Azure SQL. The schema is applied by Arts6311.MigrationService, which runs as a Container Apps job before
# the API and Worker start (.claude/rules/aspire.md); nothing here touches the schema.

locals {
  # GP_S_* and HS_S_* are the serverless families; S0, S1, GP_Gen5_2 and the rest bill a fixed rate.
  is_serverless = startswith(var.sql_sku_name, "GP_S_") || startswith(var.sql_sku_name, "HS_S_")
}

resource "azurerm_mssql_server" "main" {
  name                = var.names.sql_server
  resource_group_name = var.resource_group_name
  location            = var.location
  version             = "12.0"

  minimum_tls_version = "1.2"

  # Reachable over the public endpoint, with the firewall below as the only network control. Authentication
  # is the real control: there is no SQL login and no password to leak.
  public_network_access_enabled = true

  # No administrator_login / administrator_login_password at all. With azuread_authentication_only the
  # provider does not require them, and a server with no SQL authentication cannot be brute-forced into.
  azuread_administrator {
    login_username              = var.sql_admin_group_name
    object_id                   = var.sql_admin_group_object_id
    tenant_id                   = var.tenant_id
    azuread_authentication_only = true
  }

  tags = var.tags
}

# The one rule a consumption Container Apps environment needs: 0.0.0.0-0.0.0.0 is Azure's "allow Azure
# services" sentinel, not a range of addresses. Consumption apps have no stable outbound address to allow
# instead; pinning this down means a VNet-integrated environment with a service endpoint.
resource "azurerm_mssql_firewall_rule" "azure_services" {
  count = var.sql_allow_azure_services ? 1 : 0

  name             = "AllowAllWindowsAzureIps"
  server_id        = azurerm_mssql_server.main.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_mssql_database" "appdb" {
  # The logical name the AppHost and ArtsDbContext.ConnectionName both use.
  name      = "appdb"
  server_id = azurerm_mssql_server.main.id

  sku_name    = var.sql_sku_name
  max_size_gb = var.sql_max_size_gb
  collation   = "SQL_Latin1_General_CP1_CI_AS"

  # Serverless-only settings, so they are null on anything else. Without this, switching to a DTU SKU to
  # cut the dev bill (sql_sku_name = "S0") is rejected rather than being the one-line change it should be.
  min_capacity                = local.is_serverless ? var.sql_min_capacity : null
  auto_pause_delay_in_minutes = local.is_serverless ? var.sql_auto_pause_delay_in_minutes : null

  zone_redundant = var.sql_zone_redundant

  # Point-in-time restore. Prompt 6.3 asks for PITR to be confirmed before cutover; this is where the
  # window is set.
  short_term_retention_policy {
    retention_days = var.sql_short_term_retention_days
  }

  long_term_retention_policy {
    weekly_retention  = var.sql_long_term_retention.weekly_retention
    monthly_retention = var.sql_long_term_retention.monthly_retention
    yearly_retention  = var.sql_long_term_retention.yearly_retention
    week_of_year      = var.sql_long_term_retention.week_of_year
  }

  tags = var.tags

  lifecycle {
    # The database holds the CMS content, the orders projection and the audit log, which ADR 0017 makes
    # immutable. A plan that proposes to replace it is a plan to delete all of it, and that should never
    # happen by accident — change the SKU in the portal and import, or remove this block deliberately.
    prevent_destroy = true
  }
}
