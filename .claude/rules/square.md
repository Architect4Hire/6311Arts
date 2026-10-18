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

## Field ownership (fixed)

| CMS-owned → pushed to Square | Square-owned → pulled into the CMS (read-only in admin) |
|---|---|
| Name, description, artist, categories, images, publish/archive state | Price after creation |
| Class details, session dates/times | Stock, seats remaining, sold-out |
| Membership plan name/description/perks | Plan pricing and billing cadence after creation |
| | Orders, payments, refunds, customers, cards, subscriptions, member status |

The CMS sets a **starting price** and **initial stock or capacity** exactly once, when an item is first created in Square. After that:

- Price changes happen in Square.
- Capacity changes go through an explicit, audited "adjust seats" command that sends an inventory adjustment.

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
- **Skip no-op pushes** whose content hash matches `SquareCatalogLink`.
- **Send the last known version.** On a version conflict, re-read, re-merge the CMS-owned fields onto Square's current object and retry inside the same handler attempt.
- **An ambiguous outcome** (timeout after send) is reconciled by reading the object back before any second create.
- **Archive policy** is recorded in an ADR (delete vs. mark unavailable). Never hard-delete CMS content because of a Square delete.

## Reads (inbound)

- **Verify the webhook signature before parsing.** It's HMAC-SHA256 over *notification URL + raw body*, compared in constant time. The notification URL is the public gateway URL from configuration and must match what Square signs.
- **Never trust webhook payload state.** Use ids and timestamps only, then fetch current state:
  - catalog changes since the cursor via `SearchCatalogObjects`
  - batch-retrieve inventory counts
  - retrieve the subscription
- **Update only Square-owned fields.** Advance the cursor only after the batch commits.
- **Echo suppression.** A change whose version or hash equals our last push is ours. Record it and do nothing.
- **Objects created in Square** with no CMS id become **Draft** CMS records flagged "created in Square" for staff review.
- **Objects deleted in Square** set the CMS record **Archived + NeedsReview**.

## Payments and members

- **The browser tokenizes cards with the Web Payments SDK.** Only the source token reaches the API, and it's used once and never logged or stored.
- **Amounts are never accepted from the client.** Quote and pay re-price through Square from catalog object ids.
- **Checkout and membership signup are idempotent commands.** They take an `Idempotency-Key` header via the Idempotency kernel.
- **Member status changes only from Square** (subscription and invoice webhooks or reads), never from what the signup endpoint assumed.

## Reconciliation

- **Every 15 minutes:** compare recently changed links.
- **Nightly:** full compare.
- **What it does:**
  - fixes drift on Square-owned fields
  - re-stages pushes that never landed
  - writes an audit row per correction
- **What it never does:** delete CMS content, or write CMS-owned fields to Square without an outbox message.

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
