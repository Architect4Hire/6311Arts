output "sql_server_name" {
  value = azurerm_mssql_server.main.name
}

output "sql_server_fqdn" {
  value = azurerm_mssql_server.main.fully_qualified_domain_name
}

output "sql_database_name" {
  value = azurerm_mssql_database.appdb.name
}

output "storage_account_id" {
  value = azurerm_storage_account.main.id
}

output "storage_account_name" {
  value = azurerm_storage_account.main.name
}

output "blob_service_uri" {
  description = "What ConnectionStrings__blobs is set to. A URI, not a credential: the client pairs it with the app's managed identity."
  value       = azurerm_storage_account.main.primary_blob_endpoint
}

output "redis_hostname" {
  value = azurerm_managed_redis.main.hostname
}

output "redis_connection_string" {
  description = "StackExchange.Redis format, with the access key. Written to Key Vault by the root module; it is in Terraform state as well, which is why the state container is Entra-only and private."
  value       = "${azurerm_managed_redis.main.hostname}:${azurerm_managed_redis.main.default_database[0].port},ssl=True,abortConnect=False,password=${azurerm_managed_redis.main.default_database[0].primary_access_key}"
  sensitive   = true
}
