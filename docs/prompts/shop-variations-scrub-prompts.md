# 6311arts — Shop Variations SCRUB Prompts

Atomic prompts that give a shop item **variants** — the same piece in three print sizes, a necklace in two
chain colours — each with its own SKU, starting price, stock and sold-out state, each its own Square
variation. Nothing else about the shop changes: still artwork only, still no merch, still Square owning the
record of money.

The architectural source of truth is `CLAUDE.md` plus `.claude/rules/content.md`, `square.md`, `backend.md`,
`api-contract.md` and `frontend.md`. Read these four decisions before the first prompt:

- **ADR 0044 — class runs.** This series is the same shape applied to the shop: an aggregate whose *child*
  rows are what Square sells, each holding the SKU, the starting price and the projections, with freezing
  read per child. Where a question here has an answer there, take that answer.
- **ADR 0038 — one-of-a-kind, and the category allow-list.** One-of-a-kind artwork and variants are
  mutually exclusive, and the allow-list is the pattern the option axes copy.
- **ADR 0032 — field ownership.** The CMS owns the SKU and the *starting* figures; Square owns price, stock
  and sold-out. A variant does not change that, it multiplies it.
- **ADR 0037 — the shared SKU registry.** `CatalogSkus` is keyed on the SKU with a unique
  `(OwnerType, OwnerId)` beside it, so a variant is a new owner type and the uniqueness comes free.

**Rules:** `content.md` (lifecycle, field ownership, the allow-list pattern), `square.md` (what is pushed
and what is pulled), `backend.md` (the seam, one save), `api-contract.md` (the contract break),
`frontend.md` (Studio and the site)
**Skills:** `add-content-entity`, `add-endpoint`, `new-component`
**Reviewers:** `@architecture-reviewer`, `@api-contract-checker`, `@integration-reviewer`, `@design-review`,
`@test-gap-analyzer`

---

## Where the shop stands today

Reviewed at commit on `dev`, 2026-10-30. The shop is **content-complete and Square-less**.

| Piece | State |
|---|---|
| `ShopItem` entity | Title, slug, sanitized HTML description, optional artist (`Restrict` FK), categories, ordered gallery, description-image references, SKU + `ItemNumber`, `OneOfAKind`, `StartingPriceCents`, `StartingStockQuantity`, the projections `PriceCents` / `StockOnHand` / `SoldOut`, full lifecycle, soft delete, `RowVersion` |
| Seam | `ShopFacade` / `ShopImportFacade` → `ShopBusiness` (~1,450 lines) → `ShopDataLayer` → `ShopRepository`, complete and conventional |
| SKU | One per item, owner type `shop-item` in the kernel's `CatalogSkus`, suggested `ART-<n>` |
| Freezing | `StartingPriceEditable` / `StartingStockEditable`, both `!item.HasSquareRecord`, where `HasSquareRecord` is `PriceCents is not null \|\| StockOnHand is not null` |
| Studio | `shop-list`, `shop-item-editor`, `gallery-editor`, `shop-import`; `SHOP_LIMITS`, `SHOP_CATEGORIES`, `SHOP_IMPORT_COLUMNS` mirror the policy |
| Site | `/shop` (filter, sort, keyset paging) and `/shop/:slug` (`product-detail`) |
| CSV import | ADR 0053: nine columns, every row a new Draft, all or none |
| **Square** | **Not built.** No `SquareSync` module, no `Arts6311.SquareFake`, `Arts6311.Square` is an empty project. `PriceCents`, `StockOnHand` and `SoldOut` are therefore always `null` / `false`, and every item reads as "before its first sync" |

**One item is one Square object with one price and one count** — ADR 0038 says so in as many words, and the
projections sit on `ShopItem` for that reason. That is the assumption this series breaks.

### Why now rather than after Phase 5

