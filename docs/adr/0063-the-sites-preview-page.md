# 0063 — The site's preview page renders the real page over a seeded read

- **Status:** Accepted — the exhibition and membership-tier branches amended by [0064](0064-exhibitions-get-a-page-of-their-own.md): a show now has a page of its own to preview, and a tier previews among the published tiers rather than alone
- **Date:** 2026-10-31
- **Rules affected:** `.claude/rules/frontend.md`, `.claude/rules/content.md`
- **Builds on:** [0036](0036-revalidation-scheduled-publishing-and-previews.md) (preview tokens, and the debt this
  pays), [0042](0042-site-read-api-and-its-cache.md) (the site's read models),
  [0046](0046-the-site-reads-through-siteapi.md) (`SiteApi`, and SSR's reads through the gateway),
  [0051](0051-one-component-per-folder.md) (where a page's files live)

## Context

ADR 0036 built both halves of a preview link but one: staff mint one with `POST /api/v1/cms/preview`, and
`GET /api/v1/site/preview/{token}` reads the draft back. It left the page itself to "3.x", and no prompt ever
claimed it — so `/preview/{token}` fell through the Angular router's `**` route to the site's 404. Every preview
link staff made answered **404**, which is what this ADR fixes.

The site's pages can't simply be linked to instead. A page reads its record by slug from a published-only route
(`/site/artists/{slug}`), so a draft has nothing for it to find, and three of the seven previewable types have no
address of their own at all: an exhibition is a card on `/exhibitions` (there is no exhibition page), a membership
tier is a column on `/memberships` (ADR 0040), and a CMS page is reachable only at the two fixed routes
(`/privacy`, `/terms`).

What a preview owes, from ADR 0036 and the security review: `noindex` in the rendered HTML, `no-store` from SSR,
absence from the sitemap and any prerender list, no write access, and nothing in browser storage.

## Decision

1. **One route, `preview/:token`, declared before the `**` catch-all**, lazy, server-rendered by the `**` entry in
   `app.routes.server.ts`. The token stays in the path because the API mints that address
   (`PreviewPolicy.PathFor`); it is a capability URL, and ADR 0036's three containments (30 minutes, one read,
   `RedactSecretSegments` on the exported span) are what make that acceptable.
2. **The preview page renders the site's own page component**, not a second rendering of each content type. A
   duplicate layout would drift, and staff would be previewing a page the site never shows. So:
   - `artist` → `<app-artist-detail>`, `instructor` → `<app-instructor-detail>`,
     `class-offering` → `<app-class-detail>`, `shop-item` → `<app-product-detail>`,
     `page` → `<app-cms-page>`.
   - `exhibition` → `<app-exhibition-detail>` (ADR 0064 built that page; this ADR shipped with the exhibitions
     list standing in for it) and `membership-plan` → `<app-memberships>`, where the draft sits among the
     published tiers: a tier has no address of its own, and the page reads the way it does because of its
     neighbours.
3. **The draft is seeded into the API, not into the page.** `PreviewSiteApi extends SiteApi` and is provided **in
   place of `SiteApi` on the preview route only**. It answers the one read that asks for the previewed record and
   passes everything else to the real API with `super`, so the surroundings are the live site's: the artist's work
   is the published shop's, a class's "more classes" is the real calendar, the member discount is the real tiers.
   - The record reaches it through `PreviewContext`, a root service holding one navigation's draft. The preview
     page fills it before the page below renders and clears it on destroy. Nothing else writes it.
   - A read overridden for a type the preview is not of falls straight through, so each override is one `if`.
   - The seeded reads are reads. **No write path takes the token, and `PreviewSiteApi` adds none.**
4. **The pages take their slug as a routed input** (`input.required<string>()` bound by
   `withComponentInputBinding`, which the site already had on) rather than reading `ActivatedRoute.paramMap`.
   Routed behaviour is unchanged — the router binds `:slug`, and route `data` binds `pageSlug` / `fallbackTitle`
   for the two CMS-page routes — and the preview page passes the same value as an ordinary binding. The
   alternative was a stand-in `ActivatedRoute`, which is a fake that has to keep matching whatever each page reads
   from it.
5. **The preview takes up no room in the page it is showing.** A preview that lays out differently from the live
   page is not a preview, so: the component's host is `display: contents`, which takes its element out of the box
   tree and leaves the page's sections as children of `<main>` exactly as they are on the site; and the flag that
   says "this is a draft" is `position: fixed`, bottom left, clear of the back-to-top button and above the nav's
   `z-index`. In flow it could not work — `.nav` is fixed and `.hero` pays for it with its own 140px of top
   padding, so anything above the hero both sits under the nav and pushes the whole page down. The flag folds away
   to its tag for the part of the page it covers. A spec asserts both declarations, because jsdom has no layout.
6. **`Seo` knows when it is rendering a preview**, because the page below it sets its own tags and would otherwise
   undo the preview's. While `PreviewContext` is active, every `Seo.set` writes `robots: noindex, nofollow` and
   emits **no canonical** (a draft's address is where it *will* live, which is not a canonical yet).
7. **The response is `no-store` with `X-Robots-Tag: noindex, nofollow`** (`noStoreNoIndex()`, beside the
   `noStore()` the account pages use). `server.ts` leaves a page's own `Cache-Control` alone, so no CDN keeps a
   draft. With `robots.txt` already disallowing `/preview/` and the sitemap coming from the API, a preview is out
   of every index three ways.
8. **Everything unusable is one answer and one page.** `SiteApi.preview(token)` returns `undefined` for an expired,
   tampered-with, deleted-record or unreadable answer alike, and the page renders the site's `<app-not-found>`,
   which sets a real 404. There is no sample-data fallback for a preview, and a failed read is not a 503: a
   preview is a staff tool, and the answer a holder gets says nothing about what exists.
9. ~~**A class with no dated run is told what it is waiting for, not answered 404.**~~ **Withdrawn by
   [ADR 0065](0065-the-class-page-is-the-class-not-its-next-run.md).** This ADR treated a page defect as a content
   state: the class page read the calendar rather than the class, so a class with no run still to come had no page
   — which also meant a *published* class 404ed on its own address between terms. 0065 made the page read the
   class, so a dateless class renders its copy and says the calendar is empty, and the preview simply renders it.
10. **A preview carries what the site renders, and no more.** The answer's exhibition `bodyText` and class
   `bodyHtml` are dropped in the decoder, because no page on the site renders them today. A preview showing copy
   the published page would not would be a worse lie than leaving it out.
11. **The preview page's lazy chunk carries seven page components**, which is the one cost of reusing them. It is a
   staff-only route, loaded only when a link is opened, and the chunk is ~17 kB.

## Options considered

- **A standalone preview layout per content type.** Rejected: a second rendering of seven types, drifting from the
  real pages from the day it shipped, and staff would be approving a page the site never serves.
- **A stand-in `ActivatedRoute` provided by the preview page**, leaving the pages untouched. Rejected (and it was
  the smaller diff): the stand-in has to keep answering whatever each page reads from a route — `paramMap`,
  `data`, `snapshot.url` — and nothing fails loudly when it stops.
- **Redirecting the preview to the record's real address with the draft held in a service.** Rejected: the link
  has to keep working when it is reloaded or opened by a colleague, and the published-only route would 404.
- **Making the site's reads take a token** (`/site/artists/{slug}?preview=…`). Rejected: ADR 0036 settled one
  address for every type, a token cannot name its own route, and it would put a credential on seven cached reads.
- **Seeding the whole list reads for exhibitions and tiers with the published records plus the draft.** Taken for
  tiers by ADR 0064, and overtaken for exhibitions by the page that ADR built. The draft alone looked clearer at
  first, but a shared page reads the way it does because of its neighbours — the alternating bands, the column
  count, the "join from" price — so the draft alone was not what it would really look like.

## Consequences

- **Preview links work for all seven previewable types**, and a new previewable type adds a branch to the preview
  page beside the entry it already owes `PreviewPolicy.PathPrefixes`.
- **A preview shows exactly what the site shows.** Where the site had no page, that was the preview's ceiling —
  and it is why ADR 0064 built the exhibition page rather than leaving a show to preview as a list entry.
- **An unpublished artist's work is empty in a preview**, because the shop lists published pieces only.
- **`PreviewSiteApi` must stay a read seam.** A write added to `SiteApi` is not overridden here, and must never be:
  the token authorizes one read (ADR 0036).
- **A draft page's address may be one the site does not serve** (the same gap ADR 0064 closed for exhibitions,
  still open for pages). A CMS page lives at `/{slug}`
  (`PagePolicy`), but the site routes only `/privacy` and `/terms`; anything else is the 404. A preview of a page
  with another slug renders correctly and truthfully says where it *will* live, and that address 404s until a
  `/{slug}` route exists. Page previews are therefore only fully meaningful for the two routed pages today.
- **A preview's JSON-LD and its "buy" calls to action are the real page's.** The page below the bar is the live
  page, so a draft piece still shows "Buy now" and a draft class "Reserve your spot", both pointing at the legacy
  store (`LegacyStore.buyUrl`) where no such product exists until Phase 5, and `Seo` still emits the page's
  structured data with the address the record will have. Both were left as they are deliberately: a preview that
  hides parts of the page stops being what visitors will see, and the page is `noindex` with `nofollow`.
- **The draft's JSON is in the server-rendered HTML**, in the HTTP transfer cache's `ng-state`, along with the
  request URL and so the token. It is not a new exposure — whoever has the document already has the address in
  their own address bar, and the response is `no-store` — and filtering the preview read out of the transfer cache
  would cost a second read of the draft on hydration. If that trade is ever revisited, the change is
  `withHttpTransferCacheOptions({ filter })` in `app.config.ts`.
- **A transient gateway failure looks like an expired link.** The preview read's every failure is one answer
  (decision 7), so an outage shows staff the 404 rather than "try again". That is the cost of telling a holder
  nothing, and it is bounded: the CMS can mint another link.
- **The five converted pages make their first read from an effect** (`toObservable`) rather than the constructor's
  subscription to `paramMap`. The rendered output is the same, and SSR waits for the request either way.
- **A redirect could still shadow a preview address.** `/preview` is not in `RedirectPaths`' reserved prefixes
  (ADR 0035), so a staff-written redirect from `/preview/<something>` would win at the edge before the Angular
  route is reached. A token is 300 random characters, so nothing collides in practice; adding the prefix to the
  reserved list is a one-line follow-up, and it needs its own test and a note in ADR 0035.
