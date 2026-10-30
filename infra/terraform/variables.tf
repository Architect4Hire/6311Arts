# ---------------------------------------------------------------------------------------------------------
# Identity of the environment
# ---------------------------------------------------------------------------------------------------------

variable "subscription_id" {
  description = "The subscription this environment lives in. Required for plan and apply."
  type        = string
}

variable "project" {
  description = "Name stem for every resource. Must match bootstrap's."
  type        = string
  default     = "6311arts"
}

variable "environment" {
  description = "dev or prod. Decides the shape of everything sized below, and is part of every name."
  type        = string

  validation {
    condition     = contains(["dev", "prod"], var.environment)
    error_message = "environment must be dev or prod. A third environment needs a resource group and identities from bootstrap first."
  }
}

variable "unique_suffix" {
  description = "The same value given to bootstrap. Globally unique names are built from it."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{4,6}$", var.unique_suffix))
    error_message = "unique_suffix must be 4-6 lowercase letters and digits."
  }
}

variable "resource_group_name" {
  description = "The resource group bootstrap created for this environment. Read as a data source: this module never creates or deletes it."
  type        = string
}

variable "tags" {
  description = "Extra tags merged into every resource."
  type        = map(string)
  default     = {}
}

# ---------------------------------------------------------------------------------------------------------
# Logs
# ---------------------------------------------------------------------------------------------------------

variable "log_retention_in_days" {
  description = "Log Analytics retention. 30 is the free floor; longer costs per GB."
  type        = number
  default     = 30
}

variable "log_daily_quota_gb" {
  description = <<-EOT
    Daily ingestion cap, in GB. -1 is unlimited. A cap is a cost stop, not a quota to aim at: once it is hit
    the workspace stops ingesting for the rest of the day, which also stops the traces an incident needs.
  EOT
  type        = number
  default     = 1
}

# ---------------------------------------------------------------------------------------------------------
# Container registry and the Container Apps environment
# ---------------------------------------------------------------------------------------------------------

variable "container_registry_sku" {
  description = "Basic is enough for one small app: 10 GB included, no geo-replication. Standard raises the included storage, Premium adds private endpoints and geo-replication."
  type        = string
  default     = "Basic"

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.container_registry_sku)
    error_message = "container_registry_sku must be Basic, Standard or Premium."
  }
}

variable "container_apps_zone_redundancy_enabled" {
  description = "Zone redundancy for the Container Apps environment. Requires an infrastructure subnet, so it is off until the environment is put in a VNet."
  type        = bool
  default     = false
}

# ---------------------------------------------------------------------------------------------------------
# Azure SQL
# ---------------------------------------------------------------------------------------------------------

variable "sql_sku_name" {
  description = <<-EOT
    GP_S_* is serverless: it bills per vCore-second and can pause. Note that it only pauses while nothing is
    connected, and Arts6311.Worker polls the outbox continuously (ADR 0003), so a running worker keeps the
    database awake. In dev that is the single largest line on the bill; scale the worker to zero when the
    environment is idle, or accept the cost.
  EOT
  type        = string
  default     = "GP_S_Gen5_1"
}

variable "sql_min_capacity" {
  description = "Minimum vCores for a serverless database. Ignored by provisioned SKUs."
  type        = number
  default     = 0.5
}

variable "sql_auto_pause_delay_in_minutes" {
  description = "Minutes of inactivity before a serverless database pauses. -1 disables auto-pause, which is what prod wants."
  type        = number
  default     = 60
}

variable "sql_max_size_gb" {
  description = "Maximum database size."
  type        = number
  default     = 32
}

variable "sql_zone_redundant" {
  description = "Zone-redundant database. Not available on every serverless SKU, and it costs more."
  type        = bool
  default     = false
}

variable "sql_short_term_retention_days" {
  description = "Point-in-time restore window, in days (1-35). ADR 0008's prod story and prompt 6.3's 'PITR confirmed' rest on this."
  type        = number
  default     = 7

  validation {
    condition     = var.sql_short_term_retention_days >= 1 && var.sql_short_term_retention_days <= 35
    error_message = "sql_short_term_retention_days must be between 1 and 35."
  }
}

variable "sql_long_term_retention" {
  description = <<-EOT
    Long-term backup retention, as ISO 8601 durations. "PT0S" means none. Prod keeps weekly and monthly
    copies; dev keeps none, because dev data is seeded and disposable.
  EOT
  type = object({
    weekly_retention  = optional(string, "PT0S")
    monthly_retention = optional(string, "PT0S")
    yearly_retention  = optional(string, "PT0S")
    week_of_year      = optional(number, 1)
  })
  default = {}
}

variable "sql_admin_group_name" {
  description = <<-EOT
    Display name of the Entra group that administers this SQL server. A group, not a person: the server is
    Entra-authentication-only, so whoever is in this group is the only one who can create the database users
    the app identities sign in as (scripts/grant-sql-identities.sh).
  EOT
  type        = string
}

variable "sql_admin_group_object_id" {
  description = "Object id of that group (`az ad group show --group <name> --query id -o tsv`)."
  type        = string
}

variable "sql_allow_azure_services" {
  description = <<-EOT
    Opens the server firewall to Azure-internal callers (the 0.0.0.0 rule). Container Apps on a consumption
    environment has no stable outbound address, so without this nothing can connect. The server still accepts
    no password: authentication is Entra-only.
  EOT
  type        = bool
  default     = true
}

