# 0056 — Azure Managed Redis, non-clustered, one instance per environment

- **Status:** Accepted
- **Date:** 2026-10-30
- **Rules affected:** `.claude/rules/aspire.md` (the `cache` resource), CLAUDE.md "Stack"
- **Related:** ADR 0009 (Redis for session tickets — this resolves its open Azure half), ADR 0036 (revalidation), ADR 0049 (cache invalidation bus), ADR 0055

## Context

ADR 0009 chose Redis and left how to host it in Azure open; `docs/architecture/architecture.md` costed it at
about $15-20/mo managed, meaning an Azure Cache for Redis `C0`, or a few dollars as a container app.

That option has since closed. Microsoft is retiring Azure Cache for Redis Basic, Standard and Premium:
creation was blocked for new customers on 2026-04-01, blocked for existing customers on **2026-10-01**, and
the service retires on **2028-09-30** with instances disabled from 2028-10-01. Azure Managed Redis is the
replacement. Three things share the instance: the gateway's session tickets, the API's and Worker's
HybridCache L2, and the cache invalidation channel both hosts subscribe to.

## Decision

- **Azure Managed Redis** (`azurerm_managed_redis`), `Balanced_B0`, one instance per environment, in both
  dev and prod. Not a container app: the gateway's session tickets live there, so losing the instance signs
  every member and every staff user out, and a container app makes persistence, upgrades and the restart
  story ours to own.
- **`clustering_policy = "NoCluster"`.** The service default, `OSSCluster`, shards keys across slots and
  fails a multi-key operation that spans them; neither StackExchange.Redis as Aspire configures it nor
  HybridCache's L2 is written for that. Changing this later recreates the database and drops every session.
- **Persistence by environment:** append-only, written every second, in prod; none in dev. A replica in
  prod, none in dev.
- **Access keys, for now.** The key is a Key Vault secret each app reads with its own managed identity.
  Entra authentication to Redis needs a credential-aware client, which Aspire's Redis integration does not
  configure, so it is an application change and is listed as a gap rather than done.
- **Eviction policy is a variable, defaulted to Azure's `VolatileLRU`, and the choice stays open.** See
  Consequences.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Azure Managed Redis `Balanced_B0` (chosen)** | The only managed option still creatable. Materially more than the retired `C0`, so the hosting estimate in the architecture document needs redoing. Managed persistence, patching and failover. |
| Azure Cache for Redis `C0` | What was costed, and no longer creatable in this subscription. Not an option. |
| Redis as a container app | A few dollars a month. But sessions then depend on a container we patch, restart and persist ourselves, and a restart signs everyone out. |
| Two instances, one for sessions and one for the cache | Removes the eviction conflict below. Doubles the Redis line for a site this size. |

## Consequences

- **The hosting cost estimate is stale.** `docs/architecture/architecture.md` §11 needs re-pricing against
  the calculator before the first apply; Redis is the largest change.
- **Sessions and cache entries compete for one memory budget, and eviction cannot distinguish them.**
  Managed Redis has a single database per instance. `VolatileLRU` evicts the least recently used key *with
  a TTL* — every cache entry, and every session ticket too — so memory pressure can sign a visitor out
  mid-checkout. `NoEviction` fails the write instead, breaking sign-in rather than caching. The default
  stands because it degrades gracefully, but it is **not settled**: `docs/infrastructure/README.md` §9
  carries it as an open question, and the fix is a second instance.
- **Changing the clustering policy or high availability later recreates the database.** Both drop every
  session. Decide them per environment at creation.
- **A migration path exists if an instance was created before this ADR:** Managed Redis supersedes the
  removed `azurerm_redis_enterprise_cluster` and `azurerm_redis_enterprise_database` resources, and
  Microsoft documents moving a cache endpoint across.
