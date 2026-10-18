# 6311arts — WordPress Import SCRUB Prompts ("prime the pump")

Atomic prompts that build `Arts6311.Importer`. It pulls everything from the live WordPress/WooCommerce site into blob storage and the CMS database, locally first and then into Azure, and can re-run safely until cutover.

This file replaces Phase 4 (4.1 and 4.2) of `docs/scrub-prompts.md`. Run it after **Phase 2** (the CMS modules exist) and **3.1** (site read API). Run **I.9** once Square Phase 5.3 is done.

**Rule:** `.claude/rules/import.md`
**Skill:** `.claude/skills/add-import-mapping`
**Reviewers:** `@architecture-reviewer`, `@outbox-reviewer`, `@security-reviewer`, `@test-gap-analyzer`

## What the live site exposes (checked 2026-10-18)

| Source | Access | What it gives us |
|---|---|---|
| `wp/v2/media` | Public | Every upload: `source_url` (full original), `mime_type`, `alt_text`, parent post, dates, sizes. Many have **empty alt text**, so that's a review queue. |
| `wp/v2/pages`, `wp/v2/posts`, `wp/v2/types`, `wp/v2/taxonomies` | Public | Pages (privacy, terms), any custom post types registered with REST (CPT UI is installed) |
| `wc/store/v1/products` | Public | Products, prices, categories (artists are product categories), images, stock status |
| `wc/v3/products` (+ `meta_data`, variations) | **WooCommerce key, read-only** | Exact stock, attributes (instructor, level, capacity…), FooEvents date/time meta |
| `fooevents/v1/events` | Discover in I.1 | FooEvents event list |
| MailPoet subscribers | **Admin export (CSV)** | Newsletter list with consent; only if consent is present |

The spike's `6311/v1` endpoints are **not** installed on the live site. The importer reads the native endpoints instead; the spike's PHP is logic reference only.

## The pipeline

```text
I.1 discover ─> I.2 credentials ─> I.3 extract (snapshot) ─> I.4 media download
     ─> I.5 staging tables ─> I.6 media load (blob + renditions via outbox)
     ─> I.7 content mappings (one prompt per type) ─> I.8 load orchestration + report
     ─> I.9 Square match ─> I.10 newsletter (optional) ─> I.11 verify
     ─> I.12 run in Azure dev ─> I.13 delta refresh ─> I.14 cutover freeze + final delta
```

---

## I.1 — Source discovery

```
SCOPE: Read-only reconnaissance of https://6311arts.com, written to docs/runbooks/wp-discovery.md: the
REST namespaces; wp/v2/types and taxonomies (which custom post types exist — artists? exhibitions? —
and whether they're REST-enabled); record counts per endpoint (X-WP-Total) for media, pages, posts,
each CPT, Store API products, product categories; whether fooevents/v1/events is public and its shape;
which WooCommerce product fields we need (FooEvents date meta, attributes, stock quantity) that the
Store API does NOT expose; MailPoet presence. Sample one record per type (scrub personal data).

CONSTRAINT: .claude/rules/import.md (GET only, ≤2 concurrent, polite User-Agent). Use a throwaway
script or curl — nothing committed except the report.

RESTRICTION: No authenticated calls yet. No downloads of media binaries. No writes anywhere but docs/.

UTILIZATION: WebFetch / curl; plan mode.

BEHAVIOR: Show the list of URLs you'll call; wait. Run, write the report, and end it with a
recommendation: which data needs a read-only WooCommerce key or Application Password, and whether a
WXR export or an uploads-folder copy from the host is worth requesting.
```

## I.2 — Importer project + read-only credentials

