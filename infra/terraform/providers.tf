provider "azurerm" {
  subscription_id = var.subscription_id

  # Left at the v5 default of "none". The identity CI runs as is Contributor on one resource group, which
  # does not carry the subscription-level right to register a resource provider. Bootstrap registers every
  # provider this module needs; see bootstrap/providers.tf.

  # Containers in the media account are created over the data plane, and that account has shared-key
  # access switched off.
  storage_use_azuread = true

  features {
    key_vault {
      # Never purge a vault or a secret as part of an apply. A `terraform destroy` of dev leaves the vault
      # recoverable, and in prod purge protection makes it mandatory anyway.
      purge_soft_delete_on_destroy          = false
      purge_soft_deleted_secrets_on_destroy = false
      recover_soft_deleted_key_vaults       = true
      recover_soft_deleted_secrets          = true
    }

    resource_group {
      # The resource group belongs to bootstrap and is read here as a data source, but if that ever changes,
      # fail rather than silently delete something that is still in the group.
      prevent_deletion_if_contains_resources = true
    }
  }
}
