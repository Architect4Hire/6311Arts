output "identity_ids" {
  description = "Resource id of each application's identity, by logical name. The deploy step assigns these to the Container Apps."
  value       = { for name, identity in azurerm_user_assigned_identity.app : name => identity.id }
}

output "client_ids" {
  description = <<-EOT
    Client id of each application's identity, by logical name. Each app gets its own as AZURE_CLIENT_ID,
    which is what makes DefaultAzureCredential pick that identity for blobs and for the Entra connection to
    SQL rather than guessing among the identities on the container.
  EOT
  value       = { for name, identity in azurerm_user_assigned_identity.app : name => identity.client_id }
}

output "principal_ids" {
  description = "Object id of each application's identity. scripts/grant-sql-identities.sh needs these to create the database users."
  value       = { for name, identity in azurerm_user_assigned_identity.app : name => identity.principal_id }
}

output "names" {
  description = "Identity names, by logical app name. A database user created FROM EXTERNAL PROVIDER is named after the identity."
  value       = { for name, identity in azurerm_user_assigned_identity.app : name => identity.name }
}
