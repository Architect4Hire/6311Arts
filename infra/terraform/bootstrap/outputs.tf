# These are the values the environment backend files and the CI workflow need. Print them once with
# `terraform output` after the first apply; none of them is a secret.

output "state_resource_group_name" {
  description = "Resource group holding the state account."
  value       = azurerm_resource_group.state.name
}

output "state_storage_account_name" {
  description = "Put this in envs/*.backend.hcl as `storage_account_name`."
  value       = azurerm_storage_account.state.name
}

output "state_container_name" {
  description = "Put this in envs/*.backend.hcl as `container_name`."
  value       = azurerm_storage_container.state.name
}

output "resource_group_names" {
  description = "The resource group each environment's root module manages, by environment. Goes in envs/<env>.tfvars as `resource_group_name`."
  value       = { for env, rg in azurerm_resource_group.env : env => rg.name }
}

output "deploy_client_ids" {
  description = "Client id of each environment's deploy identity, by environment. The CI deploy job sets this as ARM_CLIENT_ID / azure/login's client-id."
  value       = { for env, id in azurerm_user_assigned_identity.deploy : env => id.client_id }
}

output "plan_client_ids" {
  description = "Client id of each environment's pull-request plan identity, by environment."
  value       = { for env, id in azurerm_user_assigned_identity.plan : env => id.client_id }
}

output "tenant_id" {
  description = "The CI jobs' ARM_TENANT_ID."
  value       = data.azurerm_client_config.current.tenant_id
}

output "subscription_id" {
  description = "The CI jobs' ARM_SUBSCRIPTION_ID."
  value       = var.subscription_id
}
