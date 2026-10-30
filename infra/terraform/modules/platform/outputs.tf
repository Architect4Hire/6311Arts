output "log_analytics_workspace_id" {
  value = azurerm_log_analytics_workspace.main.id
}

output "application_insights_connection_string" {
  description = "Carries an ingestion key, so it is handled as a secret and written to Key Vault by the root module."
  value       = azurerm_application_insights.main.connection_string
  sensitive   = true
}

output "container_registry_id" {
  value = azurerm_container_registry.main.id
}

output "container_registry_login_server" {
  value = azurerm_container_registry.main.login_server
}

output "container_app_environment_id" {
  value = azurerm_container_app_environment.main.id
}

output "container_app_environment_default_domain" {
  description = "Apps are reachable at <app>.<this domain>. The gateway's public host, and part of site_allowed_hosts (ADR 0046)."
  value       = azurerm_container_app_environment.main.default_domain
}

output "key_vault_id" {
  value = azurerm_key_vault.main.id
}

output "key_vault_name" {
  value = azurerm_key_vault.main.name
}

output "key_vault_uri" {
  value = azurerm_key_vault.main.vault_uri
}
