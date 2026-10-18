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
- **Previews** use a signed, expiring token, render `noindex`, never enter the sitemap and grant no write access.

## Content rules

- **HTML** is sanitized on write (an allow-list). Scripts and inline event handlers are never stored.
- **Square-owned fields** (price, stock or seats, sold-out) are read-only projections on the CMS record, maintained by the `SquareSync` module. CMS write models don't contain them.
- **Import.** `Arts6311.Importer` creates Draft records with legacy WordPress ids for idempotent re-runs and provenance. It never overwrites a record a staff member has edited since import (compares `UpdatedBy`).
- **Copy and SEO.** Editors write titles and descriptions. The site's `SeoService` and JSON-LD come from these fields, so required SEO fields are validated at publish, not at draft.
