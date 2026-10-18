---
name: add-square-sync
description: Make a 6311arts CMS content type sync with a Square catalog object type — field-ownership table, SquareCatalogLink, mapping, outbound push handler, inbound pull, echo suppression and reconciler coverage.
---

# Add Square Sync to a Content Type

Read `.claude/rules/square.md` and `.claude/rules/outbox.md` first. Use this for `ShopItem`, `ClassOffering` / `ClassSession`, `MembershipPlan`, and any future synced type.

## 1. Ownership table (write it in the plan, get approval)

| CMS field | Square field | Owner | Direction | Notes |
|---|---|---|---|---|
| … | … | CMS / Square | push / pull / once-on-create | … |

Price and stock/capacity are **once-on-create** from the CMS, and Square-owned after that. If a field can't be classified, stop and ask.

## 2. Mapping (in `Arts6311.Square`)

- CMS snapshot record → Square catalog object(s): `ITEM` + `ITEM_VARIATION`(s), `IMAGE`, `CATEGORY`, or `SUBSCRIPTION_PLAN` + `SUBSCRIPTION_PLAN_VARIATION`.
- Write the CMS id as a catalog custom attribute on the object.
- Compute a content hash over the CMS-owned fields only.
- Write the reverse map too: Square object → the Square-owned projection fields.
- Map money in one place.

## 3. Outbound (CMS → Square)

1. The CMS publish/update/archive DataLayer stages `square.catalog-push-requested` (or `-delete-requested`) with `{entityType, entityId}`. Use `add-outbox-message`.
2. The handler reloads the entity and its `SquareCatalogLink` through the `SquareSync` facade, then:
   - **Hash unchanged:** done.
   - **No link:** create with starting price/stock.
   - **Has a link:** upsert CMS-owned fields onto the **current** Square object (retrieve, merge, upsert with the last version).
   - **Square call:** a batch upsert with idempotency key = outbox message id. Upload images first and reference their ids.
3. On success, update the link: object and variation ids, version, hash, `Synced`, `LastPushedAt`. On a version conflict, re-read, re-merge and retry once in the same attempt. On the last attempt, set `NeedsAttention`.

## 4. Inbound (Square → CMS)

The handled webhook types are `catalog.version.updated`, `inventory.count.updated`, and subscription events for plans. Use `add-webhook-handler`.

1. Find links by Square id, or by the custom attribute.
2. Update **only** Square-owned projection fields.
3. Echo suppression: our own version/hash means record and stop.
4. Unknown object: create a Draft flagged `CreatedInSquare`. Deleted object: set Archived + NeedsReview.
5. Stage `site.revalidate-requested` for affected pages in the same save.

## 5. Reconciler coverage

Add the type to the reconciler's comparer:

- what counts as drift
- how each kind of drift is corrected (pull the Square-owned fields, or re-stage a push)
- the audit row written per correction

## 6. Admin

The edit screen shows Square-owned values read-only ("Managed in Square"), a sync status chip, the last error code, and a Retry button (stages a new push, Admin/Editor).

## 7. Tests (against `Arts6311.SquareFake`; sandbox opt-in)

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
- created-in-Square → Draft
- drift corrected by the reconciler
- CMS write model can't carry Square-owned fields

## Checklist before done

- [ ] Ownership table approved and mirrored in code comments on the mapper
- [ ] Square ids only on `SquareCatalogLink`; CMS id on the Square custom attribute
- [ ] Push handler: reload, hash skip, idempotency key = message id, version-aware merge, NeedsAttention on the last attempt
- [ ] Pull path updates Square-owned fields only; echo suppressed; revalidation staged
- [ ] Reconciler covers the type
- [ ] Admin shows read-only Square values + status + retry
- [ ] Contract tests pass on the fake; `@integration-reviewer` and `@outbox-reviewer` run
