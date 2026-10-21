# 0048 — The site server routes and maps from the API; the forms post to it; the brand images live in Media

- **Status:** Accepted
- **Date:** 2026-10-21
- **Rules affected:** `.claude/rules/frontend.md`, `.claude/rules/media.md`, `.claude/rules/aspire.md`
- **Builds on:** [0024](0024-bff-session-tickets-and-edge-antiforgery.md) (antiforgery),
  [0035](0035-redirects-and-slug-changes.md) (the Redirects table and its normal form),
  [0042](0042-site-read-api-and-its-cache.md) (`/site/redirects`, `/site/sitemap`),
  [0046](0046-the-site-reads-through-siteapi.md) (the site reads through the gateway),
  [0047](0047-forms-submissions-newsletter-and-contact.md) (the forms API)

## Context

Prompt 3.3 takes the last of the spike's hard-coded data out of the site's Express server:

- the legacy redirects, a fixed map of four addresses plus WooCommerce product and artist-archive patterns;
- `/sitemap.xml`, built from the sample data;
- `/api/subscribe` and `/api/contact`, Express routes forwarding to a webhook, unreachable through the gateway since it
  sends `/api/**` to the API;
- the artist submission page's link to a Google Form;
- the logo and the Open Graph image, served from the WordPress uploads folder.

The restriction is that every legacy URL in the spike's map still 301s, and `robots.txt` doesn't change.

## Decision

### Redirects: the CMS's list first, the spike's rules behind it

The server's first middleware resolves every request against, in order:

1. **the CMS's redirects** (`GET /api/v1/site/redirects`): staff's, imported ones and slug changes. It tries an exact
   match with the query first, then the path alone, in the Redirects module's normal form (lower case, no trailing
   slash). The row's status code is honoured; anything but 302 is a 301.
2. **the spike's fixed map** (`SPIKE_REDIRECTS`): `/events`, `/gallery`, `/submit-your-work`, `/privacy-policy`.
3. **`/product/{slug}`**: `/classes/{slug}` when the slug is a published class's, otherwise `/shop/{slug}`. The class
   slugs come from the sitemap's `/classes/{slug}` entries, so no extra endpoint is needed.
4. **`/artist/{slug}`** and `/artist/{slug}/page/{n}`: `/artists/{slug}`.

A redirect to the address asked for is never answered, so a bad row can't loop. The spike's four are also seeded into
the Redirects table (ADR 0035), so normally the CMS's copy answers first. The spike's map is the fallback for when the
API can't be read.

**The routing data is cached in the server process.** The redirects and the class slugs (`createSiteData`) are cached
for 60 seconds and refreshed behind the request:

- only the very first request waits on the API, with a 4-second timeout;
- when a read fails, the last good copy is kept, and the server asks again after 5 seconds rather than a minute. The
  first read often happens during Aspire's health check, before the gateway is up.
- with nothing cached and the API down, the spike's rules still answer.

A failed read logs only the path and the error type, never the gateway address or a message.

### The sitemap: from the API, or a 503

`/sitemap.xml` lists the fixed pages, then every published record with its last change, from
`GET /api/v1/site/sitemap`. When the API can't be read, the answer is **`503` with `Retry-After: 300` and
`no-store`**: never a short list, which a crawler would take as pages removed. `robots.txt` is unchanged.

### The forms post to the API

The newsletter sign-up, the contact form and the artist submission form post through `FormsApi` to
`/api/v1/forms/{newsletter,contact,submissions}`, with the antiforgery header like any unsafe request (ADR 0024). A
submission is the `submission` JSON part, then the `images` parts. The client checks the API's limits (six images,
10 MB each) before sending anything, and shows the API's own reason when it refuses. The Express forward routes and
`WP_ORIGIN` are deleted. A native page at `/artists/submit` replaces the Google Form, and the Submit page links to it.

### The brand images are Media assets at fixed ids

The logo and the default social image are ordinary Media assets with ids fixed in `MediaSiteImages`:

| Image | Id | The site names |
|---|---|---|
| Logo | `6311a0f5-0000-4000-8000-000000000001` | `/media/{id}/800.webp` |
| Open Graph | `6311a0f5-0000-4000-8000-000000000002` | `/media/{id}/1600.jpg` |

- **MigrationService seeds them, in every environment.** `SiteImageSeeder` uploads them through
  `IMediaFacade.EnsureSiteImageAsync`. The bytes are embedded in MigrationService, with alt text written for the
  picture. The image starts `Published`; the Worker makes its renditions, as for any upload.
- **A run that finds the id already there changes nothing.** If Media refuses the picture, the run fails rather than
  leaving the site naming a 404.
- **The fixed id and the published start belong to the seed alone.** The CMS upload route builds its view model
  without them. A staff upload of the same bytes doesn't block the seed, because a fixed id skips the duplicate check.
- **MigrationService now references `blobs` and waits for it in every mode**, not only when it imports.

## Options considered

**Brand images:**

- **(a) Media assets at fixed ids, seeded by MigrationService.** Chosen. The site needs no lookup, delivery gets the
  media pipeline's renditions and cache headers, and a clean environment has them.
- **(b) Static files in the site's build.** Simpler, but they bypass the media pipeline, and `/media` would not be
  where they live, which is what the prompt asks for.
- **(c) A "site settings" record that names an asset.** Lets staff swap the logo from the CMS, but adds a content type
  and a read for every render. It's not needed until someone asks to change the logo.

**Sitemap when the API is down:**

- **A 503 with `Retry-After`.** Chosen.
- **The fixed pages alone.** Search engines would read that as every record removed.

## Consequences

- Every legacy URL in the spike's map still 301s, with or without the API. The spike's rules are code, tested in
  `legacy-redirects.spec.ts`, and the crawl report in prompt 3.3 checked them through the gateway.
- **A new redirect is a CMS row.** The spike's map only shrinks.
- **A CMS change reaches routing within about a minute:** the site server's own 60-second routing cache, after the
  API's copy is cleared through the cache invalidation bus (ADR 0049).
- **Changing the logo is a code change for now:** a new file and a new fixed id, or option (c). Staff deleting the
  seeded asset brings it back on the next MigrationService start.
- **MigrationService needs storage wherever it runs**, so a publish without blobs fails at start.
