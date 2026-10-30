variable "names" {
  description = "The computed resource names from the root module's locals."
  type        = map(string)
}

variable "resource_group_name" {
  type = string
}

variable "location" {
  type = string
}

variable "tenant_id" {
  type = string
}

variable "tags" {
  type = map(string)
}

variable "log_retention_in_days" {
  type = number
}

variable "log_daily_quota_gb" {
  type = number
}

variable "container_registry_sku" {
  type = string
}

variable "zone_redundancy_enabled" {
  type = bool
}

variable "key_vault_purge_protection_enabled" {
  type = bool
}

variable "key_vault_soft_delete_retention_days" {
  type = number
}

variable "key_vault_admin_object_ids" {
  description = "Principals granted Key Vault Secrets Officer, so they can set the secret values this module only creates shells for."
  type        = list(string)
  default     = []
}
