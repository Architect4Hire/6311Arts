---
paths:
  - src/Arts6311.Domain/Modules/Artists/**
  - src/Arts6311.Domain/Modules/Instructors/**
  - src/Arts6311.Domain/Modules/Exhibitions/**
  - src/Arts6311.Domain/Modules/Classes/**
  - src/Arts6311.Domain/Modules/Shop/**
  - src/Arts6311.Domain/Modules/Memberships/**
  - src/Arts6311.Domain/Modules/Pages/**
  - src/Arts6311.Domain/Modules/Redirects/**
  - src/Arts6311.Importer/**
---
# CMS Content

The CMS is closed by design. Every content type is code we own: there are no plugins, no custom fields and no page builder. Staff get exactly the screens the site needs.

## Content types

| Type | Notes |
|---|---|
| `Artist` | Name, slug, bio, medium, portrait, active |
| `Instructor` | Name, slug, bio, medium (what they teach), portrait, active, website (`https:` only), Instagram handle (no `@`), optional linked artist (via the Artists facade). Not an artist flag: a separate roster (ADR 0043) |
| `Exhibition` | Title, slug, summary, body, start/end, reception start/end/note, hero, artists (M:N) |
| `ClassOffering` | Title, slug, sanitized HTML body ("More details" in the admin, ADR 0045), summary, type, level, hero; has `ClassRun` rows — the same class over different terms — each with label, **instructor** (via the Instructors facade), cadence, display zone, SKU, capacity, starting price, and `ClassSession` rows (start, end only). The run is what is sold. **Square-synced** (ADR 0032, 0037, 0044). |
| `ShopItem` | Artwork only (merch is POS-only, ADR 0032). Title, slug, sanitized HTML description, artist (optional, via the Artists facade), categories, ordered gallery, SKU, one-of-a-kind flag, starting price, starting quantity. **Square-synced** (ADR 0038). |
| `MembershipPlan` | Name, slug, perks (ordered rows), class discount %, featured, order, starting price and starting cadence. **No page of its own:** every published tier is shown on `/memberships` (ADR 0040). **Square-synced** (ADR 0005, 0040). |
| `Page` | Slug, title, sanitized HTML body (privacy, terms, about copy blocks) |
| `Redirect` | From path, to path, 301/302, source (`Manual` / `Imported` / `SlugChange`). No lifecycle: live once written (ADR 0035) |

## Lifecycle

```text
Draft ──schedule──▶ Scheduled ──publish──▶ Published ──unpublish──▶ Draft
  │    ◀─unschedule────┘            ▲
  └──────publish─────────────────────┘
  └──────archive─────────┴───────────┴──▶ Archived
```

- **Every publishable type has:** `Status`, `PublishedAt`, `ScheduledFor` (UTC), `UpdatedAt`, `UpdatedBy`, `RowVersion`.
- **Transitions are explicit commands** (`publish`, `unpublish`, `schedule`, `unschedule`, `archive`) and are audited. A `PATCH` never changes status.
  - `schedule` is the only one with a body: the shared `ContentScheduleViewModel` (`{scheduledFor}`, UTC, in the
    future). Sending it again moves the time; a published record is unpublished before it can be scheduled.
  - **What publishing requires is checked at schedule too** (ADR 0036), so a missing image alt text, summary or
    body is refused on the screen rather than at midnight.
  - **The scheduled publisher** runs in the Worker every minute, claims each record by its row version (so two
    replicas publish it once), publishes through the module's facade as `system:scheduled-publisher`, and returns a
    record it can no longer publish to Draft with a `<type>.schedule-abandoned` audit row. A unit that throws is
    counted and logged and the pass carries on, so one stuck record never starves the ones behind it.
- **Square-synced types:**
  - Publish, update and archive stage the matching `square.*` outbox message in the same save.
  - Sync status is shown, never inferred from content status.
- **Every status command stages `site.revalidate-requested {entityType, entityId, paths}`** in the same save
  (ADR 0036), so the cache stops serving what the change affects. **So does an ordinary edit to a record whose
  status is `Published`** (2.13): the content is what the site is showing, so changing it has to clear what shows
  it. An edit to a Draft, Scheduled or Archived record stages nothing, because nothing public renders one — **except
  an artist or an instructor**, whose every edit stages it: an unpublished artist is still credited by name on
  published exhibitions and artwork, and an unpublished instructor is still named on published classes (ADR 0042,
  0043).
  The Worker's kernel handler clears the `type:`, `entity:` and `site:` tags; nothing invalidates inline after a
  save, and nothing calls the site over HTTP. The tags reach the API through the **cache invalidation bus** (Redis
  pub/sub, ADR 0049): HybridCache reads a tag's time from L2 only once per process, so the shared L2 alone never
  carries the Worker's invalidation across. A change shows on the site within the Worker's poll and a render
  (2.5–6 s measured in 3.4, against up to 291 s before; ADR 0049); a process that missed a message serves its copy
  for at most the entry's lifetime.
  `Category=E2E` `APublishedClassReachesTheSitePageAndUnpublishingMakesIt404` holds the path end to end.
- **The site reads** (`/api/v1/site/*`, ADR 0042) show Published records only, as the site's own models, cached in
  each module's facade. **Every site cache entry carries the type tag of every content type it shows** — a shop item
  is tagged `shop-item` and `artist`, because it credits one — never an entity or path tag alone: a read by slug cannot
  know the id to tag with, and a rename's revalidation names only the new address. A new read that embeds another
  type's records adds that type's tag (`SiteContentTypes` spells them), or it serves the old name until it expires.
  Not-found answers and shop pages after the first are not cached.
- **Slugs** are unique per type. A slug change on a record that has ever been published redirects its old address to the new one (ADR 0035): the content module's DataLayer stages `redirects.slug-changed` in the rename's own save, and the Redirects module applies it.
  - **A membership plan is the exception** (ADR 0040): it has no address of its own, so its slug is a stable key, a
    rename owes no redirect whatever its status, and its status changes revalidate the one page every tier shares.
    Its policy exposes `SitePath` rather than a `SitePathPrefix`, and nothing appends a slug to it.
- **Shared kernel** (ADR 0031): `Managers/Publishing/` holds `PublishStatus`, the transition table `PublishLifecycle.Next`, `Slugs` and `ContentErrorCodes`; `Managers/Concurrency/EntityTag` verifies `If-Match`. A content module uses these rather than its own.
  - Status commands carry `If-Match` too. An illegal move (publishing what is published, anything from Archived) is `cms.status_transition_invalid`, never a silent no-op.
  - `PublishedAt` is the **first** publish and survives an unpublish; it is what makes a slug change owe a redirect (`PublishedPathChange.For`). Since 2.7 nothing returns `cms.slug_locked`; the code stays in the contract, and the ServiceModels' `slugLocked` now means "changing it redirects".
  - Each type's site address is its policy's `SitePathPrefix` plus the slug: `/artists/`, `/exhibitions/`, `/` for
    pages. The prefixes live once, in the kernel's `Managers/Publishing/SitePaths`, because redirects, revalidation
    and previews all need the mapping from outside the module (ADR 0036).
- **Redirects** (ADR 0035):
  - Addresses are stored in one normal form (`RedirectPaths`): a lower-case site path with no trailing slash, plus any query. The site normalizes an incoming path the same way before it looks one up.
  - An old address can't be the home page, have a fragment, or sit under a reserved prefix (`/admin`, `/api`, `/bff`, `/media`, `/webhooks`, `/health`, `/alive`, `/signin`, `/account`).
  - A destination is a site path or an `https://` address.
  - **Staff writes refuse loops and chains longer than one hop** (`redirects.loop_or_chain`).
  - **Slug changes keep every chain one hop instead of refusing.** Redirects arriving at the old address are repointed. A redirect leaving an address that is live again is retired. Each row's `EffectiveAt` makes out-of-order delivery safe.
  - Every change is Admin-only and audited. The site's list is cached for 5 minutes, and every redirect write
    (and every row a slug change touches) stages `site.revalidate-requested`, so the API's copy is cleared within
    a minute (ADR 0036).
  - **Cross-module content** (ADR 0033): a type that credits another's records (an exhibition's artists) holds a join table with a `Restrict` foreign key to the other's entity, and asks the other's facade who the records are and whether they can be linked. An archived record gains no new links.
  - An image on a Published or Scheduled record needs alt text when it is attached, not only when the record is published.
- **Previews** use a signed, expiring token, render `noindex`, never enter the sitemap and grant no write access.
  - Staff mint one with `POST /api/v1/cms/preview` (`{entityType, entityId}`); the site reads it back from
    `GET /api/v1/site/preview/{token}`. The token is a 30-minute Data Protection token over `type:id` and is the
    whole credential, so it is never logged, stored in the browser or accepted for a write (ADR 0036).
  - The answer carries the record as the site's own model — the one its published page renders — read through the
    module's `GetForPreviewAsync` whatever its status, and never cached (ADR 0042).
  - Everything unusable — expired, tampered with, or a record since deleted — is one `resource.not_found`, so a
    holder learns nothing from the answer. The answer is `no-store` and `X-Robots-Tag: noindex, nofollow`.
  - A new publishable type joins `PreviewPolicy.EntityTypes` and `SitePaths` when it lands.

## Content rules

- **HTML** is sanitized on write by the shared `IHtmlSanitizer` (`Managers/Html/`, ADR 0034), in Business, on every write path (seeds and the importer included); only its output is stored. Scripts, styles and inline event handlers are never stored.
  - The allow-list is `HtmlPolicy`, one list for every HTML field (ADR 0045): `p`, `h2`, `ul`, `ol`, `li`, `strong`, `em`, `br`, with no attributes. Other heading levels become `h2`; a link keeps its words; a picture or quote is removed (a quote's paragraphs stay). Widening it needs an ADR, the sanitizer suite's payloads extended, and `<cms-rich-text>`'s toolbar changed to match.
  - **No pictures or links in HTML at this time** (ADR 0045): a record's pictures are its hero or gallery. The picture support from ADR 0034 (the `*MediaReferences` tables with their `Restrict` key, the facades' Media checks, the alt-text rule) stays in place, unused, for when pictures come back.
  - Long-text fields that predate the sanitizer (the artist bio, the exhibition body) stay plain text until a prompt converts them.
- **Square-owned fields** (price, stock or seats, sold-out) are read-only projections on the CMS record, maintained by the `SquareSync` module. CMS write models don't contain them, and a test proves it per type (`ClassWriteModelTests`).
  - The *starting* price is the CMS's own draft field, and it stays editable only until Square has reported a price
    for the record (`startingPriceEditable`, ADR 0037). After that a change is the 5.4a command.
  - **A membership plan's starting *cadence* is the CMS's too**, and freezes on the same gate (ADR 0005, 0040): the
    first push creates the Square plan with both figures. `MembershipCadence` holds the two the centre sells, while
    the `Cadence` projection is a string of Square's own word, because Square has cadences the CMS does not offer.
    A plan has no change-price command at all: after the first push, price and cadence are changed in Square.
- **SKUs are unique across the CMS**, not per type: the shared kernel's `CatalogSkus` registry (`Managers/Catalog/`,
  ADR 0037) holds one row per SKU with its owner's type and id, so one unique key covers class runs (ADR 0044) and
  shop items. A content module's DataLayer reserves and releases through `ISkuRegistry` in the **same save** as
  the row that carries the SKU, and a clash is `cms.sku_taken`. The CMS suggests one on create
  (`CLS-<offering>-<n>`, `ART-<n>`) and staff may edit it.
- **A class run stores its display time zone** (the gallery's unless staff change it) and returns it as
  `displayTimeZone`, because its sessions' times are stored UTC and read as a wall clock (backend.md).
- **A class run names its instructor by id** (ADR 0044): the facade asks the Instructors facade which exist and which
  are archived, and Business refuses an unknown one, and an archived one for a run that did not already have them.
  Publishing needs every run to have an instructor, a session and a starting price. A run Square holds can't be
  removed and its starting price can't change in place (5.4a).
- **Shop categories are a fixed allow-list** (`ShopPolicy.Categories`, ADR 0038): the six the site filters on, each
  with its label. A write is validated against it, and widening it is a line of code plus an ADR note — not a field
  staff type. The admin mirrors the labels (`SHOP_CATEGORIES`) as it mirrors every policy's numbers.
- **A shop item's gallery is ordered, and its first photograph leads** (the card, the Open Graph image, and the
  first image the 5.x push sends). Every photograph needs alt text before the item is published, because the product
  page shows them all.
- **One of a kind means a starting quantity of one** (ADR 0032): a count other than one sent with the flag is
  refused rather than quietly replaced, and the flag and the count freeze together once Square holds the record
  (`startingStockEditable`, ADR 0038).
- **Import.** `Arts6311.Importer` creates Draft records with legacy WordPress ids for idempotent re-runs and provenance. It never overwrites a record a staff member has edited since import (compares `UpdatedBy`).
- **Copy and SEO.** Editors write titles and descriptions. The site's `SeoService` and JSON-LD come from these fields, so required SEO fields are validated at publish, not at draft.
