# 0032 — Website items are managed from the CMS; merch stays in Square POS

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/square.md`, `.claude/rules/content.md`, `.claude/rules/frontend.md`,
  `.claude/rules/import.md`, `.claude/rules/design-system.md`
- **Supersedes:** [0005](0005-square-field-ownership.md) for the price and stock of artwork and classes;
  [0006](0006-square-ids-in-squarecataloglink.md) for objects created in Square

## Context

ADR 0005 has staff change prices and stock in Square after an item is first created. The goal now is that
staff never open Square to manage what the website sells. They create an item in the CMS (including its
SKU), set its price and quantity there, and it appears in Square and on the site.

Two things narrow the problem:

- **Merch (6311arts-branded goods) is sold only at the front desk** through Square POS. It doesn't appear on
  the website or in the CMS.
- **Taxes are a Square concern.** They're configured and applied in Square.

So the website catalog is **artwork** (`ShopItem`: the shop's prints, jewelry, ceramics, books and
one-of-a-kind pieces) and **classes** (`ClassOffering` / `ClassSession`). Front-desk POS still sells those
items too, so Square sales keep moving stock and seats whatever the CMS does.

Square stays the system of record for money and stock. Its catalog takes only full-object updates guarded
by a version ([Update catalog objects](https://developer.squareup.com/docs/catalog-api/update-catalog-objects)).
Its inventory count is the latest physical count plus every adjustment whose `occurred_at` is later, ordered
by `occurred_at` rather than arrival
([How Inventory API works](https://developer.squareup.com/docs/inventory-api/how-it-works)).

## Decision

### Scope of the website catalog

- **Artwork and classes are created in the CMS and pushed to Square.** No website item is created in Square.
- **Merch has no CMS type, no site page and no import mapping.** It lives only in Square POS.
- **Objects created in Square with no CMS id are POS-only.** Inbound handlers and the reconciler ignore them.
  They never become CMS drafts. This replaces the "created in Square → Draft" rule in 0005/0006, and the
  `CreatedInSquare` sync status is removed.
- **A linked object deleted in Square** still sets the CMS record to Archived + `NeedsReview` (unchanged).
- **The CMS never sends tax ids or tax settings.** Square applies its own tax configuration.
- **Membership plans keep 0005 unchanged** (price and cadence once at creation). Whether the CMS can request
  plan price changes is **DECIDE** in 5.10, because Square subscription plan variations constrain pricing changes.

### Field ownership for artwork and classes

| Field | Owner | How it moves |
|---|---|---|
| Name, description, artist, categories, images, publish/archive, session dates | CMS | pushed on every content push |
| **SKU** (one per `ShopItem`; one per `ClassSession`) | CMS | pushed on every content push; part of the content hash |
| Session **capacity** | CMS | a change stages a seat adjustment by the difference |
| **Price** | Square (record); CMS (requests) | starting value on create; afterwards a *change-price* command |
| **Quantity on hand** (artwork) | Square (record); CMS (requests) | starting value on create; afterwards a *set-quantity* command |
| Seats remaining, sold out, orders, payments | Square | pulled only |

"Square owns the record" means the site and checkout show and charge only what Square holds, and only the
inbound pull writes the projection columns (`PriceCents`, `StockOnHand`, `SeatsRemaining`, `SoldOut`).
The CMS *asks* for a change; it never writes the projection.

### SKU

- A CMS field on `ShopItem` and on each `ClassSession`, unique across the CMS (database unique index).
- The CMS suggests one when the record is created (format set in 2.9/2.10), and staff may edit it.
- Square doesn't enforce SKU uniqueness, so the CMS does.

### Before and after the first sync

- **Before the item is linked** (`SquareCatalogLink` absent): the starting price, starting quantity and session
  capacity are ordinary draft fields. The first push creates the Square objects with them.
- **After the item is linked:** the starting fields are frozen, and price and quantity change only through
  the commands below.

### Commands (after the first sync)

Each command is an endpoint through the full seam:

- `CmsEditor` policy, `Idempotency-Key` required, audited.
- It records a `SquareChangeRequest` row in `SquareSync`: kind, entity, variation, requested value, expected
  value, `occurred_at`, status `Pending` / `Applied` / `Rejected` / `NeedsAttention`, outbox message id.
- It stages one outbox message in the same save. The Square call uses idempotency key = outbox message id.

- **Change price** `{ newPriceCents, expectedPriceCents }` → `square.price-change-requested`.
  - The handler retrieves the variation and **compares and sets**.
  - Square's current price ≠ `expectedPriceCents` → `Rejected` ("price changed since you loaded it"). The
    handler never overwrites a price someone else set.
  - A match → full-object upsert with the last known version. A version conflict is re-read and re-checked
    once, inside the same attempt.
  - Timeout after send → read back. If the price already equals `newPriceCents`, it's `Applied`.
- **Set quantity** (artwork that isn't one-of-a-kind) `{ quantity }` → `square.inventory-change-requested`.
  - The handler sends a `PHYSICAL_COUNT` at the configured location, with `occurred_at` = when staff
    submitted the command (recorded on the request).
  - A sale whose `occurred_at` is later still subtracts, so a count can't erase a sale that raced it.
- **One-of-a-kind artwork** is created with quantity 1, and it has no set-quantity command.
- **Session capacity change** → `square.inventory-change-requested` as an `ADJUSTMENT` of
  `newCapacity − oldCapacity`, staged in the same save as the capacity field.
  - A reduction larger than the current `SeatsRemaining` is refused (409).
  - The handler re-reads the count before it applies a reduction, and sets `NeedsAttention` rather than drive
    seats negative.
  - Seats are never recomputed locally from orders.

### Location

- Inventory is per location. The Square location id is per-environment configuration (`Square:LocationId`).
  It isn't hardcoded and it isn't a secret.
- The fake supplies its own.

## Options considered

| Option | Cost / trade-off |
|---|---|
| Keep 0005 (price and stock changed in Square) | No write conflicts, but staff need Square for every website price or stock change. That's the thing we want to remove. |
| CMS owns price and stock as fields, and every push overwrites Square | One editing place, but an absolute overwrite erases front-desk sales that land between the edit and the push, and silently reverses any change made in Square. |
| **CMS commands; Square stays the record (chosen)** | Staff never open Square for website items. Compare-and-set on price and physical counts dated to the request keep concurrent sales and edits safe. Costs a request table, two message types, and a pending state in the admin. |
| Delta-only stock edits ("receive +N") for artwork | Never conflicts, but staff think "I have 6 prints", not "+2 prints". A physical count dated to the request gives the absolute UX with the same safety. |

## Consequences

- **Allowed now:** a CMS path may *request* a Square-owned value change, through a `SquareChangeRequest` and
  an outbox message. **Still forbidden:** writing a Square-owned projection column from any CMS path, or
  putting price or stock in the content hash or the content push.
- The admin shows each Square-owned value with its pending request, if there is one: "Changing $40 → $45…",
  then Applied or Rejected with the reason. The projection updates only when the inbound pull lands.
- The reconciler also resolves `Pending` requests older than the retry window by reading Square: Applied if
  Square shows the requested value, otherwise `NeedsAttention`.
- Inbound and reconciler code must check for the CMS id custom attribute before doing anything with an object.
- Prompts 2.9, 2.10, 5.1, 5.4, 5.7, 5.9, 5.11, 5.12 and 5.20 and import mapping I.7 change accordingly. New
  prompts 5.4a and 5.4b add the commands.
