variable "subscription_id" {
  description = "The subscription everything is created in. Required for plan and apply; `terraform validate` runs without it."
  type        = string
}

variable "project" {
  description = "Name stem for every resource. Keep it lowercase alphanumeric: it ends up in a storage account name."
  type        = string
  default     = "6311arts"

  validation {
    condition     = can(regex("^[a-z0-9]{3,12}$", var.project))
    error_message = "project must be 3-12 lowercase letters and digits, because storage account names allow nothing else."
  }
}

variable "unique_suffix" {
  description = <<-EOT
    Chosen once and never changed. Storage accounts, container registries, SQL servers and key vaults share
    one global namespace, so the names built from `project` alone would collide with somebody else's. Pick
    4-6 lowercase alphanumerics (for example the output of `openssl rand -hex 3`) and put the same value in
    this file and in every envs/*.tfvars.
  EOT
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{4,6}$", var.unique_suffix))
    error_message = "unique_suffix must be 4-6 lowercase letters and digits."
  }
}

variable "location" {
  description = <<-EOT
    The Azure region. Default is centralus (Iowa) rather than northcentralus (Illinois, nearer the centre):
    Container Apps, Managed Redis and Azure SQL serverless are all available there, which is not true of
    every region. Check availability before changing it.
  EOT
  type        = string
  default     = "centralus"
}

variable "environments" {
  description = <<-EOT
    The environments to prepare. Each one gets a resource group, a user-assigned identity federated to this
    repository's GitHub environment of the same name, and role assignments scoped to that resource group and
    nothing wider. Deploys run only in CI or by a person (ADR 0008), so these identities are the only way in.
  EOT
  type        = set(string)
  default     = ["dev", "prod"]
}

variable "github_repository" {
  description = "owner/repo, as GitHub writes it. The federated credential's subject is built from this, so a workflow in any other repository cannot use these identities."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$", var.github_repository))
    error_message = "github_repository must look like owner/repo."
  }
}

variable "state_container_name" {
  description = "Blob container holding every environment's state file, one key per environment."
  type        = string
  default     = "tfstate"
}

variable "tags" {
  description = "Tags applied to everything bootstrap creates."
  type        = map(string)
  default     = {}
}
