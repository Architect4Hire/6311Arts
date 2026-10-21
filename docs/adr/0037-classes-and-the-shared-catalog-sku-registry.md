# 0037 — Classes: the shared catalog SKU registry and session-level projections

- **Status:** Accepted (amended by [0044](0044-class-runs-with-their-own-instructor-and-seats.md): the run, not the session, holds the SKU, capacity, starting price and projections)
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/square.md`, `.claude/rules/api-contract.md`
- **Builds on:** [0031](0031-artists-and-the-shared-content-lifecycle.md) (the shared content lifecycle),
  [0032](0032-website-items-managed-from-the-cms.md) (CMS-managed website items),
  [0034](0034-html-sanitizer-and-pages.md) (the sanitizer), [0036](0036-revalidation-scheduled-publishing-and-previews.md)
  (revalidation, scheduled publishing, previews)

## Context

Prompt 2.9 adds the third content type with children (`ClassOffering` → `ClassSession`) and the first one that is
Square-synced. Three things in ADR 0032 need a mechanism before any Square code exists (5.x):

- **A SKU is "unique across the CMS"** — one per `ClassSession` *and* one per `ShopItem` (2.10). SQL Server has no
  unique index spanning two tables, so uniqueness across types needs somewhere to live.
- **Price, seats remaining and sold-out are Square's record.** They are projection columns the CMS never writes,
  but the CMS still needs a *starting* price before the first push creates the Square objects.
- **The starting fields freeze once the item is linked.** There is no `SquareCatalogLink` table until 5.1, so the
  admin needs a signal it can read today.

## Decision

### One SKU registry in the shared kernel

`Managers/Catalog/` owns a `CatalogSkus` table: one row per SKU in the CMS, with the owner's type
(`class-session`, later `shop-item`) and id.

- The SKU is the primary key, so **the database enforces uniqueness across every type that registers one**.
- `(OwnerType, OwnerId)` is unique too: one SKU per session, one per shop item.
- `ISkuRegistry` stages its reservations and releases on the ambient `ArtsDbContext`, exactly as
  `IOutboxWriter.Enqueue` and `IAuditWriter.Record` do, so a SKU is claimed in the **same save** as the row that
  carries it. Nothing is reserved by a write that fails.
- **A release names its owner and reads the row first.** Deleting a row that is gone would fail the whole save as a
  concurrency conflict — which the editor would read as "someone else saved first" rather than as registry drift —
  and a release that ignored the owner would delete a reservation another record has since taken. So a release that
  does not match is simply not staged.
- `Reserve` refuses a SKU outside `SkuPolicy.IsValid`: the registry is the one guard on the shape, so a bad SKU is a
  defect there rather than a 500 from the database at the caller's save.
- The owning row keeps its own `Sku` column. Reads, the content hash and the 5.x Square push all need the SKU on
  the entity they are already holding; a registry-only SKU would put a join in every one of those paths. The
  module's DataLayer is the single writer of both, and `ClassesModuleTests` proves the pair cannot drift.
- The kernel names owner types as **strings**, so it still depends on no module (`backend.md`).

A clash is `cms.sku_taken` → 409 (`api-contract.md`), raised by Business when it sees the registry row and, for a
race, by the DataLayer when the insert fails.

### Suggested SKUs

Each offering gets an `OfferingNumber`, assigned at create as the next number after the highest in use, and a
session's SKU is suggested as `CLS-<offering number>-<n>` (`ART-<n>` for 2.10's artwork). Staff may edit it; a
session written without one takes the next suggestion that is free.

The number is a **suggestion aid, not an identity**: two creates racing can take the same number, which costs one
rejected SKU suggestion and nothing else, because the registry is the only guard that matters.

### Session-level projections, offering-level starting price

- `PriceCents`, `SeatsRemaining` and `SoldOut` are read-only columns on `ClassSession`: each session is its own
  Square variation with its own SKU, so each carries its own price and seat count (ADR 0032).
- `StartingPriceCents` is the offering's: one price for the course, which the first push creates every session's
  variation with. Write ViewModels contain none of the projections, and `ClassWriteModelTests` proves it by
  reflection.

### "After the first sync" before SquareSync exists

The admin edits the starting price while **no session has a Square record yet** — that is, while every session's
`PriceCents` *and* `SeatsRemaining` are null — and shows the projection with a disabled placeholder for the 5.4a
change-price action once Square has reported either. The ServiceModel carries that as `startingPriceEditable`, and
each session carries `squareHasRecord`.

The projection is written only by the inbound pull, so "Square has told us a price" is a fact the CMS already holds
— no link state is invented and nothing has to be maintained until 5.1 replaces the signal with the real link.

### The rest of the slice

- **Sessions are part of the offering's aggregate:** one `ETag`, replaced as a set by `PATCH` (as an exhibition's
  credits are), ordered by start time. A session whose projections are populated cannot be removed — it exists in
  Square and may have sales.
- **Capacity is CMS-owned** and a plain field today, editable even after the first sync (ADR 0032: a change stages a
  seat adjustment by the difference). A reduction below the seats already taken (`Capacity − SeatsRemaining`) is
  refused, and the refusal is checked before anything is mutated; 5.4a moves it to the seat-adjustment command.
- **The body is sanitized HTML** with a `ClassOfferingMediaReferences` table, as Pages (ADR 0034) — a new field
  with no legacy, and the WordPress class descriptions the importer brings in 4.x are HTML.
- **Each session stores its display time zone** (`backend.md`), defaulting to the gallery's, so an online session
  can be shown in the zone it is advertised in.
- `SitePaths.Classes` is `/classes/`, and `class-offering` joins `PreviewPolicy.PathPrefixes`.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Shared `CatalogSkus` registry, SKU also on the row (chosen)** | One kernel table and one write path that keeps two copies in step. Uniqueness is the database's, and 2.10 joins by registering a second owner type. |
| Registry is the only store | One source of truth, but every read, the content hash, the Square push and each row of the admin's session grid gain a join into a kernel table. |
| Cross-table check in Business | No schema at all, but two concurrent creates can both take a SKU, which is exactly what ADR 0032 says the CMS must prevent. |
| SKU unique per type only | Cheap, but a class session and a print could share a SKU, and Square — which enforces nothing — would hold two objects with one SKU. |
| A `squareLinked` flag on the ServiceModel, false until 5.1 | The final contract shape arrives early, but the API would report a state no code maintains. |

## Consequences

- **A new catalog type registers an owner type and reserves through `ISkuRegistry`;** it never writes `CatalogSkus`
  itself, and it never puts a SKU on a row without a reservation in the same save.
- **Still forbidden:** writing `PriceCents`, `SeatsRemaining` or `SoldOut` from any CMS path, and letting any of
  them into a write ViewModel or a content hash.
- 2.10 adds `shop-item` to `CatalogOwnerTypes` and reuses the registry, the error code and the suggestion helper.
- 5.1 replaces `startingPriceEditable`'s derivation with the `SquareCatalogLink`, and 5.4a replaces the capacity
  guard with the seat-adjustment command.
- **The per-session sync status chip waits for 5.1.** `frontend.md` asks for Synced / Pending / Needs attention
  beside each Square-held value, and there is no `SyncStatus` to show until `SquareCatalogLink` exists. Until then a
  session reads "Not in Square yet" or shows Square's own figures.
- **An edit to a published class stages no revalidation,** the same gap Artists, Exhibitions and Pages have, which
  ADR 0036 leaves open. It matters more here, because a session's dates feed the site's "what's on" lists: the API's
  own copy still clears within `HybridAppCache.MaxLocalLifetime`, but nothing clears the shared L2 until a status
  command. Whoever closes 0036's open decision closes it for all four.
