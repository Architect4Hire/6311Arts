variable "prefix" {
  type = string
}

variable "apps" {
  description = "One entry per application, keyed by the AppHost's logical name, saying what that application is allowed to reach."
  type = map(object({
    blobs   = bool
    sql     = bool
    secrets = bool
  }))
}

variable "resource_group_name" {
  type = string
}

variable "location" {
  type = string
}

variable "tags" {
  type = map(string)
}

variable "container_registry_id" {
  type = string
}

variable "key_vault_id" {
  type = string
}

variable "storage_account_id" {
  type = string
}
