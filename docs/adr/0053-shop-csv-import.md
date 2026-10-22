# 0053 — Shop CSV import: drafts only, create-only, all or nothing

- **Status:** Accepted
- **Date:** 2026-10-22
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/gateway.md`, `.claude/rules/api-contract.md`
- **Builds on:** [0020](0020-idempotency-scoped-to-a-server-owned-subject.md) (idempotent commands),
  [0032](0032-website-items-managed-from-the-cms.md) (field ownership with Square),
  [0034](0034-html-sanitizer-and-pages.md) (every HTML write sanitized),
  [0037](0037-classes-and-the-shared-catalog-sku-registry.md) (the shared SKU registry),
  [0038](0038-shop-items-artwork-categories-and-one-of-a-kind.md) (shop items),
  [0052](0052-soft-delete-for-cms-content.md) (a deleted item frees its slug and SKU)

## Context

Staff add artwork a season at a time, often dozens of pieces from one artist, and typing each into the editor is
slow. They keep the list in a spreadsheet already. They asked to upload that spreadsheet to create the pieces in
one go, and for a template showing the format.

This is not the WordPress importer (`Arts6311.Importer`, `.claude/rules/import.md`). That is a one-off migration
with legacy ids and idempotent re-runs. This is a staff tool in the CMS, used again and again.

Four facts shaped the design:

- **Square owns money and stock** (ADR 0032). A shop item is pushed to Square when it is published, and its
  starting price and count freeze once Square reports them. A bulk tool that touched a live item could get round
  those gates.
- **A spreadsheet can't carry photographs**, and every piece needs a described photograph before it can be published.
- **Half an import is worse than none.** If row 400 of 600 fails, staff should not have to work out which 399 went
  in.
- **A double-click, or a retry after a timeout, must not create the whole file twice.**

## Decision

### What an import does

- **Every row creates a new Draft.** It never updates an existing item, and never publishes one. Nothing goes to
  Square or the site until someone publishes the piece from the editor, which still checks for a description, a
  price, a count and a described photograph. That keeps every ADR 0032 gate where it is.
- **There is no images column.** Staff add photographs in the editor.
- **All or nothing.** One `SaveChangesAsync` writes every item, its categories, its SKU reservation (ADR 0037) and
  an audit row per piece (`shop-item.imported`, naming the file and the row). One bad row refuses the whole file.
- **At most 1,000 rows** (`ShopPolicy.ImportMaxRows`) and 2,000,000 characters (`ImportMaxCharacters`).
- **No outbox message**, the same as a single create: a Draft is on no page to revalidate and in no Square catalog.

### Preview, then import

- `POST /api/v1/cms/shop/import/preview` reads every row and returns each one as it would be created: its slug, its
  SKU (marked when it is a suggestion), the artist it matched, and all of its problems at once, keyed by CSV column.
  It writes nothing.
- `POST /api/v1/cms/shop/import` runs the same checks again and creates the pieces, but only when no row has a
  problem. Row problems come back as the standard `validation.failed`, keyed `rows[n].column`, where n is the
  spreadsheet row (the header is row 1). Problems with the file itself — an unknown or repeated column, no `title`
  column, no rows, too many rows, text that isn't UTF-8 — are keyed `csv` on both routes. There is no new error code.
- The import is an **idempotent command** (ADR 0020), and the first route to use the executor. It requires
  `Idempotency-Key`, and only the CSV text is fingerprinted, so the same key with another file is
  `idempotency.key_reused` (422). The admin makes one key per file it reads.
- Both routes are `CmsEditor`, the same as a single create.

### The format

| Column | Rule |
|---|---|
| `title` | Required |
| `sku` | Blank means the next free `ART-<n>`. Otherwise it is normalized as the registry stores it, must be free, and may appear only once in the file |
| `artist` | The artist's slug, or else their exact name, ignoring case. No match, several matches, or an archived artist is a row problem |
| `categories` | `\|`-separated, each a category's slug or its label |
| `one_of_a_kind` | true / false / yes / no / blank. When true, `starting_stock` must be blank or 1 |
| `starting_price` | Dollars (`45`, `45.00`, `$1,250`), stored as cents, capped at `StartingPriceMaxCents` |
| `starting_stock` | 1 to 1,000 |
| `slug` | A slug given in the file is kept, or refused if it is taken. One made from the title takes the next free `-2`, `-3`, because two prints may well both be called "Untitled" |
| `description` | Plain text: a blank line starts a paragraph and a line break stays one. Every character is encoded, then the shared sanitizer runs as on every write (ADR 0034) |

Headers are read without regard to case or order, and with spaces and hyphens read as underscores. Only `title` is
required. The admin builds the template download in the browser from the same column list, with a UTF-8 BOM so
Excel opens it correctly. There is no server route for the template.

### Where the code sits

- **CSV handling is shared kernel code:** `Managers/Csv/CsvReader` (RFC 4180, with row numbers that count records,
  not text lines) sits beside the writer, `CsvFormat`, which moved there from Forms.
- **A facade of its own:** `IShopImportFacade` reads the file into a `ShopImportSheet` and asks the Artists facade
  about the artist column. That needed a new `FindByNamesAsync`, which matches on `UPPER()` so case is ignored on
  SQLite as it is under SQL Server's collation. It then calls Business inside the executor.
  - It is a separate facade, registered by `AddShopImport()` in the API only, because the executor exists only
    there. The Worker and MigrationService compose the Shop module and validate their DI on build.
- **Business** reads each row against the same rules a single create applies (`ShopImportCells` for the cells),
  then checks the rows against each other and against the database with one read per question:
  - the live slugs
  - the SKU registry, a chunk at a time
  - the next item number
- **The DataLayer's `ImportAsync`** makes the one save. It reports a slug or SKU taken by a concurrent write as
  `cms.slug_taken` / `cms.sku_taken`, as a single create does.

### The edge

A thousand rows as JSON text outgrow the 1 MiB request limit every route has, so the gateway gets one more route,
`cms-shop-import` (`POST /api/v1/cms/shop/import/**`, `CmsEditor`, token-bearing). Its `MaxRequestBodySize` and the
API's `[RequestSizeLimit]` both come from `ShopPolicy.ImportMaxRequestBytes`: 8 MiB, which covers the character cap
at three UTF-8 bytes each plus JSON escaping. The body is read whole rather than streamed. It is bounded, and it
binds like any other ViewModel.

The import runs synchronously, not as the `202` operation api-contract.md asks of "imports". That rule is written
for the WordPress import and full reconciliation, which run for minutes. This one is bounded at a thousand inserts
in a single save, well inside a request.

## Options considered

- **Upsert by SKU.** Rejected. It would write live items past the ADR 0032 gates, could overwrite a staff member's
  edit with an old spreadsheet, and makes "what did this file do" hard to answer. Updates stay in the editor.
- **Parse in the browser and call the existing create once per row.** Rejected. It needs no backend change, but a
  dropped connection leaves half a file imported, a retry duplicates, and there are a thousand requests and a
  thousand round trips to Media and Artists.
- **Import what is valid and report the rest.** Rejected. Staff would have to reconcile the spreadsheet against the
  CMS by hand. With preview-then-commit, they fix the file and import it whole.
- **A multipart file upload, streamed like media.** Not needed. The file is text and bounded at 8 MiB, and a JSON
  body gets FluentValidation, the OpenAPI schema and model binding for free.
- **Match library images by filename.** Deferred. It would need Media to look assets up by name, which it can't
  today, and every image still needs alt text before publishing.

## Consequences

- Staff can add a season's stock in one step, and nothing they import is public or in Square until they publish it.
- `IIdempotentCommandExecutor` now has a production caller, so its transaction (ADR 0020) wraps a real DataLayer
  write. Shop's module tests compose `AddArtsIdempotency` as the API does.
- **The artist column is read before the command, not inside it**, as a single create's credit check is: an artist
  archived in the moment between preview and save can still be credited. Business re-reads slugs, SKUs and item
  numbers inside the transaction, because those are what a concurrent write would clash on.
- **The format is a contract with staff's spreadsheets.** Renaming a column breaks their files. `ShopImportColumns`
  and the admin's `SHOP_IMPORT_COLUMNS` must change together.
- **Widening the import** — images, updates, or classes — needs its own ADR. It must not reach past the field
  ownership above.
