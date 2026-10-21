# 0040 — Membership plans: no page of their own, ordered perks, and a starting cadence

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/square.md`, `.claude/rules/frontend.md`
- **Builds on:** [0005](0005-square-field-ownership.md) (plan price and cadence sent once at creation, Square's thereafter),
  [0031](0031-artists-and-the-shared-content-lifecycle.md) (the shared content lifecycle),
  [0036](0036-revalidation-scheduled-publishing-and-previews.md) (revalidation, scheduled publishing, previews),
  [0037](0037-classes-and-the-shared-catalog-sku-registry.md) (projections and the `startingPriceEditable` gate)

## Context

Prompt 2.11 adds `MembershipPlan`: the three tiers the spike sells (`MEMBERSHIPS` in
`projects/site/src/app/data/mock-shop.ts`), each with a name, perks, a class discount, a highlight flag, a place in
the order and a price.

It is the fourth content type of the phase, and the first that raises three questions the others answered by
accident:

- **It has no page of its own.** The site shows every published tier on one page, `/memberships`; there is no
  `/memberships/:slug` route and Phase 3 does not plan one. So what does its slug mean, what does a status change
  revalidate, and what does a rename owe?
- **Its perks are a list of short lines,** in an order someone chose — not prose, not a set.
- **Square bills it.** ADR 0005 has the CMS send a plan's price *and its billing cadence* once, at creation, and
  hold neither afterwards. 2.11's scope names only a starting price, and a `Cadence` projection.

## Decision

### A plan has no address, so its slug is a key

- The slug stays **unique among plans** and keeps the lifecycle every content type shares, but it is a stable key —
  for the member signup, for the Square plan's CMS id attribute, and for the page's own anchors — not a web address.
  The ServiceModel says so by returning `sitePath`, which is `/memberships` for every tier.
- **A status change revalidates `/memberships`**, the one page every tier shares, plus the type and entity tags
  (ADR 0036).
- **A slug change owes no redirect.** `redirects.slug-changed` is never staged from this module, whatever the plan's
  status: nobody could have bookmarked an address that never existed (ADR 0035 refuses a redirect whose source is
  not a site path in the first place).
- **A preview resolves to `/memberships`.** `PreviewPolicy.PathPrefixes` maps `membership-plan` to
  `SitePaths.Memberships`, and that value is the whole path: `PreviewFacade` appends no slug for this one type.
  `SitePaths.Memberships` is therefore written without a trailing slash, unlike the other prefixes.

### Perks are ordered rows, keyed by their position

`MembershipPlanPerks(MembershipPlanId, Position, Text)`, cascade-deleted with the plan and replaced as a list on
every write. The **position is the key**, so a reorder rewrites the text where it sits; one save never deletes a row
and adds it back, which a `(plan, text)` key would force. The admin reorders with up/down controls that name what
they move in their labels.

### The CMS owns a starting price *and* a starting cadence

- `StartingPriceCents` and `StartingCadence` are the CMS's draft fields — the two figures the 5.x push creates the
  Square subscription plan with (ADR 0005) — and both freeze together once Square has reported money or a cadence.
  The ServiceModel carries that one gate as `startingPriceEditable`, as ADR 0037 carries it for a class.
- `MembershipCadence` offers the two the centre sells (`Annual`, `Monthly`). It is deliberately **not** the type of
  the `Cadence` projection: Square has a dozen cadences we do not offer, so the projection is a nullable string
  holding Square's own word (`ANNUAL`, `EVERY_TWO_MONTHS`), shown in the admin as words.
- `PriceCents` and `Cadence` are read-only projection columns, written only by the inbound pull.
  `MembershipPlanWriteModelTests` proves by reflection that no write ViewModel carries either.

### `featured` is a plain flag

Any number of tiers may be highlighted; the CMS enforces nothing. The spike highlights exactly one, and which tiers
a page highlights is the site's presentation, not an invariant worth a cross-row query and a refusal on every save.

### Publishing needs a price and a perk

A tier with no price has nothing to put on its card, and one with nothing listed gives a member nothing to read.
Both are checked at publish **and at schedule** (ADR 0036), and reported together. A tier that is already public
**keeps** both: clearing its perks or its price is refused while it is Published or Scheduled, rather than leaving a
blank card on the page until someone notices.

### Every refusal precedes every write

`UpdateAsync` works out what the edit asks for, runs every refusal, and only then assigns. The tracked plan lives on
the request's own `ArtsDbContext`, so an edit that is refused half-way through would leave its earlier changes
waiting for any later save in the same request.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **No page of its own (chosen)** | One type behaves unlike the others — no redirect, a `sitePath` that ignores the slug — and that asymmetry has to be explained here and in the preview map. In exchange nothing pretends an address exists, and Phase 3 owes no route. |
| Give each plan `/memberships/:slug` | Uniform with shop items: redirects, per-plan revalidation, a per-plan preview. But Phase 3 would owe a site route and a page the spike does not have, for content the one page already shows. |
| **Ordered perk rows (chosen)** | A table and a position key for three short lines per tier. In exchange the order is explicit, the text is length-checked by the column, and nothing parses JSON to count perks. |
| A JSON column of perks | One column, no child table — and the schema's first JSON column, with nothing the database can validate and a `perkCount` the list query would have to read whole rows for. |
| **A CMS starting cadence (chosen)** | One more field than 2.11's scope names, and one more thing to freeze. It is what ADR 0005 already promised, and without it 5.x would have to invent a cadence when it creates the Square plan. |
| Defer the cadence to 5.x | Smaller slice now; an open question in the Square phase, and a plan created at a cadence nobody chose in the CMS. |
| **`featured` as a plain flag (chosen)** | Two highlighted tiers are possible, and the site decides what that looks like. |
| At most one featured tier | A cross-row check and a refusal on a save that is otherwise fine, to enforce a layout rule the page could enforce itself. |
| Featuring one unfeatures the rest | One click for staff, but a save that quietly changes a record the editor was not editing. |

## Consequences

- **Memberships is the first content module that asks no other module anything:** no artist, no library image, no
  SKU. Its facade is validation and preconditions only, and its test host composes the kernel and the module alone.
- `SitePaths.Memberships` has no trailing slash. Anything that later appends a slug to it is a defect; the preview
  map's value for `membership-plan` is a whole path.
- **Still forbidden:** writing `PriceCents` or `Cadence` from a CMS path, and letting either into a write ViewModel
  or (from 5.x) a content hash.
- 5.1 replaces `startingPriceEditable` with the `SquareCatalogLink`, and 5.x sends the name, perks, discount and the
  two starting figures as the plan's first push. Plan price changes stay Square's: ADR 0005 gives membership plans
  no change-price command, unlike artwork and class sessions (ADR 0032).
- The development seed publishes the spike's three tiers directly, as every other content seeder does, so it never
  runs the publish requirements; every staff and importer write still goes through the facade.
- An edit to a published tier stages no revalidation, the gap ADR 0036 leaves open for every content type. It bites
  a little harder here, because every tier shares one page: a perk or discount edit can be stale on `/memberships`
  until something else revalidates it. Worth closing with ADR 0036 when the site reads memberships in Phase 3.
- **A scheduled time is normalized to UTC** before it is stored or answered (`ToUniversalTime()`), so the API spells
  it `+00:00` as `api-contract.md` requires whatever offset the caller sent. The other five publishable types store
  the offset they are given; the same one-line normalization belongs in each, or once in the shared schedule path.
