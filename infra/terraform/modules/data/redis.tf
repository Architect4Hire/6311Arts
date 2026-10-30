# Redis (ADR 0009). Three things share it: the gateway's session tickets, the API's and Worker's HybridCache
# L2, and the cache invalidation channel both subscribe to (ADR 0036, ADR 0049).
#
# This is Azure Managed Redis, not Azure Cache for Redis. Creation of the Basic, Standard and Premium tiers
# of Azure Cache for Redis was blocked for existing subscriptions on 2026-10-01, ahead of retirement on
# 2028-09-30, so the cheap C0 instance the architecture document costed at $15-20/mo can no longer be
# created at all. Balanced_B0 is the smallest Managed Redis SKU; price it before the first apply.
resource "azurerm_managed_redis" "main" {
  name                = var.names.redis
  resource_group_name = var.resource_group_name
  location            = var.location

  sku_name = var.redis_sku_name

  high_availability_enabled = var.redis_high_availability_enabled
  public_network_access     = "Enabled"

  default_database {
    # Access keys on, because that is what the application can use today: Aspire's Redis integration hands
    # StackExchange.Redis a connection string, and Entra authentication to Redis needs a credential-aware
    # client (docs/infrastructure/README.md, "Gaps in the application"). The key is read from Key Vault by
    # each app's managed identity and never appears in a workflow or a container template.
    access_keys_authentication_enabled = true

    client_protocol = "Encrypted"

    # NoCluster, deliberately. The default OSSCluster shards keys across slots, and a multi-key operation
    # spanning slots fails; neither StackExchange.Redis as Aspire configures it nor HybridCache's L2 is
    # written for that. Changing this later recreates the database and drops every session.
    clustering_policy = "NoCluster"

    # Azure's default. It evicts only keys that carry a TTL, which is every cache entry - and also every
    # session ticket, so under memory pressure a signed-in visitor can be signed out rather than a cache
    # entry being dropped. Separating the two would need a second instance; it is called out in
    # docs/infrastructure/README.md as an open question against ADR 0009.
    eviction_policy = var.redis_eviction_policy

    # Persistence is what decides whether a restart signs everybody out. Append-only, written every second,
    # for prod; nothing for dev. The two frequencies are mutually exclusive, which is why one is always null.
    persistence_append_only_file_backup_frequency = var.redis_persistence == "aof" ? "1s" : null
    persistence_redis_database_backup_frequency   = var.redis_persistence == "rdb" ? "1h" : null
  }

  tags = var.tags
}
