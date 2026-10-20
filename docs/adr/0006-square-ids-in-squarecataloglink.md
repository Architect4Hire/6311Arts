# 0006 — Square ids in `SquareCatalogLink`, not entities

- **Status:** Accepted; the created-in-Square rule is superseded by [0032](0032-website-items-managed-from-the-cms.md)
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/square.md`, `.claude/rules/external.md`, `.claude/rules/backend.md` (module boundaries)

## Context

Content entities (shop items, classes, plans) need a pairing with Square catalog objects and variations, the last known Square version, a content hash for no-op detection, a sync status and timestamps. Putting provider ids and sync state on core entities ties the content model to one provider and spreads sync concerns across every module.

## Decision

- **Square identifiers and sync state never go on core entities.** They live in integration records owned by the `SquareSync` module:
  - **`SquareCatalogLink`:** entity type, entity id, Square object id, variation ids, last known Square version, content hash, sync status, last error code, last pushed and pulled timestamps.
  - **`SquareSyncCursor`:** the catalog `begin_time` cursor.
  - **`SquareWebhookEvent`:** the inbox.
- Content entities keep only read-only **projections** of Square-owned values (ADR 0005).
- **The CMS id is written onto the Square object as a catalog custom attribute,** so either side can find its pair.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Integration records in `SquareSync` (chosen)** | Content stays provider-neutral; sync logic lives in one module. Costs a join or link lookup per synced entity. |
| `SquareId` / `SyncState` columns on content entities (v1 plan) | Simpler queries, but provider coupling in every module, migrations whenever sync state changes, and it breaks `external.md`. |
| Rely only on the Square custom attribute (no local link) | No link table, but every lookup needs a Square call, and there's nowhere local for version, hash and status. |

## Consequences

- Echo suppression and no-op skipping compare against `SquareCatalogLink.version` and the content hash.
- ~~Objects created in Square with no CMS id become Draft records flagged "created in Square".~~ Superseded by 0032: they are POS-only and ignored. Linked objects deleted in Square set the CMS record to Archived + NeedsReview.
- The custom-attribute mechanism must be confirmed against Square's catalog docs, and cited, when the gateways are built (Phase 5).
