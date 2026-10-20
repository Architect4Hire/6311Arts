# 0038 — Shop items: artwork only, a category allow-list, and the one-of-a-kind freeze

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/square.md`, `.claude/rules/frontend.md`
- **Builds on:** [0032](0032-website-items-managed-from-the-cms.md) (CMS-managed website items, merch stays in POS),
  [0033](0033-exhibitions-and-cross-module-artist-credits.md) (credits facade to facade),
  [0034](0034-html-sanitizer-and-pages.md) (the sanitizer),
  [0037](0037-classes-and-the-shared-catalog-sku-registry.md) (the shared SKU registry, projections, previews)

## Context

Prompt 2.10 adds `ShopItem`: the shop's artwork — prints, jewelry, ceramics, books and one-of-a-kind pieces —
created in the CMS and pushed to Square. Merch has no type here at all (ADR 0032).

It is the **second** owner type in the SKU registry ADR 0037 introduced, which is what makes "unique across the
CMS" testable rather than theoretical, and it raises three questions Classes did not.

- **Categories.** The spike's shop filters on a fixed, labelled set of six (`projects/site/src/app/pages/shop.ts`),
  and `square.md` has the CMS push categories to Square.
- **A gallery, not a hero.** Artwork has photographs, several of them, in an order someone chose.
- **One-of-a-kind.** ADR 0032 says such a piece is created with quantity 1 and has *no* set-quantity command, so
  the flag decides whether an item ever gets one.

## Decision

### Artwork, with its artist asked of Artists

- `ShopItem` is artwork only. There is no merch type, category or flag, and no import mapping for merch.
- The artist is **optional** (the spike's magazines have none) and is a `Restrict` foreign key to `Artist`; who they
  are is asked of `IArtistsFacade`, and an archived artist gains no new work (ADR 0033).
- The description is sanitized HTML with a `ShopItemMediaReferences` table, as Pages and Classes (ADR 0034, 0037).
  `content.md` gives `ShopItem` no summary field, so the site's meta description comes from the description.

### Categories are a fixed allow-list

`ShopPolicy.Categories` holds the six the site already filters on — jewelry, prints, ceramics, painting, sculpture,
books-magazines — each with its label. A write is validated against it and stored in `ShopItemCategories`.

- The site's filter and the 5.x Square category mapping both stay stable, and the admin can show checkboxes with
  real labels rather than a free-text field that a typo fragments.
- It matches "the CMS is closed by design" (`content.md`): widening the set is a line of code plus a note here.
- The slugs are the contract; the **labels are mirrored** in the admin (`SHOP_CATEGORIES`) the way `CLASS_LIMITS`
  mirrors `ClassPolicy`, and the site keeps its own copy.

### The gallery is ordered, and its first image leads

`ShopItemImages` holds the photographs with a `Position`. Position 0 is the lead: the card, the Open Graph image,
and the first image the 5.x push sends ("images first", `square.md`). Every photograph needs alt text before the
item is published, because the product page shows all of them.

### One-of-a-kind freezes with the starting quantity

- **One-of-a-kind forces the starting quantity to 1.** Staff do not have to agree with themselves twice.
- **The flag and the starting quantity are editable only while the item has no Square record** — the same gate as
  the starting price (`startingPriceEditable`, ADR 0037) — and the ServiceModel carries it as
  `startingStockEditable`.
- After that, quantity moves only through 5.4b's set-quantity command, and ADR 0032 gives one-of-a-kind artwork no
  such command, so flipping the flag then would have nothing to act on.

### Projections sit on the item

`PriceCents`, `StockOnHand` and `SoldOut` are read-only columns on `ShopItem` itself, not on a child as a class
session's are: a shop item is one Square object with one price and one count. Write ViewModels contain none of
them, which `ShopItemWriteModelTests` proves by reflection.

### The registry's write records move to the kernel

`SkuReservation` and `SkuChanges` were in `Modules/Classes/Data` (ADR 0037) because only Classes had them. They
describe what any write does to the kernel's registry, and a module may not reach into another's types, so they
move to `Managers/Catalog/`. Each module's DataLayer still supplies its own `CatalogOwnerTypes` value.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **A category allow-list (chosen)** | Staff cannot add a category without a code change. In exchange the site's filter, the admin's labels and the Square mapping cannot drift, and nothing arrives misspelled. |
| Staff-typed categories | No code change to add one, but "jewelry" and "jewellery" become two filters, with no label to show and nothing to map to Square. |
| A `Category` content type | The most flexible, and a slice of its own: an entity, screens, routes, lifecycle and tests for a type `content.md` does not list. |
| **Freeze the flag and quantity after the first sync (chosen)** | One more editability rule to explain, and it is the only honest one: after the sync Square holds the count, and one-of-a-kind artwork has no command to change it. |
| Fix one-of-a-kind at create | Simplest rule, but a typo on a fresh draft could only be fixed by deleting the item. |
| Leave both editable | Simplest screen, and after the first sync the fields would describe nothing. |
| A single lead image plus a gallery (as Exhibitions' hero) | Two concepts where artwork has one. An ordered gallery whose first entry leads gives the same thing with one table and one reorder control. |

## Consequences

- **Shop is the registry's second owner type,** so `cms.sku_taken` is now reachable across types: a shop item
  cannot take `CLS-4-1`, and a class session cannot take `ART-7`. A test asserts exactly that.
- **Still forbidden:** writing `PriceCents`, `StockOnHand` or `SoldOut` from a CMS path, and letting any of them
  into a write ViewModel or a content hash.
- 5.1 replaces `startingPriceEditable` and `startingStockEditable` with the `SquareCatalogLink`, 5.4a brings the
  change-price command and 5.4b the set-quantity command, which one-of-a-kind artwork still will not have.
- The per-item sync status chip waits for 5.1, as a class session's does (ADR 0037).
- An edit to a published item stages no revalidation, the gap ADR 0036 leaves open for every content type.
- **The development seed is the one path that publishes a piece with no photograph.** The spike's pictures are
  WordPress URLs rather than library originals, so there are none to attach, and a shop of drafts would leave the
  development site empty. `ShopBusiness.SeedAsync` sets the status directly, as every other content seeder does, so
  it never runs the publish requirements. Every staff and importer write still goes through the facade, which
  refuses to publish a piece without a described photograph, and `ShopModuleTests` asserts both halves.
