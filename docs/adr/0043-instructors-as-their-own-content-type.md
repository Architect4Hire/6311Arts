# 0043 — Instructors as their own content type

- **Status:** Accepted
- **Date:** 2026-10-21
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/backend.md`
- **Builds on:** [0031](0031-artists-and-the-shared-content-lifecycle.md) (the shared content lifecycle),
  [0033](0033-exhibitions-and-cross-module-artist-credits.md) (cross-module credits, facade to facade),
  [0036](0036-revalidation-scheduled-publishing-and-previews.md) (revalidation, scheduling, previews),
  [0042](0042-site-read-api-and-its-cache.md) (the site read API and its cache tags)

## Context

A class used to carry its teacher as free text (`ClassOffering.Instructor`). The site already has an instructors
directory and profile pages (`/instructors`, `/instructors/:slug`, from the spike), which matched a class to a
profile by slugifying the name. Staff asked for the instructor to be a lookup: choose from a list of instructors, kept
like the artist roster but as a separate kind of record.

Some instructors also sell work in the shop, so their profile links to their artist page (the spike's
`artistSlug`). Most do not, and many artists never teach, so the two lists are not the same people.

## Decision

- **A new `Instructors` module**, cloned from Artists: `Instructor` has name, slug, medium ("what they teach"),
  plain-text bio, active, portrait, website, Instagram handle, an optional linked artist and a legacy WordPress id,
  plus the shared lifecycle (draft, schedule, publish, unpublish, archive), audit, row version and slug redirects.
  Its seam, validators, scheduled publisher, preview branch and CMS routes (`/api/v1/cms/instructors`) are the
  artist ones under another name.
- **Website and Instagram have one stored shape.** A website is an absolute `https:` address with a host and no
  user info (`InstructorPolicy.IsWebsite`); plain `http:` is refused. An Instagram handle is stored without the `@`,
  lower-cased, and must match Instagram's own shape (`^[a-z0-9._]{1,30}$`).
- **The artist link is a `Restrict` foreign key into Artists**, as a shop item's credit is (ADR 0033). The facade
  asks `IArtistsFacade` whether the artist exists and is not archived before Business records the id, and finishes
  every CMS answer with the artist as Artists describes them now (`InstructorDetail` → `InstructorServiceModel`).
- **Every edit revalidates, whatever the status**, as for an artist: a draft instructor is still named on published
  classes (ADR 0044), so a rename changes what the site shows. `SiteContentTypes.Instructor` is the type tag.
- **Site reads live in the module's facade**, cached under the `instructor` and `artist` tags:
  `GET /api/v1/site/instructors` and `/{slug}`. The model carries `artistSlug` only while the linked artist is
  published. What an instructor teaches is a filter on the class list (`/site/classes?instructor={slug}`), not part of
  the instructor read, so Instructors never asks Classes anything and the two modules do not depend on each other.
- `SitePaths.Instructors` is `/instructors/`; `instructor` joins `PreviewPolicy.PathPrefixes`, the sitemap and
  `SiteContentTypes.All`.
- **Development seed:** the spike's six profiles (`data/instructors.ts`), Published without portraits, with Candice
  Johnson linked to her artist record. MigrationService runs it after the artist roster and before the classes.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **A separate `Instructor` type with an optional artist link (chosen)** | A second roster to keep. Each list holds exactly the people it is about, and an instructor who also sells is one link away. |
| A flag on `Artist` ("also teaches") | One roster, but every instructor would need an artist page, a medium and a shop presence they may not have, and the site's two directories would be one filter apart from leaking into each other. |
| Keep the free-text name | No new type, but nothing stops two spellings of one person, and the profile page keeps matching on a slugified name. |

## Consequences

- A class run names its instructor by id (ADR 0044). Instructors is registered after Artists and before Classes in
  every host.
- `Artists` now has two inbound foreign keys (shop items and instructors). Archiving an artist is still how one is
  retired; neither link blocks it, and a linked archived artist simply stops being offered for new links.
- The importer (4.x) gains an instructor mapping when the WordPress source is mapped; until then, staff enter them.
- 3.2 replaces the site's `MOCK_INSTRUCTORS` and the name-slugifying lookup with these reads.