# ---------------------------------------------------------------------------------------------------------
# Storage for media and the Data Protection key ring
# ---------------------------------------------------------------------------------------------------------

variable "storage_replication_type" {
  description = "LRS for dev; prod holds the only copy of uploaded artwork images, so it wants at least ZRS."
  type        = string
  default     = "LRS"
}

variable "storage_blob_retention_days" {
  description = "Soft-delete window for blobs. A deleted rendition or a purged master can be recovered inside it (ADR 0030)."
  type        = number
  default     = 14
}

# ---------------------------------------------------------------------------------------------------------
# Redis (ADR 0009)
# ---------------------------------------------------------------------------------------------------------

variable "redis_sku_name" {
  description = <<-EOT
    Azure Managed Redis SKU. Balanced_B0 is the smallest. Azure Cache for Redis is not an option any more:
    creation of its Basic, Standard and Premium tiers was blocked for existing subscriptions on
    2026-10-01, ahead of retirement on 2028-09-30.
  EOT
  type        = string
  default     = "Balanced_B0"
}

variable "redis_persistence" {
  description = <<-EOT
    "none", "rdb" or "aof". The gateway keeps session tickets in Redis (ADR 0009), so losing the instance
    signs every member and every staff user out. "aof" writes every second and is what prod wants; dev can
    live with "none".
  EOT
  type        = string
  default     = "none"

  validation {
    condition     = contains(["none", "rdb", "aof"], var.redis_persistence)
    error_message = "redis_persistence must be none, rdb or aof."
  }
}

variable "redis_eviction_policy" {
  description = <<-EOT
    What Redis drops when it runs out of memory. VolatileLRU, Azure's default, evicts the least recently
    used key that has a TTL — which is every cache entry and also every session ticket, so a busy evening
    can sign a visitor out. NoEviction fails the write instead, which breaks sign-in rather than cache
    writes. Neither is free; see docs/infrastructure/README.md, "Open questions".
  EOT
  type        = string
  default     = "VolatileLRU"
}

variable "redis_high_availability_enabled" {
  description = "Managed Redis replication. Cannot be changed without recreating the instance."
  type        = bool
  default     = true
}

# ---------------------------------------------------------------------------------------------------------
# Email (ADR 0023, ADR 0047)
# ---------------------------------------------------------------------------------------------------------

variable "acs_data_location" {
  description = "Where Communication Services stores its data. Required in azurerm 5.x — it no longer defaults."
  type        = string
  default     = "United States"
}

variable "email_custom_domain" {
  description = <<-EOT
    A sending domain to add alongside the Azure-managed one, for example "6311arts.com". Leave null to use
    the Azure-managed domain only, which is what every environment except production should do: it sends
    from a donotreply@<guid>.azurecomm.net address and is rate limited, so a form confirmation from dev can
    never look like it came from the centre. A custom domain is created unverified; the TXT, SPF and DKIM
    records have to be published by hand before it will send (prompt 6.3).
  EOT
  type        = string
  default     = null
}

# ---------------------------------------------------------------------------------------------------------
# Key Vault
# ---------------------------------------------------------------------------------------------------------

variable "key_vault_purge_protection_enabled" {
  description = <<-EOT
    Once on, this cannot be turned off, and the vault cannot be purged before its soft-delete window ends.
    Prod wants it. Dev does not, or a destroyed dev environment blocks the next one from reusing the name.
  EOT
  type        = bool
  default     = false
}

variable "key_vault_soft_delete_retention_days" {
  description = "Days a deleted vault or secret stays recoverable (7-90)."
  type        = number
  default     = 7
}

variable "key_vault_admin_object_ids" {
  description = <<-EOT
    Principals that may read and write secret values in addition to the CI identity — the people who run
    scripts/set-secrets.sh. Object ids of users or groups (`az ad signed-in-user show --query id -o tsv`).
  EOT
  type        = list(string)
  default     = []
}

# ---------------------------------------------------------------------------------------------------------
# Application settings that are configuration, not secrets
# ---------------------------------------------------------------------------------------------------------

variable "seed_admin_email" {
  description = "The first Admin MigrationService seeds (CLAUDE.md 'Staff sign-in'). The password is a Key Vault secret, set out of band."
  type        = string
  default     = "admin@6311arts.localhost"
}

variable "forms_staff_email" {
  description = <<-EOT
    Where new artist submissions and contact messages are announced (ADR 0047). Production alone sets the
    centre's real inbox; every other environment keeps a non-routable address so a visitor's message never
    reaches a stranger from dev.
  EOT
  type        = string
  default     = "staff@6311arts.localhost"
}

variable "site_allowed_hosts" {
  description = "Hosts the Angular SSR engine will render for (ADR 0046). The Container Apps domain, plus the real domain in prod."
  type        = list(string)
  default     = []
}

variable "square_mode" {
  description = "fake, sandbox or production (ADR 0007). Only prod is ever production, and run mode never reaches it at all."
  type        = string
  default     = "sandbox"

  validation {
    condition     = contains(["fake", "sandbox", "production"], var.square_mode)
    error_message = "square_mode must be fake, sandbox or production."
  }
}
