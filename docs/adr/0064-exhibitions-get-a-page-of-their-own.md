# 0064 — Exhibitions get a page of their own

- **Status:** Accepted
- **Date:** 2026-10-31
- **Rules affected:** `.claude/rules/frontend.md`, `.claude/rules/content.md`
- **Builds on:** [0042](0042-site-read-api-and-its-cache.md) (the read this page needs, which already existed),
  [0033](0033-exhibitions-and-cross-module-artist-credits.md) (credits), [0036](0036-revalidation-scheduled-publishing-and-previews.md)
  (the address the revalidation already names), [0063](0063-the-sites-preview-page.md) (the preview page, which is
  what exposed this), [0051](0051-one-component-per-folder.md)

## Context

The site had no page for an exhibition. `/exhibitions` listed every show — the upcoming ones as full-bleed
spotlights, the past ones as cards — and nothing linked anywhere. Three things already assumed otherwise:

- **The API serves one.** `GET /api/v1/site/exhibitions/{slug}` has existed since 2.x, returns
  `SiteExhibitionServiceModel` with the show's `bodyText`, and is in `api-contract.md`'s table of public reads.
  Nothing called it.
- **The sitemap advertises one per show.** `ExhibitionsBusiness.ListSitemapAsync` emits
  `SitePaths.AddressOf(ExhibitionPolicy.SitePathPrefix, slug)` — `/exhibitions/{slug}` — for every published
  exhibition, so the site has been telling crawlers about a page per show that answered **404**.
- **Staff write a body for every show** (`content.md`'s Exhibition row), and no page ever rendered it.

ADR 0063 brought it to the surface: with no page to render, a previewed exhibition had to borrow the list page and
show the draft alone, and its write-up could not be previewed at all. The operator's question — "what will this
really look like?" — had no honest answer for an exhibition.

## Decision

1. **`/exhibitions/:slug` is a page**, `pages/exhibition-detail/`, lazy and server-rendered by `app.routes.server.ts`'s
   `**` entry (content-driven, so never prerendered). It reads `/api/v1/site/exhibitions/{slug}` through
   `SiteApi.exhibition(slug)`, **which answers three ways, not two**: the show, `undefined` for no such published
   show, and `null` for a failed read. A missing show is the site's 404; a failed read must not be, or a blip
   would tell a crawler that a published show is gone — it renders a short "try again shortly" band while the
   server answers 503 `no-store` (frontend.md). `product()` still collapses the two, which is a defect of its own.
2. **It is built in the artist page's idiom**, not a new one: hero, a `detail` band with the show's poster beside
   its copy, a `facts` list (when it is on, the opening reception, who is in it, free to visit), the two buttons,
   then a **More exhibitions** band of cards. Nothing new in `styles.scss`.
3. **The site's models carry what the page needs.** `SiteExhibition extends SiteExhibitionSummary` adds `bodyText`,
   with `decodeSiteExhibition`; the shared `Exhibition` gains `body` (the write-up as paragraphs, split at blank
   lines like an artist's bio, ADR 0031) and `credits` (each credit with its slug where it has one). The list
   mapper fills `credits` too, so one mapping serves both.
4. **A credited artist is a link when they are published and plain text when they are not**, which is how every
   other credit on the site reads (ADR 0042): the slug is `null` for an unpublished artist, and the page simply
   names them.
5. **The list page links to it, without changing how the list looks.** The card's title and each spotlight's
   heading become anchors that inherit their heading's colour and weight and carry no underline at rest, so
   `/exhibitions` is pixel-identical until a pointer or focus ring arrives (design-system.md rule 6). Each gets
   the style in its own component stylesheet, and the card joins the `--focus: var(--focus-on-dark)` list at the
   top of `styles.scss` (design-system.md rule 7) because its title sits on a photograph under a dark veil, where
   an ink ring would disappear. The link's `::after` stretches the target across the veil — the title, the dates
   and the summary — so it is not one 27px line of type.
6. **A preview of an exhibition renders this page**, which replaces ADR 0063's "the exhibitions page carrying the
   one show": `PreviewSiteApi` now seeds `exhibition(slug)`, keyed on the draft's slug, and leaves `exhibitions()`
   to the real API so **More exhibitions** is the live list.
7. **A membership tier previews among the published tiers** (also amending ADR 0063, which showed it alone). A
   tier has no page of its own (ADR 0040) — it is a column on `/memberships` — and how that page reads depends on
   its neighbours: the column count, which tier is featured, the "join from" price. The draft replaces its
   published self when it is an edit to one, and otherwise joins at the end, because the site's tier model carries
   no display order (the API's order *is* the list), so a new tier's real position is not knowable in the browser.
8. **Nothing in the API, the cache or the outbox changes.** The read, its HybridCache entry and its tags already
   existed, and the address this page serves is the one `site.revalidate-requested` has always carried for an
   exhibition (ADR 0036), so publishing or editing a show already clears exactly this page.

## Options considered

- **Leave the site list-only and stop advertising the pages in the sitemap.** Rejected: the read, the body field
  and the sitemap all say a page was intended, staff write copy that nothing shows, and a show with a poster, dates
  and a reception is the most shareable thing the gallery has. Removing sitemap entries would also drop any
  ranking those URLs have earned.
- **Render the body inside the list's spotlight.** Rejected: it makes one page unboundedly long, there is no
  address to share for a single show, and the past shows' cards still could not show it.
- **A dialog over the list.** Rejected: no address, no crawlable page, and the spike's visual language has no
  dialog on the public site.
- **Previewing an exhibition on the list page with the draft among the published shows** (the smaller change).
  Rejected once the page existed: the operators asked to see the record as its own page, and the list could never
  show the write-up.

## Consequences

- **The sitemap stops pointing at 404s**, and each published show becomes a crawlable page with its own title,
  description, canonical and `ExhibitionEvent` JSON-LD.
- **The exhibition body finally has a reader.** It stays plain text: if a show ever needs headings or lists, that is
  a sanitized `bodyHtml` field and its own ADR (`content.md`'s note on long-text fields that predate the sanitizer).
- **`/exhibitions` gained its first links out**, so the pixel-diff against spike0 (design-system.md rule 6) shows a
  hover and focus state that did not exist before, and nothing else.
- **A draft tier previews last among the tiers unless it is an edit to a published one.** Giving the site's tier
  model a display order would fix it, and would be an API shape change.
- **Every previewable type now previews as a real site page.** The last exception, a class with no dated run,
  went with [ADR 0065](0065-the-class-page-is-the-class-not-its-next-run.md), which found the same bug this ADR
  found on exhibitions — a page that 404s for published content the sitemap names.
