# 0042 — The site read API, its cache, and the site's own read models

- **Status:** Accepted — except "Publishing an image", recorded here and implemented by 3.1b; how a change reaches
  another process amended by [0049](0049-cache-invalidation-bus.md) (the "within a minute" below did not hold)
- **Date:** 2026-10-21
- **Rules affected:** `.claude/rules/api-contract.md`, `.claude/rules/backend.md`, `.claude/rules/content.md`,
  `.claude/rules/media.md`
- **Builds on:** [0030](0030-media-library-compressed-masters-and-renditions.md) (renditions, delivery, the image
  DECIDE), [0032](0032-website-items-managed-from-the-cms.md) (Square owns money and stock),
  [0033](0033-exhibitions-and-cross-module-artist-credits.md) (credits across modules),
  [0036](0036-revalidation-scheduled-publishing-and-previews.md) (revalidation, previews — **amended**: the preview
  answer now carries these models), [0040](0040-membership-plans-without-a-page-of-their-own.md) (one page for every
  tier)

## Context

Prompt 3.1 adds the anonymous `/api/v1/site/*` reads the public site will render from in 3.2: classes with their
sessions, exhibitions, artists with their work, the shop, memberships, pages and the sitemap. Redirects already
existed (ADR 0035). Three things were left to it by earlier decisions:

- **Turning the cache on.** Every content module has staged `site.revalidate-requested` for its status changes since
  2.8, and for every edit to a published record since 2.13, but nothing read through the cache yet.
- **The preview answer's shape.** ADR 0036 returned the CMS ServiceModels with their bookkeeping blanked, "until 3.1
  defines the site's own".
- **ADR 0030's "Publishing an image".** Every asset stays `Draft` — staff-only at `/media/**` — until content
  publishing marks it `Published`, and ADR 0036 moved that decision here.

The spike's models (`projects/site/src/app/core/models.ts`) are what the site renders today; these reads have to
carry everything they do, from records whose owners are spread across seven modules.

## Decision

### The routes

| Route | Paging | Answered by | Cached as | Tags |
|---|---|---|---|---|
| `GET /site/classes` | none: only classes with a session still to come | Classes | `class-offering:list:site` | `type:class-offering` |
| `GET /site/classes/{slug}` | — | Classes | `class-offering:{slug}` | `type:class-offering` |
| `GET /site/exhibitions` | none: newest opening first, a few a year | Exhibitions | `exhibition:list:site` | `type:exhibition`, `type:artist` |
| `GET /site/exhibitions/{slug}` | — | Exhibitions | `exhibition:{slug}` | as above |
| `GET /site/artists` | none: the roster, with work counts | Site | `artist:list:site` | `type:artist`, `type:shop-item` |
| `GET /site/artists/{slug}` | the first page of their work | Site | `artist:{slug}` | as above |
| `GET /site/shop?category=&artist=&sort=&cursor=&limit=` | cursor | Shop | first page only | `type:shop-item`, `type:artist` |
| `GET /site/shop/{slug}` | — | Shop | `shop-item:{slug}` | as above |
| `GET /site/memberships` | none: display order | Memberships | `membership-plan:list:site` | `type:membership-plan` |
| `GET /site/pages/{slug}` | — | Pages | `page:{slug}` | `type:page` |
| `GET /site/sitemap` | none | Site | `sitemap:list:site` | every content type's |

All are `[AllowAnonymous]` and show **Published** records only; Draft, Scheduled and Archived are the same
`resource.not_found` as an address that never existed.

### Every entry carries type tags, and only type tags

HybridCache takes an entry's tags before its factory runs, so a read by slug cannot tag itself with the record's id,
which it does not know yet; and a rename's revalidation names only the new address, so an entry under the old slug
would outlive it. So **every site entry carries the type tag of every content type it shows**: a shop item's page is
tagged `shop-item` and `artist`, because it credits one. "Any change to a type clears every site entry built from
it" is right for renames, for unpublishing and for an artist's new name on a piece of work alike, and staff write
seldom enough that clearing a whole type costs nothing. The kernel spells the names once (`SiteContentTypes`, beside
`SitePaths`), and a test holds each equal to its module's audit `ResourceType`.

- **Lifetimes.** `SiteCachePolicy.Lifetime` is five minutes in HybridCache (one minute in each replica's memory, as
  before); responses send `Cache-Control: public, max-age=60` — how long anything in front of the API may keep a
  read (ADR 0049 corrects "the minute a change takes to reach another replica") — and `no-store` on a failure.
- **The worst case is the lifetime, not the minute.** HybridCache is cache-aside: a read that began before a commit
  can finish after the revalidation cleared its tags, and store what it read for the full lifetime. That needs a read
  and a write within milliseconds of each other on one record; the usual case is the minute, the bound is five.
- **What is not cached.** A not-found (the key is a slug the visitor chose, so keeping misses would let anyone fill the
  cache, and a lookup on a unique slug is cheap); and a shop page after the first (its key would be the visitor's
  cursor). An artist filter is resolved to a published artist before the cache is asked, so a made-up slug is an
  empty page that is never stored.
- **What a lifetime, not a message, bounds.** A session passing its end, and a rendition finishing, change what the
  site shows without any write to announce it; both appear within the lifetime.

### Who answers

