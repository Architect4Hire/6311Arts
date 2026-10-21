# 0046 — The site reads through SiteApi: relative addresses, the gateway backend, a 503 for a failed read

- **Status:** Accepted
- **Date:** 2026-10-21
- **Rules affected:** `.claude/rules/frontend.md`, `.claude/rules/api-contract.md`, `.claude/rules/aspire.md`
- **Amends:** [0042](0042-site-read-api-and-its-cache.md) (`GET /site/shop` carries the list's total)
- **Builds on:** [0001](0001-single-origin-yarp-gateway-bff.md) (the gateway is the only way in),
  [0026](0026-https-gateway-in-run-mode-and-the-platform-checkpoint.md) (run mode's gateway is HTTPS),
  [0040](0040-membership-plans-without-a-page-of-their-own.md) (tiers come from the CMS)

## Context

Prompt 3.2 moves the public site off WordPress and onto `/api/v1/site/*`. Five things had no answer yet:

- **Where the server sends its reads.** frontend.md says the browser uses relative addresses and SSR uses the
  gateway's origin from the Node environment. But Angular's HTTP transfer cache keys a response by the URL it was
  asked for: if the server asks `https://gateway:port/api/...` and the browser asks `/api/...`, every page is
  fetched twice and hydrates from a second answer. `HTTP_TRANSFER_CACHE_ORIGIN_MAP` only maps one origin to another,
  and the browser has none.
- **What a failed read does.** The spike fell back to bundled sample data whenever WordPress was slow, so a visitor
  could be shown invented classes, and a crawler could index them.
- **How the shop says "312 works" and "Page 3 of 26".** The client requires the count. 3.1's shop list pages by
  cursor and carried no total.
- **What an unpriced record looks like.** ADR 0042 made `priceCents` `null` until Square reports a price.
- **Where `security.allowedHosts` comes from**, since `angular.json` is build-time.

## Decision

### One address for both sides, re-targeted below the cache

`SiteApi` (`projects/site/src/app/services/`) asks for relative `/api/v1/site/...` on the server and in the browser
alike. On the server only, `GatewayBackend` replaces Angular's `HttpBackend`: it extends `FetchBackend` and, for a
site API read, swaps the address for `SITE_API_ORIGIN` (the gateway endpoint Aspire injects) before fetching. The
backend runs after every interceptor, the transfer cache included, so the cache stores the response under the
relative address the browser will ask for. A read already resolved against the page being rendered (Angular's server
platform may do that) is re-targeted the same way. With no `SITE_API_ORIGIN` (a build-time prerender), a site read
fails rather than going anywhere else. Verified on the running system: the shop's `ng-state` entry is stored under
`/api/v1/site/shop`.

### A failed read is a 503 nobody caches

A read that fails, times out (4 s) or cannot be decoded gives the page an empty list or a missing record. On the
server, the response becomes **503 `Cache-Control: no-store`**: `server.ts` only adds `s-maxage` to a 200, so a blip
is never cached as the page, and a 503 is never indexed as a removed page. A real 404 is just a missing record, and the
page shows its own not-found with a 404. Failed responses are not put in the transfer cache, so the browser asks
again and usually recovers.

**Sample data stays, behind a flag.** `SITE_MOCK_FALLBACK=true` (AppHost parameter `site-mock-fallback`, default
`false`) serves the spike's data instead, for design work without a backend. The server hands the flag to the browser
in `TransferState`, so a client-side navigation behaves as the render did.

### The shop's list carries its total (amends 0042)

`GET /site/shop` answers `CountedCursorPage<SiteShopItemSummaryServiceModel>`: `items`, `nextCursor` and
**`totalCount`**, the number of published works the category and artist filters match, whatever the page. It is
additive: the same two fields as before plus one. Repository, DataLayer, Business and Facade gain a count over the same
filter as the page (`ShopRepository.PublishedMatching`), so the two can't disagree. The first page is cached with its
count, and a later page counts again. The artist page's `works` stays a plain `CursorPage`, because its reader shows
the work, not the number.

The site's pager shows "N works" and "Page N of M" from the total. A later page's address is
`/shop?page=N&cursor=…`; `?page=N` alone (an old link) is the first page. **Previous** uses the cursor the visitor came
forward with. Someone who lands deep in the list from a shared link gets **First page** instead, because a cursor
can't go backwards.

**Mobile first.** Below 720px the pager puts "Page N of M" on its own line, with the buttons under it as 48px-tall
targets. The buttons share the row, or take a row each below about 340px. Previous stays left and Next right. Desktop
keeps the spike's single row. A page turn lands on the new works: the count gets focus, so a keyboard or screen-reader
user isn't left on a Next button that has gone, and it is scrolled under the fixed bar (`scroll-margin-top`), not to
the hero, which on a phone is a screen and a half away. Category and order change the list in place without moving
the visitor, as the class filters already do. These links carry real hrefs, not `routerLink`, which would navigate
a second time and scroll to the top; only a plain click is taken over.

### Unpriced is not for sale

A piece, a class run or a tier whose price Square has not reported shows **"Price to be announced"** where the figure
would be: a small uppercase label like the count's. On a two-up phone grid, a card puts it under the artist. There is
no Buy, Reserve or Join, and no JSON-LD `Offer`. An artist credited without a published page is named without a link.

### The page shapes stay; the models mirror the API

`src/app/models/site.ts` mirrors the `Site*ServiceModel`s exactly, with decoders. `site-mappers.ts` turns them into the
spike's view shapes, so the templates barely changed. UTC instants become wall-clock ISO strings in the run's display
zone, because the calendar orders a day by `start.slice(11)`. A class becomes one entry per upcoming run, each with
that run's instructor, price and seats.

### Buy, cart and join links stay on WooCommerce until Phase 5

They move unchanged into `LegacyStore`, with one forced change: WooCommerce's numeric ids are gone, so Buy opens the
WooCommerce product page by slug instead of adding an id to the cart.

### Allowed hosts from configuration

`angular.json` keeps only `localhost`, which `ng serve` needs. Every other host comes from Angular's own
`NG_ALLOWED_HOSTS`, merged with that list. The AppHost sets it from the `site-allowed-hosts` parameter; publish
mode's Container Apps domain and `6311arts.com` are 6.1's.

### Run mode trusts the developer certificate

The gateway is HTTPS in run mode, and Node trusts only its own CA list. The site resource calls
`WithDeveloperCertificateTrust(true)`, so Aspire hands Node the certificate through `NODE_EXTRA_CA_CERTS` in run mode
only ([certificate trust](https://learn.microsoft.com/en-us/dotnet/aspire/app-host/certificate-trust)).
`SITE_API_ORIGIN` is a reference, not a `WaitFor`: the gateway already waits on the site.

## Options considered

- **Absolute addresses on both sides** (the browser's from `location.origin`). The cache keys would match, but
  frontend.md forbids origins in client code, and nothing would be gained.
- **`HTTP_TRANSFER_CACHE_ORIGIN_MAP`.** It maps an origin to another origin; the browser's address has none.
- **An interceptor that re-targets on the server.** User interceptors run before Angular's root ones, so the transfer
  cache would key the absolute address. Declined.
- **Relying on Angular resolving relative addresses against the request.** That is the site's own origin (YARP sends
  the destination's host), so the read would hit the Node server, not the gateway. Declined.
- **Always falling back to sample data** (the spike). Invented classes on a live page, and in the index. Declined, kept
  as an opt-in.
- **A cursor-only pager without a count.** It drops a client requirement. Declined.
- **Walking every cursor page on the server to count and number pages.** Several API reads per render, and it
  bypasses the cache. Declined.
- **The total on every `CursorPage`.** It would make every CMS list count on every request for a number no CMS screen
  shows. Declined for a separate type on the one list that needs it.

## Consequences

- With equal data, the server-rendered `<head>` of `/`, `/classes`, a class, `/exhibitions` and `/memberships` is
  byte-for-byte the spike's (compared with build hashes masked). Against the dev seed, the differences are data: class
  pictures not yet uploaded, Square prices not yet reported (so no `Offer`), and the seed's own slugs.
- `/memberships` renders on request instead of being prerendered, because the tiers are the CMS's.
- A Previous link from a shared deep link goes to the first page.
- **Rate limiting (open, scheduled in prompt 6.1).** The gateway's per-IP limiter sees every SSR read as coming from the site
  container's one address, so in Azure all SSR reads share one `api` budget (300/min) and the global 600/min. The site
  needs its own partition, or a trusted identity at the gateway, before launch.
- Every `src/app/models` change on the site is a contract change for `api-contract-checker`, as it already was for
  the CMS.
