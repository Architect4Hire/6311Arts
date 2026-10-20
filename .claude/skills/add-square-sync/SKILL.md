---
name: add-square-sync
description: Make a 6311arts CMS content type sync with a Square catalog object type — field-ownership table, SquareCatalogLink, mapping, outbound push handler, inbound pull, echo suppression and reconciler coverage.
---

# Add Square Sync to a Content Type

Read `.claude/rules/square.md` and `.claude/rules/outbox.md` first. Use this for `ShopItem` (artwork), `ClassOffering` / `ClassSession`, `MembershipPlan`, and any future synced type. Merch is POS-only and never gets a CMS type (ADR 0032).

## 1. Ownership table (write it in the plan, get approval)

| CMS field | Square field | Owner | Direction | Notes |
|---|---|---|---|---|
| … | … | CMS / Square | push / pull / once-on-create / command | … |

- **SKU** and session **capacity** are CMS-owned. SKU is pushed and hashed.
- **Price and stock** for artwork and classes: **once-on-create**, then **command** (ADR 0032). Square holds the value, and only the pull writes the projection.
- **Membership plan price and cadence:** once-on-create only (ADR 0005).

If a field can't be classified, stop and ask.

## 2. Mapping (in `Arts6311.Square`)

- CMS snapshot record → Square catalog object(s): `ITEM` + `ITEM_VARIATION`(s), `IMAGE`, `CATEGORY`, or `SUBSCRIPTION_PLAN` + `SUBSCRIPTION_PLAN_VARIATION`.
- Write the CMS id as a catalog custom attribute on the object.
- Map the SKU to the variation's `sku`. Never set tax ids.
- Compute a content hash over the CMS-owned fields only (SKU included; price and stock never).
- Write the reverse map too: Square object → the Square-owned projection fields.
- Map money in one place.

## 3. Outbound (CMS → Square)

1. The CMS publish/update/archive DataLayer stages `square.catalog-push-requested` (or `-delete-requested`) with `{entityType, entityId}`. Use `add-outbox-message`.
2. The handler reloads the entity and its `SquareCatalogLink` through the `SquareSync` facade, then:
   - **Hash unchanged:** done.
   - **No link:** create with starting price/stock (stock at the configured location).
   - **Has a link:** upsert CMS-owned fields onto the **current** Square object (retrieve, merge, upsert with the last version). Keep Square's current price; the content push never changes it.
   - **Square call:** a batch upsert with idempotency key = outbox message id. Upload images first and reference their ids.
3. On success, update the link: object and variation ids, version, hash, `Synced`, `LastPushedAt`. On a version conflict, re-read, re-merge and retry once in the same attempt. On the last attempt, set `NeedsAttention`.

## 4. Inbound (Square → CMS)

The handled webhook types are `catalog.version.updated`, `inventory.count.updated`, and subscription events for plans. Use `add-webhook-handler`.

1. Find links by Square id, or by the custom attribute.
2. Update **only** Square-owned projection fields.
3. Echo suppression: our own version/hash means record and stop.
4. Object with no CMS id: POS-only, so skip it. Linked object deleted: set Archived + NeedsReview.
5. Mark any `Pending` `SquareChangeRequest` whose requested value now shows in Square as `Applied`.
6. Stage `site.revalidate-requested` for affected pages in the same save.

## 5. Price and stock commands (artwork and classes, ADR 0032)

Each is an endpoint (`add-endpoint`: `CmsEditor`, `Idempotency-Key`, audited). It writes a `SquareChangeRequest` and stages one message in the same save (`add-outbox-message`).

- **Change price** `{newPriceCents, expectedPriceCents}` → `square.price-change-requested`. The handler retrieves the variation: if the current price ≠ expected, `Rejected`; otherwise it does a full-object upsert with the version. On timeout, read back.
- **Set quantity** `{quantity}` → `square.inventory-change-requested` (`PHYSICAL_COUNT`, `occurred_at` = submission time, configured location). Not for one-of-a-kind items.
- **Capacity change** (a session save) → `square.inventory-change-requested` (`ADJUSTMENT` by the difference). Refuse a reduction below `SeatsRemaining`; the handler re-checks and sets `NeedsAttention` rather than go negative.

## 6. Reconciler coverage

Add the type to the reconciler's comparer:

- what counts as drift
- how each kind of drift is corrected (pull the Square-owned fields, or re-stage a push)
- the audit row written per correction
- stale `Pending` change requests (resolve by reading Square)
- objects with no CMS id are skipped

## 7. Admin

The edit screen shows:

- the SKU (editable)
- the current Square price and quantity, each with a **Change** action that opens the command and shows its pending/applied/rejected state (the value itself updates only when the pull lands)
- a sync status chip, the last error code, and a Retry button (stages a new push, Admin/Editor)

Before the first sync, the starting price and quantity are plain editable fields.

## 8. Tests (against `Arts6311.SquareFake`; sandbox opt-in)

- create
- content update
- image change
- archive
- no-op skip on an unchanged hash
- version conflict
- timeout-after-send reconciliation
- price changed in Square shows on the site
- stock goes to 0, so sold out
- echo ignored
- object with no CMS id ignored
- SKU change pushed; duplicate SKU refused
- price change applied; stale `expectedPriceCents` rejected; timeout-after-send read back
- set quantity racing a sale (the sale still counts)
- capacity reduction below seats remaining refused
- drift corrected by the reconciler
- CMS write model can't carry Square-owned projections

## Checklist before done

- [ ] Ownership table approved and mirrored in code comments on the mapper
- [ ] Square ids only on `SquareCatalogLink`; CMS id on the Square custom attribute
- [ ] Push handler: reload, hash skip, idempotency key = message id, version-aware merge, NeedsAttention on the last attempt
- [ ] Pull path updates Square-owned projections only; echo suppressed; no-CMS-id objects skipped; revalidation staged
- [ ] Price/quantity commands: change request + outbox in one save, compare-and-set on price, dated physical count, idempotency key = message id
- [ ] Reconciler covers the type
- [ ] Admin shows SKU, current Square values with Change commands and their state, status chip and Retry
- [ ] Contract tests pass on the fake; `@integration-reviewer` and `@outbox-reviewer` run