Each content module's facade answers its own type's reads and caches them; Business decides what is published and
maps to the site's model; the facade asks Artists and Media, facade to facade, who is credited and what the pictures
are. Two reads need more than one module, and a new facade-only module, **`Modules/Site`**, answers them: the artist
directory and an artist's page need Shop's work beside Artists' roster — and Shop already asks Artists about its
credits, so Artists asking Shop would make the two depend on each other — and the sitemap needs every type. Like
`Modules/Preview` it has no Business or Data layer: there is no rule to own and nothing to persist.

### The site's own models

The site's models are written for the public, not trimmed from the CMS's: none has a status, `updatedBy`, an ETag, a
legacy id, a SKU, a starting price or quantity, a stock count, or anything of Square's but its record of the money
(a test reflects over every `Site*ServiceModel`). Ids are ours; money is cents with `currency`; times carry
`+00:00`. Mapped to the spike's:

| Spike | Site model | Notes |
|---|---|---|
| `ArtClass` | `SiteClassSummaryServiceModel` / `SiteClassServiceModel` | real `sessions[]` (upcoming only, each with Square's price and seats) instead of a start, an end and a count; `sessionCount` counts every session, so "a six-week course" stays six; `fromPriceCents` is the lowest upcoming price; a class that is over keeps its page with no sessions |
| `Exhibition` | `SiteExhibitionSummaryServiceModel` / `SiteExhibitionServiceModel` | `artists[{name, slug}]`, where `slug` is `null` for an artist who is not published; the body is plain text and named `bodyText` (an artist's is `bioText`), so a sanitized body can arrive later as `bodyHtml` without renaming anything |
| `Artist` | `SiteArtistSummaryServiceModel` (directory) / `SiteArtistPageServiceModel` | `workCount` is the spike's `productCount`; the page carries the artist and the first page of their work, which continues at `/site/shop?artist={slug}` |
| `Product` | `SiteShopItemSummaryServiceModel` / `SiteShopItemServiceModel` | `soldOut` instead of `inStock`; the stock count is not shown; **`priceCents` is `null` until Square has priced the piece**, and the site shows it as not yet for sale — the CMS's starting price is never shown, because Square owns the price (ADR 0032) |
| `MembershipTier` | `SiteMembershipPlanServiceModel` | price and cadence are Square's record, `null` until it has one |
| `CmsPage` | `SitePageServiceModel` | `bodyHtml` is the sanitized body as stored |
| — | `SitemapEntryServiceModel` | `{path, lastModified}`; `/memberships` once, when any tier is published |
| `image?: string` | `SiteImageServiceModel` | `{src, alt, width, height, sources[]}`: the widest JPEG rendition as `src`, every rendition in `sources`, never the master; an image with no rendition yet is left out |

The spike's `Instructor` has no counterpart: a class names its instructor as text, and no Instructors type is
planned. 3.2 keeps the spike's instructor data.

### The preview answer carries these models

`ContentPreviewServiceModel`'s members are now the site's detail models, read through each module's
`GetForPreviewAsync` whatever the record's status, so a preview shows exactly what the published page would and has no
bookkeeping to blank. This is the breaking change ADR 0036 scheduled, and the one sanctioned inside v1: nothing
consumed the old shape, and 3.2 binds to this one.

### Publishing an image — decided, implemented by 3.1b

**Option 1 of ADR 0030, one-way.** Publishing a record stages `media.publish-requested` with the image ids it shows
in the same save, and a Media handler marks those assets `Published`. Nothing turns an image back to `Draft`: once a
picture has been public it has been seen, and hiding it again would only break pages and caches that still name it.
It is its own prompt because it is a new outbox message staged by six modules. **Until it lands, every image these
routes describe answers 404 to a visitor**, so 3.2 depends on it.

## Options considered

- **Entity and path tags, by slug-to-id lookup first.** Precise, but a database read on every request to learn the
  tag, and still wrong for a rename unless the revalidation also named the old address. Declined for the type tag.
- **Caching misses.** Faster for repeated bad addresses, and a way to fill the cache. Declined.
- **The artist directory in Artists, counting through Shop.** A two-way dependency between facades. Declined for a
  composing module.
- **Showing the starting price until Square has one.** The page would look complete sooner, at the cost of showing a
  price Square never agreed to (ADR 0032). Declined.
- **Images derived at delivery, or public once rendered** (ADR 0030's options 2 and 3). The first needs a cross-module
  read on every image request; the second contradicts media.md's draft rule. Declined.

## Consequences

- Any content write clears every cached site read of its type, and an artist write also clears exhibitions, the shop
  and the directory. Correct, and at this centre's rate of writes, free.
- **An artist's edit stages its revalidation whatever the artist's status** — the one exception to content.md's
  "a draft edit stages nothing" — because an unpublished artist is still credited by name on published work.
- A published change usually shows within seconds, through the cache invalidation bus (ADR 0049; the "within a
  minute" first written here assumed the shared L2 carried it, which it does not), and at worst within the
  five-minute lifetime (the cache-aside race above, or a missed bus message); a cache in front of the API adds
  nothing to that.
- A site model change is a contract change: the OpenAPI snapshot, and from 3.2 the site's own models, move with it.
- 3.2 must not ship before 3.1b, or every picture on the site is missing.
- The sitemap is one unpaged list. The sitemap protocol caps a file at 50,000 addresses, a few hundred times what
  the centre publishes; past that the site's `/sitemap.xml` becomes an index.
- Square's inbound pull (5.x) writes price and stock projections; when it lands, it must stage
  `site.revalidate-requested` for what it changed, or the site shows the old price until the lifetime runs out.
