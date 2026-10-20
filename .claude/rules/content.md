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
| `ClassOffering` | Title, slug, summary, body, type, level, instructor, hero, cadence; has `ClassSession` rows (start, end, display zone, starting capacity). **Square-synced.** |
| `ShopItem` | Title, slug, description, artist, categories, gallery, one-of-a-kind flag, starting price. **Square-synced.** |
| `MembershipPlan` | Name, slug, perks, class discount %, featured, order, starting price. **Square-synced.** |
| `Page` | Slug, title, sanitized HTML body (privacy, terms, about copy blocks) |
| `Redirect` | From path, to path, 301/302, source (`Imported` / `Manual`) |

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
- **Slugs** are unique per type and immutable once published. A slug change after publish creates a `Redirect`.
- **Shared kernel** (ADR 0031): `Managers/Publishing/` holds `PublishStatus`, the transition table `PublishLifecycle.Next`, `Slugs` and `ContentErrorCodes`; `Managers/Concurrency/EntityTag` verifies `If-Match`. A content module uses these rather than its own.
  - Status commands carry `If-Match` too. An illegal move (publishing what is published, anything from Archived) is `cms.status_transition_invalid`, never a silent no-op.
  - `PublishedAt` is the **first** publish and survives an unpublish; it is what locks the slug. Until Redirects (2.7), a slug change on a record ever published is refused with `cms.slug_locked`.
  - **Dated exception to the revalidation rule above, until 2.8 (ADR 0031):** Artists' status commands stage no outbox message yet, because nothing outside the CMS reads content before 3.1. The skipped test `PublishStagesARevalidationInTheSameSave` holds the place. It is not a precedent for any other type.
  - An image on a Published or Scheduled record needs alt text when it is attached, not only when the record is published.
- **Previews** use a signed, expiring token, render `noindex`, never enter the sitemap and grant no write access.

## Content rules

- **HTML** is sanitized on write (an allow-list). Scripts and inline event handlers are never stored. Until the sanitizer exists (2.6), a long-text field is plain text (the artist bio is), never HTML.
- **Square-owned fields** (price, stock or seats, sold-out) are read-only projections on the CMS record, maintained by the `SquareSync` module. CMS write models don't contain them.
- **Import.** `Arts6311.Importer` creates Draft records with legacy WordPress ids for idempotent re-runs and provenance. It never overwrites a record a staff member has edited since import (compares `UpdatedBy`).
- **Copy and SEO.** Editors write titles and descriptions. The site's `SeoService` and JSON-LD come from these fields, so required SEO fields are validated at publish, not at draft.