```
SCOPE: Create Arts6311.Importer (.NET console, System.CommandLine) registered in the AppHost as an
explicit-start resource referencing appdb and blobs (WaitForCompletion(migrations)). Commands (stubs
for now): discover, extract, stage, load, match-square, verify, report. Add AppHost secret parameters
wp-wc-consumer-key, wp-wc-consumer-secret, wp-app-user, wp-app-password, created only when
Import:Authenticated=true and passed ONLY to the importer. Typed HttpClients for the public and
authenticated WordPress APIs (base URL from config, timeout, Retry-After, 2-concurrency limiter,
User-Agent "6311arts-importer").

CONSTRAINT: import.md; aspire.md; add-aspire-resource; external.md. Extend .claude/hooks/secret-guard.sh
to block WooCommerce key shapes (ck_…, cs_…).

RESTRICTION: Credentials never in appsettings, logs, snapshot files or the report. Importer is not
published to Azure yet (I.12). No content writes yet.

UTILIZATION: add-aspire-resource, add-external-integration; @security-reviewer.

BEHAVIOR: Plan, wait, implement, show the importer starting from the dashboard and printing its
command list. Document in docs/runbooks/wp-import.md how to create the read-only WooCommerce key and the
read-only Application Password user, and how to revoke both after cutover.
```

## I.3 — Extract: JSON snapshot

```
SCOPE: `importer extract` writes artifacts/wp-snapshot/<UTC timestamp>/ with raw JSON for every
endpoint I.1 identified, one file per page (e.g. media/page-0001.json, wc-v3-products/page-0003.json),
plus manifest.json (endpoint, page, count, SHA-256, extractedAt). Resumable: re-running fills missing
pages only. --modified-after <iso> makes a delta snapshot that records its base snapshot. Add
artifacts/ to .gitignore.

CONSTRAINT: import.md (Extract). Paginate via X-WP-Total/X-WP-TotalPages; request only fields needed
(_fields) where the API supports it.

RESTRICTION: GET only. No transformation in this step — raw responses as received. No binaries yet.

UTILIZATION: add-external-integration.

BEHAVIOR: Plan the endpoint list and file layout; wait. Implement with tests against a canned HTTP
handler (pagination, 429 + Retry-After, resume). Run it for real and show the manifest totals vs I.1
counts.
```

## I.4 — Extract: media originals

```
SCOPE: `importer extract --media` downloads each media item's full original (source_url) into the
snapshot's files/ folder named <wpMediaId>-<slug>.<ext>, verifies size and SHA-256, records them in
the manifest, skips WordPress-generated sizes, and is resumable (hash check, temp file + atomic
rename). Also download images referenced inside page/product HTML that aren't in the media library
(report them).

CONSTRAINT: import.md; 2 concurrent downloads max.

RESTRICTION: No uploads to blob yet. Don't follow off-site URLs except the WordPress/Jetpack CDN host
for the same files.

UTILIZATION: add-external-integration.

BEHAVIOR: Plan, wait, implement with tests (resume after a cut-off download, checksum mismatch
retried). Run and report: files, total bytes, failures, media without alt text.
```

## I.5 — Import module: staging tables

```
SCOPE: Modules/Import in Arts6311.Domain: ImportRun, ImportItem (SourceType, SourceId, PayloadHash,
Status Staged/Loaded/Skipped/Failed/NeedsReview, TargetType, TargetId, ErrorCode, Notes),
ImportMediaMap (WpMediaId, MediaAssetId, Checksum). `importer stage <snapshot>` reads the snapshot and
upserts ImportItems (payload hash per record) inside one ImportRun. Admin read-only "Import" screen:
runs, per-type counts, NeedsReview queue with links to the target record.

CONSTRAINT: import.md; backend.md; add-endpoint for the admin read routes (CmsAdmin).

RESTRICTION: Staging stores hashes and ids, not personal data. Migration reviewed before apply.

UTILIZATION: add-endpoint, new-component; @architecture-reviewer.

BEHAVIOR: Plan, wait, implement, migration review, stage the real snapshot, show counts per type.
```

## I.6 — Load media into blob storage