Because Phase 5 does not exist yet. Every one of these would otherwise be built against one price per item
and then rebuilt: 5.1 `SquareCatalogLink` (it already reserves a **plural** `variation ids` field), 5.4 the
outbound push, 5.4a change-price (compare-and-set, per variation), 5.4b set-quantity
(`PHYSICAL_COUNT`, per variation), 5.7 the inbound catalog handler, 5.8 the inbound inventory handler, 5.12
the reconciler, 5.13 cart and quote. Landing variants first makes all of them per-variation from the start,
which is what Square's model is anyway: **in Square an item always has at least one variation.** Today's
single-price item is a one-variation item that has not been written down as one.

The cost of doing it now is a contract break in `/api/v1/cms/shop/**` and `/api/v1/site/shop/**` while the
only consumer is the in-repo admin and the in-repo site — exactly the position ADR 0044 was in, and it took
the break for the same reason.

---

## Decisions taken before this file was written

| Decision | Settled as |
|---|---|
| What is sellable | **The variant.** `ShopItem` → `ShopItemVariant` rows, as offering → run. The SKU, the starting price, the starting stock and the projections move off the item onto the variant. The item keeps title, slug, description, artist, categories and the gallery. |
| An item with no variations | **Still has one variant,** created implicitly and shown as a plain price field in Studio. There is no second code path: "no variations" is one unnamed variant, which is also exactly what Square stores. |
| How size and colour are expressed | **A fixed allow-list of option axes** (`ShopPolicy.VariantOptions`: `size`, `colour`, `material`), each variant carrying zero or more `(axis, value)` pairs with a staff-typed *value*. The axes are closed like the categories (ADR 0038); the values are open, because "8×10 in" is not something to hard-code. |
| Combinations | **Staff add the variants they actually stock.** No matrix generator, no cartesian product. A gallery that has three sizes in blue and one in red adds four rows. |
| Duplicate combinations | **Refused.** Two variants on one item may not carry the same set of `(axis, value)` pairs, and `cms.variant_duplicate` says so. |
| One-of-a-kind | **Mutually exclusive with having more than one variant.** A one-of-a-kind piece is one variant at quantity 1 with no options. Adding a second variant to it is refused, and so is flagging a multi-variant item one-of-a-kind. |
| Per-variant images | **A variant may name one image from the item's gallery** as its own, so choosing "Blue" swaps the lead photograph. Optional; the gallery stays the item's. |
| Freezing | **Per variant,** read from that variant's own projections, as a run's is (ADR 0044). One sold variant does not freeze its siblings. |
| Square mapping | **One `ITEM`, one `ITEM_VARIATION` per variant.** Whether the axes become Square `ITEM_OPTION` objects is **DECIDE** (see the end of this file); the default written down here is a composed variation name and no option objects. |
| CSV import | **Variant columns added** (`variant_name`, `variant_size`, `variant_colour`, `variant_material`), with consecutive rows sharing a `sku`-less title grouped onto one item. **DECIDE** — the simpler alternative is one variant per imported row and no grouping. |
| Merch | **Still out of bounds.** Variants are for artwork — editions, sizes, finishes. Nothing here creates a merch type, and `CLAUDE.md` still has merch sold only through Square POS. |

---

## Where it slots into the run order

```text
2.10 (shop items) + 2.13 ✅ ──> V.1 … V.8 ──> V.10 ✅ ──> 5.1 … 5.14 (Square, now per variation)
                                     └──> V.9 (writes Square's half down; builds none of it)
```

| Prompts | Need | Notes |
|---|---|---|
| V.1 | nothing | The ADR. Decide before any code, and decide the two `DECIDE`s with me. |
| V.2 | V.1 | Entity, EF configuration, migration. Nothing reads it yet. |
| V.3 | V.2 | Policy and validators. Still nothing reads it. |
| V.4 | V.3 | Business: the rules, the SKU reservations, the publish requirements. The backend is correct after this. |
| V.5 | V.4 | The CMS contract and its tests. **The admin breaks here and is fixed in V.7.** |
| V.6 | V.5 | The site reads. The site breaks here and is fixed in V.8. |
| V.7 | V.5 | Studio. |
| V.8 | V.6 | The product page and the shop cards. |
| V.9 | V.1 | `square.md`, the ADR's Square section and the 5.x prompt text. Paper only. |
| V.10 | everything | Checkpoint. |

