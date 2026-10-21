---
paths:
  - src/Arts6311.Importer/**
  - src/Arts6311.Domain/Modules/Import/**
---
# WordPress Import ("prime the pump")

Moving content off WordPress is a one-way, repeatable pipeline in three stages. Each stage reads only the previous stage's output:

```text
EXTRACT  WordPress REST (+ optional WXR export / uploads copy) ──> immutable local snapshot (JSON + files + manifest)
STAGE    snapshot ──> Import module tables (ImportRun, ImportItem, ImportMediaMap) with hashes and statuses
LOAD     staged items ──> module facades (Media, Artists, Exhibitions, Classes, Shop, Memberships, Pages, Redirects)
VERIFY   counts, links, images, redirects, SEO parity ──> report + admin review queue
```

## Extract

- **WordPress is read-only to us.**
  - Only GET requests.
  - Two concurrent requests at most, with a polite `User-Agent` naming 6311arts.
  - Honour `Retry-After`.
  - Paginate with `X-WP-Total` / `X-WP-TotalPages`.
  - Never use `wp-admin` scraping.
- **Credentials.** Anything the public endpoints don't expose uses read-only credentials, as Aspire secret parameters that only the Importer receives:
  - a WooCommerce REST key with *Read* permission
  - an Application Password for a dedicated read-only user

  Never commit them. Revoke them after cutover.
- **The snapshot** lives in `artifacts/wp-snapshot/<UTC timestamp>/`, which is gitignored.
  - It holds raw JSON per endpoint page, and media **originals** only (WordPress-generated sizes are skipped; we make our own renditions).
  - `manifest.json` records: endpoint, page, record count, SHA-256 per file, source URL, and an `extractedAt` value per record.
- **Snapshots are immutable.** A re-extract creates a new snapshot. A **delta** extract (`modified_after`) records its base snapshot.
- **Downloads are resumable** by checking manifest hashes. A partial file never counts as done.

## Stage and load

- **The `Import` module owns the staging tables:**
  - `ImportRun`: snapshot id, mode, timestamps, counts
  - `ImportItem`: source type, source id, payload hash, status (`Staged` / `Loaded` / `Skipped` / `Failed` / `NeedsReview`), target entity type and id, error code
  - `ImportMediaMap`: WordPress media id ↔ `MediaAssetId` + checksum
- **The loader writes only through module facades.** Never through `DbContext` or repositories of other modules. Media goes through the Media facade, so blob storage, checksum dedupe and the `media.rendition-requested` outbox all apply.
- **Idempotent, keyed on the WordPress id.** For each item:
  - Unchanged payload hash: skip.
  - Changed payload, and the target hasn't been edited by staff since import (`UpdatedBy` = importer): update it.
  - Changed payload, and the target *has* been edited by staff: `NeedsReview`. Never overwrite.
- **Load in dependency order:** Media → Artists → Instructors → Exhibitions → Classes (+ runs and sessions) → Shop items → Membership plans → Pages → Redirects.
- **Imported content lands as `Draft`.** Publishing is a deliberate staff step (or an approved bulk publish), so imports never flood Square pushes.
- **Prices and stock.** WooCommerce price and stock import only into the **starting** fields (`StartingPriceCents`, starting quantity, run capacity). They're used once, when the item is first created in Square. They never touch Square-owned projections. The WooCommerce SKU, if there is one, imports into `Sku`.
- **Merch is never imported** (ADR 0032). Products identified as merch in the mapping table are reported as `Skipped (merch: POS-only)`.
- **Body HTML** is rewritten and sanitized:
  - Shortcodes and Beaver Builder markup are stripped, and listed in the report.
  - The shared sanitizer runs last. It keeps paragraphs, one heading level, bold, italic and lists (ADR 0045): links keep their words, and inline pictures are removed and listed in the report. A WordPress post's featured image is the record's hero or gallery, through `ImportMediaMap`; rewriting `wp-content/uploads/...` URLs inside the body is not needed while pictures are off.

## Square matching (when 6311 already sells in Square)

Before any CMS item is pushed, a **match pass** reads the Square catalog (read-only) and proposes links by SKU, then exact name, then a normalized name plus price. Approved matches create `SquareCatalogLink` rows (`Linked`), so the first publish **updates** the existing Square item instead of creating a duplicate. Unmatched items are listed for staff.

## Personal data

- **Newsletter subscribers** import only from a MailPoet export that includes consent timestamps. No consent, no import.
- **Customer accounts and passwords are never migrated.** Members re-register; Square keeps customer records.
- **Snapshots containing personal data** are deleted after cutover. The runbook records when.

## Verify

Every load ends with `docs/runbooks/import-report-<run>.md`:

- source vs target counts per type
- skips, failures and `NeedsReview` with reasons
- images missing alt text
- broken internal links
- orphan media
- slug collisions
- a redirect crawl (every old URL → 301 → a 200 page)
- SEO parity for a sample of 20 pages (title, description, canonical)
