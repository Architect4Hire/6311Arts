# Media and the Data Protection key ring. The containers are the same two the AppHost declares for Azurite,
# so a deployment and a laptop see the same names (.claude/rules/aspire.md).

resource "azurerm_storage_account" "main" {
  name                = var.names.storage_account
  resource_group_name = var.resource_group_name
  location            = var.location

  account_kind             = "StorageV2"
  account_tier             = "Standard"
  account_replication_type = var.storage_replication_type

  https_traffic_only_enabled = true
  min_tls_version            = "TLS1_2"

  # No public blobs, ever. Media is delivered by the API under /media/**, through the gateway, which is the
  # only public origin (ADR 0001, ADR 0030).
  allow_nested_items_to_be_public = false

  # No shared keys: the API, Worker and migrations job read and write with their own managed identities,
  # and the connection string handed to them is a service URI, not a credential.
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true

  public_network_access = "Enabled"

  blob_properties {
    delete_retention_policy {
      days = var.storage_blob_retention_days
    }

    container_delete_retention_policy {
      days = var.storage_blob_retention_days
    }
  }

  tags = var.tags

  lifecycle {
    # This account holds the only copy of every uploaded artwork image and the Data Protection key ring.
    # Losing the key ring invalidates every emailed confirmation and reset link in flight (ADR 0023).
    prevent_destroy = true
  }
}

resource "azurerm_storage_container" "media" {
  name                  = "media"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

resource "azurerm_storage_container" "dataprotection" {
  name                  = "dataprotection"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}