**V.2 through V.8 are one contract change and must land together** on a branch. Merging V.5 without V.7
leaves Studio unable to save a shop item.

---

## The shape

```text
ShopItem                                     one Square ITEM
├── Title, Slug, Description, ArtistId       CMS-owned, pushed
├── Categories[]                             CMS-owned, pushed
├── Images[] (ordered, first leads)          CMS-owned, pushed
└── Variants[]  ◀── NEW, 1..ShopPolicy.VariantMaxCount
    ├── Name?                 "8×10 in", or null for the only variant
    ├── Options[]             (axis, value) from ShopPolicy.VariantOptions
    ├── ImageMediaId?         one of the item's gallery images
    ├── Sku                   CMS-owned, unique across the CMS (owner type shop-item-variant)
    ├── Position              the order staff chose; 0 is the default selection
    ├── StartingPriceCents    CMS draft field, frozen once this variant has a Square record
    ├── StartingStockQuantity CMS draft field, frozen on the same gate
    ├── PriceCents            Square's record  ─┐
    ├── StockOnHand           Square's record   ├─ read-only projections, one Square ITEM_VARIATION
    └── SoldOut               Square's record  ─┘
```

Field ownership, restated for a variant (`square.md`'s table gains these rows in V.9):

| CMS-owned → pushed | Square holds the record → pulled | CMS *requests* a change |
|---|---|---|
| Variant name, option values, position, image, **SKU** | `PriceCents`, `StockOnHand`, `SoldOut` | price: change-price per variation (5.4a) |
| The starting price and quantity the **first** push creates the variation with | — | quantity: set-quantity per variation (5.4b), except one-of-a-kind |

---

## What may never change

This list is the series' spine. Every prompt restates the part that applies to it, and V.10 tests all of it.

| Never | Why |
|---|---|
| A write ViewModel carrying `priceCents`, `stockOnHand` or `soldOut` — **on the item or on a variant** | ADR 0032. `ShopItemWriteModelTests` proves it by reflection today; V.5 extends it to the variant models rather than replacing it. |
| A CMS path writing a projection column | `square.md`. The inbound pull through the `SquareSync` facade is the only writer, and it does not exist yet. |
| A content hash or a content push that includes price or stock | `square.md`. The hash covers CMS-owned fields, SKU included. |
| Two variants on one item with the same option combination, or two records in the CMS with the same SKU | `cms.variant_duplicate` and `cms.sku_taken`. The second is the database's job (`CatalogSkus`), not a query. |
| A one-of-a-kind item with two variants, or a count other than 1 | ADR 0032, ADR 0038. Refused, never quietly corrected. |
| A variant Square holds being deleted, or its starting price or quantity changed in place | ADR 0037, 0044. The same gate a class run has. |
| Reserving or releasing a SKU in a save of its own | ADR 0037. `ISkuRegistry` stages in the **same** `SaveChangesAsync` as the row. |
| A merch type, field or flag | `CLAUDE.md`, ADR 0032. Variants are editions and sizes of artwork. |
| An option axis staff can type | ADR 0038's reasoning: the site's selector, Studio's labels and the 5.x mapping would drift. Values are typed; axes are not. |

---

## V.1 — The ADR and the two decisions

```
SCOPE: Write docs/adr/0062-shop-item-variants.md (claim 0062; 0055-0058 are the artist-accounts series,
0059 analytics, 0060-0061 infrastructure — renumber if any lands differently). One ADR, in the
docs/adr/README.md format, recording: the variant as the sellable unit and the SKU, starting figures and
projections moving from ShopItem onto it; an item with no variations being one unnamed variant rather than a
second code path; option axes as a fixed allow-list with staff-typed values; staff-entered combinations
rather than a generated matrix; duplicate combinations refused; one-of-a-kind and multiple variants mutually
exclusive; per-variant freezing; the optional per-variant gallery image; one Square ITEM with one
ITEM_VARIATION per variant; the /api/v1 contract break and why it stays v1 (as ADR 0044 did); and that the
migration carries every existing item's SKU, starting figures and projections onto one generated variant.
Options considered must include: variants on the item (chosen), a second content type, a generated matrix
from axes, and leaving the shop single-price and modelling editions as separate items.
Mark two DECIDE items with their costs: whether the axes become Square ITEM_OPTION / ITEM_OPTION_VAL objects
or variations keep a composed name; and whether the CSV import groups rows into one item or makes one item
per row.

CONSTRAINT: docs/adr/README.md format. Cite ADR 0032, 0037, 0038, 0044, 0052, 0053 and api-contract.md.
Read ADR 0044 before writing a line: where a question here has an answer there, take that answer and say so.

RESTRICTION: Do NOT write code, EF configuration, a migration or UI. Do NOT decide the two DECIDE items —
record the options and their costs and wait for me. Do NOT propose a merch type, a free-text axis, or any
change to who owns price and stock.

UTILIZATION: @architecture-reviewer on the field-ownership section.

BEHAVIOR: Draft it, summarize the contract break in five lines, wait for my review and my two decisions.
```

## V.2 — The entity, the configuration and the migration

```
SCOPE: ShopItemVariant and ShopItemVariantOption entities in Modules/Shop/Data/Entities/, their
IEntityTypeConfiguration classes, and the AddShopItemVariants migration.
Variant: Id, ShopItemId (Cascade from the item, as ClassRun is from its offering), Name?, Position, Sku,
ImageMediaId? (Restrict to MediaAsset, as the gallery's is), StartingPriceCents?, StartingStockQuantity?,
PriceCents?, StockOnHand?, SoldOut, HasSquareRecord computed as ClassRun's is.
Option: Id or a composite key, ShopItemVariantId, Axis (varchar, ShopPolicy.VariantOptionAxisMaxLength),
Value (nvarchar), unique (ShopItemVariantId, Axis) — one value per axis per variant.
Remove Sku, ItemNumber's SKU role, OneOfAKind's stock coupling, StartingPriceCents, StartingStockQuantity,
PriceCents, StockOnHand and SoldOut from ShopItem; keep OneOfAKind and ItemNumber on the item.
CatalogOwnerTypes gains ShopItemVariant = "shop-item-variant"; ShopItem stays for nothing and is removed
only if no row can hold it (check the registry's data first and say which you did).
Migration: generated, then read. It must carry every existing ShopItem's Sku, StartingPriceCents,
StartingStockQuantity, PriceCents, StockOnHand and SoldOut onto one new variant per item with Position 0 and
a null Name, and re-point that item's CatalogSkus row from owner type shop-item to shop-item-variant with
the new variant id. Soft delete: the variant is a child of a soft-deleted aggregate, so it carries no
DeletedAt of its own and no global query filter — confirm that against ClassRun and say so in the prompt's
summary.

CONSTRAINT: backend.md (module layout); content.md; ADR 0037, 0044, 0052; add-content-entity skill. Never
hand-edit the generated migration (CLAUDE.md "Hygiene"). Restart aspire run so MigrationService applies it;
while aspire run holds the build output, redirect it with -p:BaseOutputPath.

RESTRICTION: No projection column on a write path. No Square id on either entity — those live in
SquareCatalogLink (ADR 0006), which does not exist yet. Do NOT add a Square-owned field the inbound pull
would not write. Do NOT drop and recreate the item's data: the migration carries it across.

UTILIZATION: add-content-entity; @architecture-reviewer.

BEHAVIOR: Plan the two entities, the column moves and the data-carrying migration; wait; implement; prove
the migration with a round trip — seed an item on the old shape, migrate, assert the variant holds the SKU
and figures and the CatalogSkus row points at it.
```

## V.3 — Policy, the axis allow-list and the validators

```
SCOPE: ShopPolicy gains VariantOptions (the fixed axes: size, colour, material — each a slug and a label,
as Categories is), IsVariantOption(slug), VariantMaxCount (8: an edition's sizes, not a warehouse),
VariantNameMaxLength (80), VariantOptionAxisMaxLength (16), VariantOptionValueMaxLength (60) and
VariantOptionsMaxCount (3, one per axis). StartingPriceMaxCents and StartingStockMaxQuantity stay and now
apply per variant.
Write ViewModels in Managers/ViewModels/: ShopItemVariantWriteViewModel (Id? so an edit moves a variant
rather than replacing it — exactly as ClassSession keeps its ids, Name?, Options as (axis, value) pairs,
ImageMediaId?, Sku?, StartingPriceCents?, StartingStockQuantity?), and ShopItemCreateViewModel /
ShopItemUpdateViewModel losing Sku, OneOfAKind's stock pairing, StartingPriceCents and
StartingStockQuantity and gaining Variants.
Validators: shape only — counts, lengths, an axis in the allow-list, a non-blank value, no two options on
one variant sharing an axis, at least one variant on create, and the "nothing to change" rule extended.

CONSTRAINT: content.md ("a fixed allow-list ... widening it is a line of code plus an ADR note");
backend.md (shape validation at the edge, invariants in Business); ADR 0038, 0062.

RESTRICTION: Shape only here. Duplicate combinations, one-of-a-kind exclusivity, freezing and SKU clashes
are Business's and belong in V.4 — a validator that reaches for the database or another module is a defect.
No price, stock or sold-out field on any of these models.

UTILIZATION: @architecture-reviewer, @test-gap-analyzer.

BEHAVIOR: Plan the numbers and the model shapes; wait; implement; unit-test every validator rule including
an unknown axis, a blank value, two options on one axis and nine variants.
```

## V.4 — Business: the rules, the SKUs and the publish requirements

```
SCOPE: ShopBusiness handles variants as a replaced set, the way ClassBusiness handles runs.
Create: at least one variant; a variant with no name and no options is the "no variations" case; suggest a
SKU per variant (ART-<item>-<n>, numbered by position, falling back to ART-<item> for a lone variant —
confirm the shape against SkuPolicy and ADR 0037 and say which you chose); reserve every SKU through
ISkuRegistry in the one save.
Update: match sent variants by Id, create the ones with none, delete the ones left out — and refuse to
delete one Square holds (cms.variant_in_square, as a class run is refused). Per-variant freezing from that
variant's own HasSquareRecord: its starting price and quantity may be sent unchanged but not changed.
Invariants, each its own error code and all problems reported at once keyed to variants[n]:
  - cms.variant_duplicate — two variants with the same option set (compare as a set, case-insensitively on
    the value, trimmed)
  - cms.variant_one_of_a_kind — OneOfAKind with more than one variant, or a starting quantity other than 1
  - cms.variant_image_not_in_gallery — ImageMediaId is not one of the item's ImageMediaIds
  - cms.sku_taken — from the registry, per variant
Publishing requires: a description, every photograph described (unchanged), and for every variant a
starting price or a Square price — the class-run rule, per variant. Checked at schedule too (content.md).
Seeds and the development seeder give each seeded item one variant.

CONSTRAINT: backend.md (Business calls the DataLayer only; one SaveChangesAsync); content.md (what
publishing requires is checked at schedule); ADR 0032, 0037, 0038, 0044, 0062; square.md.

RESTRICTION: Business never writes PriceCents, StockOnHand or SoldOut. Never reserve or release a SKU
outside the write's own save. Never silently correct a one-of-a-kind count — refuse it. Do NOT let a
variant Square holds be removed, renamed out of its SKU, or repriced in place.

UTILIZATION: add-content-entity; @architecture-reviewer, @outbox-reviewer (the revalidation staging is
unchanged and must stay unchanged), @test-gap-analyzer.

BEHAVIOR: Plan each rule and where it lives; wait; implement; tests: Business with a mocked DataLayer for
every rule above; a real-database test that a failed save leaves neither the variants nor their SKU
reservations; and a test that an edit keeping a variant's id moves it rather than replacing it.
```

## V.5 — The CMS contract

```
SCOPE: ServiceModels and the endpoints. ShopItemServiceModel and ShopItemSummaryServiceModel lose sku,
startingPriceCents, startingStockQuantity, startingPriceEditable, startingStockEditable, priceCents,
stockOnHand, soldOut and squareHasRecord, and gain variants: ShopItemVariantServiceModel[] carrying each of
those per variant plus id, name, options, imageMediaId, position. The summary gains
fromPriceCents (the lowest Square price among variants, null when none is priced), variantCount and
soldOut (true only when every variant is sold out). ShopItemDetail and ShopItemListRow follow.
Endpoints are unchanged in shape: POST/PATCH /api/v1/cms/shop take the new write models, the list and detail
return the new ServiceModels, If-Match and the audit rows are untouched. Audit detail names what changed
about which variant.
Tests: extend ShopItemWriteModelTests so the reflection assertion covers ShopItemVariantWriteViewModel;
endpoint tests for the happy path, cms.variant_duplicate, cms.variant_one_of_a_kind, cms.sku_taken and an
authz failure; refresh the OpenAPI snapshot and list the removed fields in the prompt's summary.

CONSTRAINT: api-contract.md (ProblemDetails with stable codes, integer cents beside a currency, the ETag);
backend.md (controllers inject facades only); add-endpoint skill. Each new error code gets its status in
ApiProblemDetails.

RESTRICTION: Breaking change, deliberately, and it stays /api/v1 for ADR 0044's reason — v1 is unreleased
and its only consumers are in this repository. Do NOT add a v2, a compatibility shim or a field that
restates a variant's price on the item. Studio is broken between this prompt and V.7: say so in the summary
and do not paper over it.

UTILIZATION: add-endpoint; @api-contract-checker, @architecture-reviewer.

BEHAVIOR: Plan the model diff field by field; wait; implement; run the full suite; report every removed and
added field as a table.
```

## V.6 — The site reads

```
SCOPE: /api/v1/site/shop and /api/v1/site/shop/{slug} (ADR 0042). SiteShopItemSummaryServiceModel gains
fromPriceCents (lowest Square price among variants) and keeps soldOut as "every variant is sold out";
SiteShopItemServiceModel gains variants: SiteShopItemVariantServiceModel[] (id, name, options,
image, priceCents, soldOut — never stockOnHand, which is a staff number and not a visitor's) and
optionAxes: the axes this item actually uses, in ShopPolicy.VariantOptions order, each with its values in
variant position order, so the page renders selectors without inferring them in TypeScript.
Sorting: price-asc / price-desc order by fromPriceCents, with an unpriced item last in both, as today.
ShopItemSiteRow composes it. Cache tags are unchanged (shop-item and artist) — a variant is not a content
type and adds none.

CONSTRAINT: content.md (site reads show Published only, every entry carries the type tag of every type it
shows); ADR 0042; backend.md (cross-module reads facade to facade — the gallery and the credit still come
from Media and Artists).

RESTRICTION: stockOnHand never leaves the CMS surface. No new cache tag, no entity or path tag in a read by
slug. Do NOT cache a not-found answer or a page after the first (ADR 0042). The site is broken between this
prompt and V.8.

UTILIZATION: add-endpoint; @api-contract-checker, @architecture-reviewer.

BEHAVIOR: Plan the two models and the sort; wait; implement; tests: an item with three variants returns the
lowest price as fromPriceCents, one with none priced returns null and sorts last, soldOut is true only when
every variant is, and the axes come back in policy order with values in position order.
```

## V.7 — Studio: the variants editor

```
SCOPE: A variant-list editor in projects/admin, modelled on features/classes/run-list, used by
shop-item-editor: rows with name, the option axes as (axis select, value text) pairs, an image picker
limited to the item's gallery, SKU, starting price and starting quantity, reorder and remove, and a
per-variant "Square holds this" state that disables the frozen fields and the remove control.
"No variations" is the default: a new item opens with one row whose name and options are empty and whose
price field is the one the editor shows prominently; an "Add a variation" control reveals the rest.
models/shop.ts mirrors the policy: SHOP_VARIANT_OPTIONS (axes and labels, as SHOP_CATEGORIES mirrors the
categories) and the new SHOP_LIMITS numbers. shop-list gains a variants column and shows fromPriceCents.
Field errors keyed variants[n].* are shown on the right row.

CONSTRAINT: frontend.md (standalone, signals, zoneless, strict; no HttpClient in a component; the service
owns the calls); design-system.md and @6311/ui primitives; ADR 0050 (markup and styles in sibling files),
ADR 0051 (one component per folder); new-component skill.

RESTRICTION: No price, stock or sold-out input anywhere in Studio beyond the two starting fields, and those
disabled per variant once Square holds it. No axis the policy does not list. Do NOT reimplement the gallery
editor — the image picker chooses from the gallery the existing gallery-editor manages.

UTILIZATION: new-component; @design-review, @architecture-reviewer.

BEHAVIOR: Plan the component and its inputs/outputs; wait; implement; Vitest for: one row by default, add
and remove, a frozen row's disabled fields and blocked remove, a duplicate combination surfacing the
server's error on the right row, and reorder changing position.
```

## V.8 — The site: selectors on the product page

```
SCOPE: product-detail renders the axes as selectors (a radio group per axis, labelled, keyboard-operable),
resolves the selection to a variant, and shows that variant's price and sold-out state; selecting a variant
with an image of its own swaps the lead photograph. The default selection is position 0. A combination the
item does not stock is disabled, not hidden, so the page says what exists. An item with one unnamed variant
renders exactly as it does today: no selectors, one price.
The shop cards show fromPriceCents as "From $x" when the item has more than one priced variant, and the
plain price otherwise; sold-out stays the item-level "every variant" answer.

CONSTRAINT: frontend.md (SSR-safe: the first render is the default selection, no browser-only API in the
render path); design-system.md; ADR 0039 (no new inline script); ADR 0050, 0051; ADR 0046 (the site reads
through SiteApi).

RESTRICTION: No cart, no add-to-cart, no checkout — 5.13 and 5.14 own those and do not exist. Do NOT infer
the axes or their values in TypeScript: V.6 returns them. Do NOT show stock counts to visitors. The page
must render with JavaScript disabled, showing the default variant.

UTILIZATION: new-component; @design-review, playwright-cli for the keyboard path.

BEHAVIOR: Plan the selector and the resolution; wait; implement; Vitest for selection, image swap, the
unstocked combination and the single-variant passthrough; a Playwright check that the selectors are
reachable and announced.
```

## V.9 — Square's half, written down and not built

```
SCOPE: Paper only. Update .claude/rules/square.md: the field-ownership table gains the variant rows from
this file's "The shape" section; "Website catalog" says one ITEM per shop item and one ITEM_VARIATION per
variant; change-price and set-quantity are per variation and carry the variant's SKU; one-of-a-kind is one
variation at 1 with no set-quantity command. Update docs/prompts/scrub-prompts.md prompts 5.1, 5.4, 5.4a,
5.4b, 5.7, 5.8, 5.12 and 5.13 so each names variants where it says item: SquareCatalogLink's variation ids
map variant id to Square variation id; the push upserts the item and its variations in one batch; the
inbound handlers write projections onto the variant the variation maps to and ignore a variation with no
CMS id; the reconciler compares per variation; quote and cart price from a variant's catalog object id.
Add to ADR 0062's Square section whichever of the two DECIDE answers I gave.

CONSTRAINT: square.md's existing structure; ADR 0006 (Square ids live in SquareCatalogLink, never on a core
entity), 0032, 0062. Cite Square's own documentation for the item/variation model and for PHYSICAL_COUNT.

RESTRICTION: Write NO code. Do NOT create the SquareSync module, the gateways or the fake — those are 5.1,
5.2 and 5.3. Do NOT change who owns a field.

UTILIZATION: @integration-reviewer on the field-ownership table; verify the Square model against
developer.squareup.com and cite the page.

BEHAVIOR: Plan the diff to each rule and prompt; wait; apply it; list every prompt you touched.
```

## V.10 — ✅ Shop variations checkpoint

```
SCOPE: Prove the whole series. dotnet build -warnaserror; dotnet test (including Category=E2E, Docker
needed); cd src/web && npm ci && npm run build && npm test; then aspire run and walk it through the gateway:
create an item with three sizes, publish it, see the selectors and the prices on /shop/:slug, see "From $x"
on /shop, unpublish it and see a 404. Assert every line of this file's "What may never change" table has a
test, and name the test for each. Re-run @architecture-reviewer, @api-contract-checker,
@integration-reviewer, @design-review and @test-gap-analyzer and fix what they raise.
Update .claude/rules/content.md and api-contract.md per the table below, and docs/architecture/architecture.md
where it describes the shop.

CONSTRAINT: CLAUDE.md "Usage"; every reviewer.

RESTRICTION: Do not close the checkpoint with a known gap unrecorded — an unfixed finding becomes a DECIDE
line in ADR 0062 or a new prompt, never a silence.

UTILIZATION: all reviewers; playwright-cli.

BEHAVIOR: Run everything, report pass or fail per command with its output, and list what each reviewer said.
```

---

## Rules to update (V.10)

| Rule | Change |
|---|---|
| `content.md` | The `ShopItem` row gains variants and loses the item-level SKU and starting figures; a **Shop variants** section: the axis allow-list, one-of-a-kind exclusivity, duplicate combinations, per-variant freezing, publish requiring a price per variant, and that "no variations" is one unnamed variant |
| `square.md` | V.9's diff: the variant rows in the ownership table, one ITEM plus one ITEM_VARIATION per variant, per-variation commands |
| `api-contract.md` | The new error codes and their statuses; the shop contract break noted beside ADR 0044's |
| `backend.md` | Nothing. The seam does not change, which is the point |

## Open decisions (`DECIDE` in V.1)

- **Whether the axes become Square `ITEM_OPTION` / `ITEM_OPTION_VAL` objects.** Option objects give Square's
  own dashboard and POS proper size and colour pickers, and keep the two systems' vocabularies aligned. They
  are also more objects to create, version, reconcile and delete, and they are shared across items, so two
  items using "Size" must agree on its values. The default written down here is a composed variation name
  ("8×10 in / Blue") and no option objects, which is cheaper and still sells correctly. Decide before 5.4.
- **Whether the CSV import groups rows into one item.** Grouping consecutive rows that share a title into one
  item with several variants is what a staff spreadsheet of an edition looks like; it also makes a blank cell
  ambiguous and every row-level error message harder to word. One item per row is simpler and makes importing
  a three-size edition three items that staff then merge by hand — which they cannot do, because there is no
  merge. ADR 0053 says every row becomes a new Draft; grouping bends that sentence and needs a note there too.
- **Whether `ItemNumber` still earns its place.** It exists to build the suggested SKU `ART-<n>`, which now
  belongs to the variant. Keeping it keeps the suggestion stable and human; removing it means a suggestion
  built from the variant's position alone. Cheap either way, and tidier decided than drifted.
- **Whether a variant needs its own `Position`-independent stable label for Square.** A renamed variant whose
  SKU is unchanged is the same variation to Square; a reordered one is too. Nothing here depends on it, but
  5.12's reconciler will want to say which variation is which, and the SKU is the obvious answer.
- **Whether one-of-a-kind survives at all.** With per-variant stock it is a one-variant item at quantity 1,
  which is what the flag already means. It stays because the site shows it and the Square half reads it (no
  set-quantity command), but it is now derivable rather than independent.

## Verification, per CLAUDE.md

```text
dotnet build -warnaserror
dotnet test                                     # Category=E2E needs Docker
cd src/web && npm ci && npm run build && npm test
dotnet ef migrations add AddShopItemVariants --project src/Arts6311.Domain --startup-project src/Arts6311.Api
aspire run                                      # browse only through the gateway endpoint
```

An existing shop item must survive the migration with its SKU, its starting figures and its address intact,
and must render on `/shop/:slug` exactly as it does today. That is the first thing to check after V.2 and
the last thing to check in V.10.
