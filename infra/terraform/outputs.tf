# The contract between this module and the deploy step.
#
# Terraform owns the platform; Arts6311.AppHost still owns the application model (ADR 0008). These outputs
# are how the second half finds the first: the deploy job reads them and binds the Aspire model's Azure
# resources to the ones that already exist, rather than provisioning its own. docs/infrastructure/README.md,
# "The handshake", lists which Aspire call each output feeds.
#
# Read them as JSON in a workflow:  terraform output -json > infra.json

output "resource_group_name" {
  value = data.azurerm_resource_group.main.name
}

output "location" {
  value = data.azurerm_resource_group.main.location
}

output "container_registry_login_server" {
  description = "Where CI pushes the images. CI authenticates with its federated identity; the registry has no admin user."
  value       = module.platform.container_registry_login_server
}

output "container_app_environment_id" {
  description = "The environment every app and the migrations job is created in."
  value       = module.platform.container_app_environment_id
}

output "container_app_environment_default_domain" {
  description = "Apps answer at <app>.<this>. The gateway's public hostname until a custom domain is added (prompt 6.3)."
  value       = module.platform.container_app_environment_default_domain
}

output "gateway_url" {
  description = "The one public origin (ADR 0001). The Square webhook URL is this plus /webhooks/square."
  value       = "https://gateway.${module.platform.container_app_environment_default_domain}"
}

output "key_vault_name" {
  value = module.platform.key_vault_name
}

output "key_vault_uri" {
  description = "The prefix of every Key Vault reference the apps are given."
  value       = module.platform.key_vault_uri
}

output "app_identity_ids" {
  description = "Resource id of each application's managed identity, by the AppHost's logical name."
  value       = module.identity.identity_ids
}

output "app_identity_client_ids" {
  description = "Client id of each application's identity. Each app gets its own as AZURE_CLIENT_ID."
  value       = module.identity.client_ids
}

output "app_identity_names" {
  description = "Identity names, by logical app name — what scripts/grant-sql-identities.sh creates database users for."
  value       = module.identity.names
}

output "sql_server_fqdn" {
  value = module.data.sql_server_fqdn
}

output "sql_database_name" {
  value = module.data.sql_database_name
}

output "storage_account_name" {
  value = module.data.storage_account_name
}

output "redis_hostname" {
  value = module.data.redis_hostname
}

output "email_azure_managed_domain" {
  description = "Mail from a non-production environment leaves from donotreply@ this domain and nowhere else (ADR 0047)."
  value       = module.email.azure_managed_domain
}

output "email_custom_domain_verification_records" {
  description = "DNS records a custom sending domain still needs. Null unless email_custom_domain is set."
  value       = module.email.custom_domain_verification_records
}

output "secret_names" {
  description = "Every Key Vault secret the applications read, so the deploy step can build their references and scripts/set-secrets.sh can check none is still a placeholder."
  value = sort(concat(
    keys(local.secret_shells),
    [
      azurerm_key_vault_secret.appdb.name,
      azurerm_key_vault_secret.blobs.name,
      azurerm_key_vault_secret.cache.name,
      azurerm_key_vault_secret.mail.name,
      azurerm_key_vault_secret.application_insights.name,
    ],
  ))
}

output "app_settings" {
  description = <<-EOT
    The non-secret configuration the applications need, resolved for this environment. Not secrets: these
    are addresses and switches, and they are the values prompt 6.1 wires into the publish-mode model.
  EOT
  value = {
    ASPNETCORE_ENVIRONMENT = var.environment == "prod" ? "Production" : "Development"
    Square__Mode           = var.square_mode
    SeedAdmin__Email       = var.seed_admin_email
    Forms__StaffAddress    = var.forms_staff_email
    Email__FromAddress     = "donotreply@${module.email.azure_managed_domain}"
    Email__SiteOrigin      = "https://gateway.${module.platform.container_app_environment_default_domain}"
    NG_ALLOWED_HOSTS = join(",", length(var.site_allowed_hosts) > 0
      ? var.site_allowed_hosts
      : ["gateway.${module.platform.container_app_environment_default_domain}"])
  }
}
