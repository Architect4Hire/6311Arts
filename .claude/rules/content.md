---
paths:
  - src/Arts6311.Domain/Modules/Artists/**
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
| `Exhibition` | Title, slug, summary, body, start/end, reception start/end/note, hero, artists (M:N) |
| `ClassOffering` | Title, slug, summary, body, type, level, instructor, hero, cadence, starting price; has `ClassSession` rows (start, end, display zone, SKU, capacity). **Square-synced** (ADR 0032). |
| `ShopItem` | Artwork only (merch is POS-only, ADR 0032). Title, slug, description, artist, categories, gallery, SKU, one-of-a-kind flag, starting price, starting quantity. **Square-synced.** |
| `MembershipPlan` | Name, slug, perks, class discount %, featured, order, starting price. **Square-synced.** |
| `Page` | Slug, title, sanitized HTML body (privacy, terms, about copy blocks) |
| `Redirect` | From path, to path, 301/302, source (`Manual` / `Imported` / `SlugChange`). No lifecycle: live once written (ADR 0035) |

## Lifecycle

```text
Draft → Scheduled → Published → Archived
          └──────────┘ (unschedule)
```

- **Every publishable type has:** `Status`, `PublishedAt`, `ScheduledFor` (UTC), `UpdatedAt`, `UpdatedBy`, `RowVersion`.
- **Transitions are explicit commands** (`publish`, `unpublish`, `schedule`, `archive`) and are audited. A `PATCH` never changes status.
- **Square-synced types:**
  - Publish, update and archive stage the matching `square.*` outbox message in the same save.
  - Sync status is shown, never inferred from content status.
- **Every publish or unpublish stages `site.revalidate-requested`,** so cached SSR pages and API cache tags refresh.
- **Slugs** are unique per type. A slug change on a record that has ever been published redirects its old address to the new one (ADR 0035): the content module's DataLayer stages `redirects.slug-changed` in the rename's own save, and the Redirects module applies it.
- **Shared kernel** (ADR 0031): `Managers/Publishing/` holds `PublishStatus`, the transition table `PublishLifecycle.Next`, `Slugs` and `ContentErrorCodes`; `Managers/Concurrency/EntityTag` verifies `If-Match`. A content module uses these rather than its own.
  - Status commands carry `If-Match` too. An illegal move (publishing what is published, anything from Archived) is `cms.status_transition_invalid`, never a silent no-op.
  - `PublishedAt` is the **first** publish and survives an unpublish; it is what makes a slug change owe a redirect (`PublishedPathChange.For`). Since 2.7 nothing returns `cms.slug_locked`; the code stays in the contract, and the ServiceModels' `slugLocked` now means "changing it redirects".
  - Each type's site address is its policy's `SitePathPrefix` plus the slug: `/artists/`, `/exhibitions/`, `/` for pages.
- **Redirects** (ADR 0035):
  - Addresses are stored in one normal form (`RedirectPaths`): a lower-case site path with no trailing slash, plus any query. The site normalizes an incoming path the same way before it looks one up.
  - An old address can't be the home page, have a fragment, or sit under a reserved prefix (`/admin`, `/api`, `/bff`, `/media`, `/webhooks`, `/health`, `/alive`, `/signin`, `/account`).
  - A destination is a site path or an `https://` address.
  - **Staff writes refuse loops and chains longer than one hop** (`redirects.loop_or_chain`).
  - **Slug changes keep every chain one hop instead of refusing.** Redirects arriving at the old address are repointed. A redirect leaving an address that is live again is retired. Each row's `EffectiveAt` makes out-of-order delivery safe.
  - Every change is Admin-only and audited. The site's list is cached for 5 minutes until 2.8's revalidation.
  - **Dated exception to the revalidation rule above, until 2.8 (ADR 0031, 0033, 0034):** Artists', Exhibitions' and Pages' status commands stage no outbox message yet, because nothing outside the CMS reads content before Phase 3. A skipped test `PublishStagesARevalidationInTheSameSave` in each module holds the place. It is granted per type, by ADR, and is not a precedent for any other type.
  - **Cross-module content** (ADR 0033): a type that credits another's records (an exhibition's artists) holds a join table with a `Restrict` foreign key to the other's entity, and asks the other's facade who the records are and whether they can be linked. An archived record gains no new links.
  - An image on a Published or Scheduled record needs alt text when it is attached, not only when the record is published.
- **Previews** use a signed, expiring token, render `noindex`, never enter the sitemap and grant no write access.

## Content rules

- **HTML** is sanitized on write by the shared `IHtmlSanitizer` (`Managers/Html/`, ADR 0034), in Business, on every write path (seeds and the importer included); only its output is stored. Scripts, styles and inline event handlers are never stored.
  - The allow-list is `HtmlPolicy`: `p`, `h2`–`h4`, lists, `strong`, `em`, `a[href]`, `br`, `blockquote`, `img[src][alt]`. Links are site-relative, `#`, `http(s):`, `mailto:` or `tel:`; images are library renditions (`/media/{id}/{rendition}`) only. Widening it needs an ADR and the sanitizer suite's payloads extended.
  - **Every picture in HTML is a library original with its own `alt`,** and the content module records it in a reference table with a `Restrict` key to `MediaAsset` (as `PageMediaReferences`), kept in step with the body on each write, so Media's delete guard sees it.
  - Long-text fields that predate the sanitizer (the artist bio, the exhibition body) stay plain text until a prompt converts them.
- **Square-owned fields** (price, stock or seats, sold-out) are read-only projections on the CMS record, maintained by the `SquareSync` module. CMS write models don't contain them.
- **Import.** `Arts6311.Importer` creates Draft records with legacy WordPress ids for idempotent re-runs and provenance. It never overwrites a record a staff member has edited since import (compares `UpdatedBy`).
- **Copy and SEO.** Editors write titles and descriptions. The site's `SeoService` and JSON-LD come from these fields, so required SEO fields are validated at publish, not at draft.
