variable "names" {
  type = map(string)
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

variable "sql_admin_group_name" {
  type = string
}

variable "sql_admin_group_object_id" {
  type = string
}

variable "sql_sku_name" {
  type = string
}

variable "sql_min_capacity" {
  type = number
}

variable "sql_auto_pause_delay_in_minutes" {
  type = number
}

variable "sql_max_size_gb" {
  type = number
}

variable "sql_zone_redundant" {
  type = bool
}

variable "sql_short_term_retention_days" {
  type = number
}

variable "sql_long_term_retention" {
  type = object({
    weekly_retention  = optional(string, "PT0S")
    monthly_retention = optional(string, "PT0S")
    yearly_retention  = optional(string, "PT0S")
    week_of_year      = optional(number, 1)
  })
}

variable "sql_allow_azure_services" {
  type = bool
}

variable "storage_replication_type" {
  type = string
}

variable "storage_blob_retention_days" {
  type = number
}

variable "redis_sku_name" {
  type = string
}

variable "redis_persistence" {
  type = string
}

variable "redis_high_availability_enabled" {
  type = bool
}

variable "redis_eviction_policy" {
  type = string
}
