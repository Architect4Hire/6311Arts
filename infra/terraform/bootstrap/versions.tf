# Pinned the same way the .NET side pins Aspire: one place, and the CLI and the provider move together
# (CLAUDE.md "Verify fast-moving APIs"). azurerm 5.x is a breaking change from 4.x — notably
# `resource_provider_registrations` now defaults to "none" and `azurerm_storage_container` takes
# `storage_account_id` rather than a name.
terraform {
  required_version = ">= 1.9.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.12"
    }
  }

  # Deliberately no backend block: bootstrap is what *creates* the state account, so it cannot store its
  # state in one. Its state stays local and gitignored (see README, "Bootstrap"). Everything it creates is
  # importable, so a lost bootstrap state costs an import, not a rebuild.
}
