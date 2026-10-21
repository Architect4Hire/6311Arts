---
paths:
  - src/Arts6311.Square/**
  - src/Arts6311.SquareFake/**
  - src/Arts6311.Domain/Modules/SquareSync/**
  - src/Arts6311.Domain/Modules/Shop/**
  - src/Arts6311.Domain/Modules/Classes/**
  - src/Arts6311.Domain/Modules/Memberships/**
  - src/Arts6311.Domain/Modules/Commerce/**
  - src/Arts6311.Domain/Modules/Members/**
---
# Square Integration

Square is the system of record for money. The CMS is the system of record for content. The two stay in step through the outbox (`outbox.md`) and a reconciler. Square's own docs warn that two-way catalog sync is risky, so the guardrails below are not optional.

## Website catalog (ADR 0032)

- **Artwork (`ShopItem`) and classes (`ClassOffering` / `ClassRun`, ADR 0044)** are created in the CMS and pushed to Square. Staff never manage them in Square.
- **Merch is POS-only.** It has no CMS type, no site page and no import mapping.
- **Square objects with no CMS id custom attribute are POS-only.** Inbound handlers and the reconciler ignore them (no drafts, no audit noise).
- **Taxes are Square's.** Never send tax ids or tax settings.

## Field ownership

| CMS-owned → pushed on every content push | Square holds the record → pulled into projections | CMS *requests* a change (command → outbox) |
|---|---|---|
| Name, description, artist, categories, images, publish/archive state | Price | Artwork and class run price: **change price** (compare-and-set) |
| **SKU** (per `ShopItem`, per `ClassRun`; unique across the CMS through the kernel's `CatalogSkus` registry, ADR 0037) | Stock, seats remaining, sold-out | Artwork quantity: **set quantity** (`PHYSICAL_COUNT` dated to the request) |
| Class details, run instructor, session dates/times, **run capacity** | Orders, payments, refunds, taxes, customers, cards, subscriptions, member status | Capacity change → seat `ADJUSTMENT` by the difference |
| Membership plan name, perks, class discount, featured flag, order, **and the starting price and cadence the first push creates the plan with** | Plan pricing and billing cadence after creation | — (ADR 0005 still applies to plans: there is no change-price command for a plan) |

- **Before the first sync,** the starting price, quantity, capacity and (for a membership plan) billing cadence are draft fields, and the first push creates the Square objects with them. After the item is linked, they're frozen. Until `SquareCatalogLink` exists (5.1), "linked" is read from the projections themselves: a record whose price Square has reported is past its first sync (`startingPriceEditable`, ADR 0037, 0040).
- **Square-owned projection columns** (`PriceCents`, `StockOnHand`, `SeatsRemaining`, `SoldOut`, a plan's `PriceCents` and `Cadence` — the latter a string of Square's own word, ADR 0040) are written only by the inbound pull through the `SquareSync` facade. A command never writes them, and the content push and the content hash never include price or stock.
- **Every command** is a `SquareChangeRequest` row (`Pending` / `Applied` / `Rejected` / `NeedsAttention`) plus one outbox message in the same save. It requires `CmsEditor`, an `Idempotency-Key`, and an audit row.
- **Change price** carries `expectedPriceCents`. If Square's current price differs, the request is `Rejected`. Never overwrite a price someone else set.
- **Set quantity** is a `PHYSICAL_COUNT` with `occurred_at` = submission time, so later sales still subtract ([How Inventory API works](https://developer.squareup.com/docs/inventory-api/how-it-works)). One-of-a-kind artwork is created at 1 and has no set-quantity command.
- **A capacity reduction** larger than `SeatsRemaining` is refused. The handler re-reads the count and sets `NeedsAttention` rather than drive seats negative. Never recompute seats from orders.
- **Inventory is per location.** The location id is per-environment configuration (`Square:LocationId`), never hardcoded.

## Boundaries

- **Only `Arts6311.Square` references the Square SDK.**
  - It implements narrow, application-owned gateway interfaces declared in the Domain: `ISquareCatalogGateway`, `ISquareInventoryGateway`, `ISquareOrdersGateway`, `ISquarePaymentsGateway`, `ISquareCustomersGateway`, `ISquareSubscriptionsGateway`.
  - SDK DTOs are mapped to Domain records inside that assembly.
  - Money mapping (`Money.Amount` minor units ↔ `long` cents) lives in exactly one place.
- **Square identifiers never go on core entities.** They live in integration records in the `SquareSync` module:
  - `SquareCatalogLink`: entity type, entity id, Square object id, variation ids, last known Square version, content hash, sync status, last error code, last pushed/pulled timestamps
  - `SquareSyncCursor`: the catalog `begin_time` cursor
  - `SquareWebhookEvent`: the inbox
- **The CMS id is written onto the Square object as a catalog custom attribute**, so either side can find its pair.
- **Each environment uses its own credentials:**
  - **Local:** `square-mode` = `fake` (default, offline) or `sandbox`.
  - **Production:** credentials exist only in the prod Key Vault.
  - **No run-mode path can reach Square production.**

## Writes (outbound)

- **Every write carries an idempotency key** equal to the outbox message `Id`. A retry with the same key is safe; a new key is a new operation.
- **Batch.** Use `BatchUpsertCatalogObjects` and batch inventory changes. Never list the whole catalog on a schedule.
- **Skip no-op pushes** whose content hash matches `SquareCatalogLink`. The hash covers CMS-owned fields (SKU included), never price or stock.
- **Send the last known version.** On a version conflict, re-read, re-merge the CMS-owned fields onto Square's current object and retry inside the same handler attempt.
- **An ambiguous outcome** (timeout after send) is reconciled by reading the object back before any second create.
- **Archive policy** is recorded in an ADR (delete vs. mark unavailable). Never hard-delete CMS content because of a Square delete.

## Reads (inbound)

- **Verify the webhook signature before parsing.** It's HMAC-SHA256 over *notification URL + raw body*, compared in constant time. The notification URL is the public gateway URL from configuration and must match what Square signs.
- **Never trust webhook payload state.** Use ids and timestamps only, then fetch current state:
  - catalog changes since the cursor via `SearchCatalogObjects`
  - batch-retrieve inventory counts
  - retrieve the subscription
- **Update only Square-owned projections,** and resolve any matching `Pending` `SquareChangeRequest`. Advance the cursor only after the batch commits.
- **Echo suppression.** A change whose version or hash equals our last push is ours. Record it and do nothing.
- **Objects with no CMS id** (merch and anything else made in Square) are POS-only. Skip them.
- **Linked objects deleted in Square** set the CMS record **Archived + NeedsReview**.

## Payments and members

- **The browser tokenizes cards with the Web Payments SDK.** Only the source token reaches the API, and it's used once and never logged or stored.
- **Amounts are never accepted from the client.** Quote and pay re-price through Square from catalog object ids.
- **Checkout and membership signup are idempotent commands.** They take an `Idempotency-Key` header via the Idempotency kernel.
- **Member status changes only from Square** (subscription and invoice webhooks or reads), never from what the signup endpoint assumed.

## Reconciliation

- **Every 15 minutes:** compare recently changed links.
- **Nightly:** full compare.
- **What it does:**
  - fixes drift on Square-owned projections
  - re-stages pushes that never landed
  - resolves stale `Pending` change requests by reading Square (Applied if Square shows the requested value, otherwise `NeedsAttention`)
  - writes an audit row per correction
- **What it never does:** delete CMS content, touch objects with no CMS id, or write anything to Square without an outbox message.

## Testing

- **Every gateway runs the same contract test suite** against `Arts6311.SquareFake` (always) and the Square sandbox (opt-in via an environment flag).
- **Required scenarios:**
  - create
  - content update
  - image change
  - archive
  - version conflict
  - timeout after send
  - duplicate webhook
  - out-of-order webhooks
  - bad signature
  - echo of our own push
  - drift corrected by the reconciler
  - price change applied, and rejected on a stale `expectedPriceCents`
  - set quantity racing a sale (the sale still counts)
  - capacity reduction below seats remaining refused
  - object created in Square without a CMS id ignored
