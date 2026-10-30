# 0054 — Web addresses are made from the name, never typed in the CMS

- **Status:** Accepted
- **Date:** 2026-10-30
- **Rules affected:** `.claude/rules/content.md`
- **Builds on:** [0031](0031-artists-and-the-shared-content-lifecycle.md) (slugs from the name),
  [0035](0035-redirects-and-slug-changes.md) (a published rename redirects),
  [0053](0053-shop-csv-import.md) (generated slugs numbered)

## Context

Every CMS editor with a page of its own (artists, instructors, exhibitions, classes, shop items, pages) showed an
editable **Web address** field. It followed the name on a new record until someone typed in it, and after that it
was theirs to change, with a redirect from the old address once the record had been published (ADR 0035).

Staff never need to choose an address. They asked for the field to be disabled everywhere.

With the field disabled, an address made from the name could still be taken (two prints both called "Untitled"),
and create refused that with `cms.slug_taken`. Staff could do nothing about it except rename the record.

## Decision

- **The field shows the address but can't be edited.** In all six editors it's a disabled control. On a new
  record it follows the name or title. On a saved one it shows the stored slug, and renaming the record leaves the
  address as it is. The CMS sends no `slug` on create or update.
- **A create with no slug never fails because the address is taken.** Each module's Business picks the first
  free address of `name`, `name-2`, `name-3`, and so on, through the kernel's `Slugs.FirstFreeAsync`. That's the
  numbering the shop CSV import already used (`Slugs.Numbered`, moved from `ShopImportCells`). For a page, an
  address the site already uses (`PagePolicy.ReservedSlugs`) counts as taken, so a page called "Shop" becomes `shop-2`.
- **A slug the request does name is still kept or refused** as before. That covers the API contract, the
  importer, seeds and the CSV import's own column. The unique index still has the last word, so a create that
  races another for the same address gets `cms.slug_taken`. The editor shows that beside the field.
- **Membership plans are unchanged.** A plan's slug is a stable key, not a web address (ADR 0040), and stays
  editable.
- **The API's slug fields and the rename redirect (ADR 0035) stay as they are.** Nothing in the CMS uses them
  now, but the contract doesn't change.
