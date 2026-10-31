# 0062 — Every CMS list sorts by its columns, on the server

- **Status:** Accepted
- **Date:** 2026-10-31
- **Rules affected:** `.claude/rules/frontend.md`, `.claude/rules/api-contract.md`
- **Builds on:** [0042](0042-site-read-api-and-its-cache.md) (the shop's price orders: a keyset per order,
  unpriced last), [0052](0052-soft-delete-for-cms-content.md) (the Deleted view)

## Context

Every CMS list (artists, instructors, classes, exhibitions, pages, membership tiers, artwork, redirects, and the
three Forms inboxes) came back in one fixed order: by name, by title, newest first, or the tiers' display order.
Staff asked to sort them by any column, and for the tables to look sharper.

The lists are cursor-paged 50 at a time behind **Load more**, on a keyset (`PageCursor`: the last row's sort value
and its id). Sorting the rows already in the browser would sort a fragment: with 50 of 300 artists showing, "Updated,
newest first" would quietly leave the newest one on page four. So the order has to be the server's.

Each list's repository also hand-wrote its keyset `WHERE` and `ORDER BY`, and the public shop's four orders showed
what one per column would cost: a `switch` of near-identical blocks per list.

## Decision

- **`sort` on every CMS list.** `GET /api/v1/cms/{list}?sort=updated` ascending, `?sort=-updated` descending. Absent
  is the list's own order, unchanged from before. A column the list does not sort by is a 400
  (`validation.failed`, naming the columns there are), not a silent fall-back: the CMS is the only client, and a
  wrong name is a bug to see. The Deleted views (ADR 0052) keep their fixed order and ignore it.
- **One kernel helper, `Managers/Paging/ListOrder<TEntity, TRow>`.** A module declares its columns once, in
  `Managers/<Type>ListOrder.cs` (beside its validators, which read the column names from it; Business resolves and
  pages through it and the repository applies it), each as a key on the entity (for the query) and the same key on
  the row the repository returns (for the next cursor). The helper builds the `ORDER BY` and the keyset `WHERE` from
  those expressions:
  - the column, then the id to break ties, with the `WHERE` resuming in exactly the `ORDER BY`'s terms. The id
    runs the way the list always broke ties (descending for the exhibitions and the Forms inboxes, ascending
    elsewhere), so no list's own order moves;
  - a column that can be empty sorts its empty rows **last both ways**, as the leading `ORDER BY` term, the keyset
    treating an empty value as one past every other (ADR 0042's unpriced-last, generalised);
  - text compares under the column's collation (`string.Compare`, which EF translates to `>`), a status as its stored
    number (so the lifecycle's order, not the alphabet), a flag as no-before-yes;
  - the values go to SQL as parameters, not constants.
- **The sort is part of the cursor's scope**, so a cursor from one order is `CursorInvalid` in another rather than a
  wrong page. A cursor issued before this change has no sort in its scope, so a CMS tab left open across the
  deploy gets `paging.cursor.invalid` on its next **Load more** and reloads from the top. The cursor's value is
  typed by its column (text as written, a status as its number, a date in its round-trip form) and a value that does
  not parse as the column's type is `CursorInvalid` too, never an exception.
- **What sorts: every column a table shows** (staff asked for all of them). The only header without a sort is the
  newsletter list's, which holds buttons, not data. Where a column is not one stored value, it sorts by the one
  that leads it:
  - **A class's next session** is the earliest session still to come, counted from the moment the list is read.
    **Its instructors** sort by the alphabetically first name any run names. **An artwork's artist** sorts by the
    credited artist's name. A class with no session to come or no instructor, and an uncredited piece, sort last.
  - The two names belong to other modules. A column's key may take a **context** the repository hands
    `ListOrder.Apply`: the moment, or the other module's table that the foreign key already reaches
    (`ClassRun.InstructorId`, `ShopItem.ArtistId`; ADR 0033, 0044). That table is read only in the `ORDER BY`, its
    keyset and the row's cursor value, all inside the owning module's repository; the names shown still come
    through the other module's facade. Both reads respect its soft-delete filter, so a deleted artist sorts as none,
    which is how the facade shows them. There is no other way to page by a name the database must order, and
    loading every row to sort them in memory would undo the paging.
  - **"In Square"** sorts by the figure it leads with, Square's price; an artwork Square has not priced sorts last.
  - **A contact message** sorts by its first 100 characters, so the cursor carries a short value. **A submission's
    "wants to"** sorts by its choices as stored.
  - **A membership tier's price** sorts by the figure the table shows: Square's price once Square holds the plan,
    the starting price before. Reading a Square-held projection for an order writes nothing (`square.md`).
  - **A list's own order can be a column no header shows**: a tier's display order (`order`) and a newsletter
    address's sign-up time (`created`). They are kept as they were, so absent `sort` changes no list's order.
- **The CMS.**
  - `shared/list-sort.ts` (`ListSort`) holds a list's order in the address bar as `?sort=`, so Back from a record
    returns to the list in the order it was left and a link opens sorted. The list's own order stays out of the
    address. It knows the words for each order ("Updated, newest first"), which the table's caption carries and the
    live region announces when the rows arrive ("Sorted by Updated, newest first. 50 artists.").
  - `table[cmsSortable]` and `th[cmsSortHeader]`: the whole header cell is one button. The sorted column says so
    with `aria-sort`, an ink label, a chevron and a `--hot` rule under it; the others show, on hover or focus, which
    way a click would go. A first click sorts dates newest first and everything else from the top; a second turns
    it round.
  - On a phone the wide columns, and their headers, are hidden, so `cms-sort-picker` offers "Sort by" with every
    column each way round. Hidden above 600px.
  - **One table look**, `.cms-table` in the admin's `styles.scss`, replacing a copy of the same rules in every list's
    stylesheet: quiet uppercase headers over an ink rule, rows lifting to white under the pointer, figures in
    tabular numerals, and rows that dim while a new order or filter loads (`aria-busy`) instead of vanishing.
  - The `chevron` glyph joins `@6311/ui`'s icon set.

## Consequences

- A new sortable column is a line in the module's `ListOrder`, a line in the list component's `SORTS`, and a
  `cmsSortHeader` on its header; `ListOrderTranslationTests` fails until it has a sample value, so every column is
  run through EF before it ships.
- A new CMS list follows the same pattern rather than hand-writing a keyset.
- Columns staff sort by often may want an index; none is added now. The lists are hundreds of rows, not thousands.
  Revisit with the query plans once the WordPress import has run against Azure SQL.
- `ListOrderTranslationTests` runs on SQLite, like the rest of the suite; the SQL Server test container (ADR 0030
  follow-ups) would cover the same expressions on the production engine.
