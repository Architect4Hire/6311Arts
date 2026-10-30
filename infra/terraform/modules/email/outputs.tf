output "communication_service_name" {
  value = azurerm_communication_service.main.name
}

output "connection_string" {
  description = "Carries an access key. Written to Key Vault by the root module."
  value       = azurerm_communication_service.main.primary_connection_string
  sensitive   = true
}

output "azure_managed_domain" {
  description = "The <guid>.azurecomm.net sending domain. The From address is donotreply@ this."
  value       = azurerm_email_communication_service_domain.azure_managed.from_sender_domain
}

output "custom_domain_verification_records" {
  description = "The DNS records a customer-managed domain needs before it will send. Empty unless email_custom_domain is set."
  value       = var.custom_domain == null ? null : azurerm_email_communication_service_domain.custom[0].verification_records
}