```
SCOPE: `importer load --type media`: for each staged media item, upload the original through the Media
facade's import path (kind Imported, source URL and import date recorded, checksum dedupe, EXIF GPS
stripped, alt text from WordPress or flagged missing) — which stores it in the `media` blob container
and stages `media.rendition-requested` in the same save. Write ImportMediaMap. Re-runs skip by checksum.

CONSTRAINT: import.md; media.md; add-media-feature; outbox.md (renditions via the outbox, never
inline).

RESTRICTION: No direct blob client use in the importer — only the Media facade. No public blob access.

UTILIZATION: add-media-feature; @outbox-reviewer.

BEHAVIOR: Plan, wait, implement; tests: duplicate upload deduped, re-run no-op, missing alt text
flagged. Run against Azurite and show the media library in admin with renditions appearing.
```

## I.7 — Content mappings (run once per type, in this order)

```
SCOPE: Add the import mapping for <TYPE> using add-import-mapping, and `importer load --type <TYPE>`.
  TYPE = artists        (product categories that aren't type categories — reuse the spike's
                          TYPE_CATEGORIES list — or the CPT if I.1 found one; portrait from media)
       | exhibitions    (CPT or pages per I.1; dates, reception, artists, hero)
       | classes        (FooEvents products → ClassOffering + ClassSession rows from date/time meta;
                          attributes instructor/type/level/sessions/cadence/capacity; starting price
                          and starting capacity only)
       | shop-items     (non-class, non-membership products; artist category; gallery via media
                          map; starting price; starting stock; one-of-a-kind when stock = 1)
       | membership-plans (products in the memberships category; perks from description; starting
                          price)
       | pages          (privacy, terms and any copy pages I name)
       | redirects      (every old permalink: /product/<slug>, /artist/<slug>, /events, /gallery,
                          /submit-your-work, page permalinks → new routes; source = Imported)

CONSTRAINT: import.md; content.md; add-import-mapping (mapping table first, golden tests from real
snapshot records). Body HTML rewritten through ImportMediaMap, shortcodes/builder markup stripped and
reported, then sanitized.

RESTRICTION: Everything lands as Draft. Never publish, never call Square, never write Square-owned
projections. Staff-edited targets become NeedsReview, never overwritten. Facades only.

UTILIZATION: add-import-mapping; @architecture-reviewer.

BEHAVIOR: Show the mapping table and the golden fixtures; wait. Implement, run the load for this type
locally, show counts (loaded/skipped/NeedsReview) and 3 records in the admin.
```

## I.8 — Load orchestration + report

```
SCOPE: `importer run <snapshot>` = stage → load media → load each type in dependency order → report.
Each step is resumable; a failed item doesn't stop the run. Write docs/runbooks/import-report-<run>.md
per import.md (counts source vs target, skips/failures/NeedsReview with reasons, missing alt text,
stripped shortcodes, broken internal links, orphan media, slug collisions). `--dry-run` stages and maps
without loading.

CONSTRAINT: import.md; add-background-job (checkpointed operation semantics).

RESTRICTION: No publishing. No Square.

UTILIZATION: @test-gap-analyzer.

BEHAVIOR: Plan, wait, implement; run twice against the same snapshot and prove the second run changes
nothing. Show the report.
```

## I.9 — Square catalog match (run after Square Phase 5.3)

```
SCOPE: `importer match-square`: read the Square catalog (ISquareCatalogGateway, read-only) and propose
links for imported shop items, classes/sessions and membership plans — by SKU, then exact name, then
normalized name + price. Write proposals to a review table; admin screen to approve/reject in bulk;
approval creates SquareCatalogLink rows (status Linked, Square version recorded) so the first publish
UPDATES the existing Square object. Report unmatched on both sides.

CONSTRAINT: square.md; import.md (Square matching); add-endpoint for the approval routes (CmsAdmin,
audited).

RESTRICTION: Read-only against Square. No catalog writes. No automatic linking without approval.
Fake or sandbox only in run mode.

UTILIZATION: @integration-reviewer.

BEHAVIOR: Plan the matching rules; wait. Implement with tests on the Square fake seeded with near-miss
names. Show the proposal screen.
```

## I.10 — Newsletter subscribers (optional)

```
SCOPE: `importer load --type newsletter --file <mailpoet-export.csv>`: import subscribers that have a
consent timestamp into NewsletterSignup (source Imported, original consent date kept); everything else
reported and skipped.

CONSTRAINT: import.md (Personal data).

RESTRICTION: No consent → no import. The CSV is never committed; delete it after loading. No emails
sent to imported subscribers.

UTILIZATION: @security-reviewer.

BEHAVIOR: Plan, wait, implement, run, report counts, confirm the file was removed.
```

## I.11 — ✅ Verify the local import

```
SCOPE: `importer verify`: (1) every Redirect resolves 301 → 200 through the gateway; (2) every
imported page renders on the local site (as preview, since content is Draft) with no broken images;
(3) SEO parity on 20 sampled pages vs the live WordPress pages (title, description, canonical); (4) media
renditions all present. Append results to the run report. Then run every reviewer on the importer.

CONSTRAINT: import.md (Verify); frontend.md.

RESTRICTION: Read-only.

UTILIZATION: playwright-cli; @architecture-reviewer, @outbox-reviewer, @security-reviewer,
@test-gap-analyzer.

BEHAVIOR: Report the verification table and reviewer BLOCKERs; wait before fixing.
```

## I.12 — Run the import in Azure dev

```
SCOPE: Publish the importer as a Container Apps job (manual trigger) with managed identity to Azure SQL
and Storage and Key Vault references for the WordPress credentials. Move the snapshot to a private
`import-snapshots` blob container (the job reads from there, not from a laptop). Runbook steps to run
it against dev and read the report.

CONSTRAINT: aspire.md (publish mode); aspire-deployment skill.

RESTRICTION: No `aspire deploy` from the session; show me commands, I run them. Snapshot container
private; delete snapshots after cutover per the runbook.

UTILIZATION: aspire-deployment; @security-reviewer.

BEHAVIOR: Plan, wait, implement the publish mapping + runbook; after my deploy, run verify against dev.
```

## I.13 — Delta refresh

```
SCOPE: `importer extract --modified-after <last run>` + `run` loads only changed records: unchanged hash
→ skip; changed and untouched by staff → update; changed and staff-edited → NeedsReview. New WordPress
media and pages since the last run are picked up.

CONSTRAINT: import.md (idempotency, never overwrite staff edits).

RESTRICTION: Deletions in WordPress are reported, never applied automatically.

UTILIZATION: @test-gap-analyzer.

BEHAVIOR: Tests for each branch; run a real delta locally; report.
```

## I.14 — ✅ Cutover freeze and final delta

```
SCOPE: Add to docs/runbooks/cutover.md: announce a WordPress content freeze; final delta extract +
run in prod; Square match approval; bulk publish of approved content (staged pushes drain through the
outbox — watch the Sync screen); verify; DNS switch (Phase 6.3); revoke the WooCommerce key and
Application Password; delete snapshots containing personal data; keep WordPress read-only for N days.

CONSTRAINT: import.md; square.md.

RESTRICTION: Every irreversible step waits for my explicit go in-session.

UTILIZATION: all reviewers.

BEHAVIOR: Draft the checklist with owners; wait; rehearse it end-to-end in dev and report timings.
```

---

## Template — Fix one import mapping

```
SCOPE: Imported <type> record(s) <ids> map wrong: <symptom>.
CONSTRAINT: add-import-mapping; import.md.
RESTRICTION: Fix the pure mapper + add a golden fixture reproducing it; re-run only that type; staff-
edited targets stay NeedsReview.
UTILIZATION: @test-gap-analyzer.
BEHAVIOR: Reproduce from the snapshot, show the failing golden test, wait, fix, re-run the type,
report changed counts.
```
