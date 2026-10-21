---
paths:
  - src/web/**
---
# Angular Frontend

`src/web/` is one Angular 22 workspace with three projects:

| Project | What it is |
|---|---|
| `projects/site` | The public site, server-rendered (`@angular/ssr` + Express). Migrated from `spike0`. |
| `projects/admin` | The CMS. A client-rendered SPA served under `/admin/`. |
| `projects/ui` | `@6311/ui`: the design tokens and the few primitives both apps share (`design-system.md`) |

## Architecture

- **Components.**
  - Standalone components only, no NgModules.
  - Strict TypeScript and strict templates.
  - Zoneless.
  - `OnPush` everywhere.
- **State.** Signals for local and derived state; RxJS for streams and interop.
- **HTTP.** Typed services in `src/app/services/` own all HTTP. Components never inject `HttpClient`.
- **Models.** `src/app/models/` mirrors the API ServiceModels exactly: string ids, integer cents, ISO strings. Check them with `api-contract-checker`.
- **Folders.**
  - `core/`: session, interceptors, guards, SEO
  - `shared/`: compositions of `@6311/ui`
  - `features/` (admin) or `pages/` (site): lazy routes
- **Logic.** Never use `any`. Decode unknown data at boundaries.
- **Workspace config.** Application tsconfigs live at the workspace root as `src/web/tsconfig.<project>.json` (+ `tsconfig.<project>.spec.json`), never inside `projects/<app>/`. `@angular/build` names lazy routes relative to the tsconfig's folder but keys the SSR manifest by workspace-relative paths, so a tsconfig under the project folder silently drops route modulepreload hints (see the comment in `tsconfig.site.json`). Don't move them. `projects/ui` is a library and uses a library tsconfig.

## Calling the API

- **Browser calls** use relative `/api/v1/...` and `/bff/...` URLs, with credentials and the XSRF header (interceptor). There are no absolute origins anywhere in client code.
- **SSR calls** use the gateway origin injected by Aspire into the Node server environment (`SITE_API_ORIGIN`). That's server-side config, never bundled.
  - The site's `SiteApi` asks for relative `/api/v1/site/...` on both sides, and the server's `GatewayBackend` (an
    `HttpBackend`, so below the transfer cache) re-targets them to that origin. Never re-target in an interceptor:
    the transfer cache would key the absolute address and the browser would fetch every page twice (ADR 0046).
  - A failed read is an empty answer and, on the server, a 503 `no-store`; sample data only with
    `SITE_MOCK_FALLBACK=true`. A record Square has not priced shows "Price to be announced" and is not for sale (ADR 0042, 0046).
- **Session** state comes from `/bff/session`. No tokens or user data in browser storage.
- **XSRF** is Angular's own interceptor via `withXsrfConfiguration({ cookieName: '__Host-6311.csrf', headerName: 'X-XSRF-TOKEN' })`. It runs before every `withInterceptors` function, so an interceptor that retries a write sets the header itself (ADR 0027).
- **Leaving an app** (site → CMS, CMS → `/signin`) is a full navigation through an injectable (`BrowserNavigation`, `BrowserLocation`), never `window.location` in a component, so tests can see it.
- **`?returnUrl`** is untrusted: read it only through the site's `safeReturnUrl` (ADR 0027).
- **Emailed-link credentials** (`#userId=…&token=…`) are taken out of the address bar by `captureLinkFragment` in
  `main.ts`, before bootstrap, and handed to the page once through `LinkFragment`. Never read `location.hash` for
  them in a component, and never keep them anywhere but a private field (ADR 0029).

## Site (SSR) — keep what the spike got right

- **Keep from the spike:**
  - `SeoService` (title, description, canonical, Open Graph and Twitter tags)
  - JSON-LD (`ArtGallery`, `EducationEvent`, `ExhibitionEvent`, `Product`, `BreadcrumbList`)
  - `/sitemap.xml` and `/robots.txt`
  - real 404 status
  - SSR render modes (`app.routes.server.ts`)
  - `Cache-Control: s-maxage` headers
  - HTTP transfer cache
  - hydration with event replay
  - scroll animations that never hide content
- **Legacy URL redirects** come from `/api/v1/site/redirects` (CMS-managed and imported), with the spike's hard-coded map as a fallback. The order is the CMS's list, then the spike's map, then `/product/{slug}` (a published class's slug → `/classes`, otherwise `/shop`) and `/artist/{slug}` (`server/legacy-redirects.ts`). `server/site-data.ts` caches the routing data for 60 s and refreshes it behind the request (5 s after a failed read). Every legacy URL in the spike's map must still 301 with the API down (ADR 0048).
- **`/sitemap.xml`** comes from `/api/v1/site/sitemap`. When that can't be read it is a `503` with `Retry-After`, never a shorter list. `robots.txt` is fixed text (ADR 0048).
- **Forms** post through `FormsApi` to `/api/v1/forms/*` with the antiforgery header. The site server has no form routes of its own (ADR 0047, 0048).
- **The logo and default social image** are Media assets at the fixed ids in `SITE_IMAGES` (`core/site.ts`), mirroring `MediaSiteImages`. Name them as `/media/{id}/{rendition}`, never a WordPress upload URL (ADR 0048).
- **Checkout and member billing** load the Square Web Payments SDK. It runs in the browser only, never during SSR, and its host comes from config. Card fields stay inside Square's iframes.
- **`security.allowedHosts`** comes from configuration (local gateway host, Container Apps domains, `6311arts.com`): `angular.json` keeps only `localhost` for `ng serve`, and the AppHost's `site-allowed-hosts` parameter becomes Angular's `NG_ALLOWED_HOSTS` (ADR 0046).

## Admin

- **Every data surface** has loading, empty, error and success states.
- **Unsaved edits** survive recoverable errors and warn on navigation.
- **Square-held values** (price, stock, **seats remaining**, sold-out) are never edited in place. Before the first sync, the starting price and quantity are plain fields. After it, each value shows Square's current figure with a **Change** action that sends a command and shows its Pending / Applied / Rejected state, plus the sync status chip (ADR 0032).
  - **A membership plan's price and billing cadence** are plain fields before the first push and Square's figures
    afterwards, read-only with **no** Change action: a plan has no such command (ADR 0005, 0040). The editor says so
    and names Square as where they change.
  - **A class run's capacity is the CMS's, not Square's** (ADR 0032, 0044), so it stays an ordinary field after the
    first sync; a change to it is what makes the seat adjustment. What is read-only beside it is the seats *remaining*.
  - The sync status chip needs a `SyncStatus` to show, which arrives with `SquareCatalogLink` in 5.1. Until then a
    class run or shop item reads "Not in Square yet" or shows Square's own figures (ADR 0037, 0038, 0044).
  - **A shop item's one-of-a-kind flag and starting count** are editable only before the first sync, and the count
    field is hidden while the flag is set: there is one of it, so there is nothing to type (ADR 0038).
- **A 409 conflict** shows both versions and keeps the editor's changes.
- **The shell** (2.2, ADR 0027): `SessionService` (signal, memory only), `sessionGuard` on the shell, `roleGuard(...)` per section (Sync is Admin-only), `unsavedChangesGuard` + `warnBeforeUnload` on editors, and the problem interceptor's toasts. A screen that shows a failure in place sets `SUPPRESS_PROBLEM_TOAST`. A new section is an entry in `features/layout/sections.ts`; its prompt swaps the placeholder `loadComponent`. A record's editor (`artists/new`, `artists/:id`) is an entry in `DETAIL_ROUTES` in `app.routes.ts`, which gives it the section's roles and the unsaved-changes guard.
- **HTTP backend.** The CMS uses Angular's default XHR backend, not `withFetch()`, because fetch can't report upload progress (ADR 0030). The site keeps `withFetch()`.
- **Media.** `features/media/` holds the library, uploader, editor, and `MediaPicker` (`<cms-media-picker [(open)] (picked)>`) for any editor that needs an image.
- **Dev servers** run without live reload: the gateway does not carry Vite's websocket. Refresh to see a change (ADR 0027).

## Verification

From `src/web/`: `npm ci`, then `npm run build`, which builds every project: `ui` (the ng-packagr strict check), `site`, then `admin`. Unit tests run on **Vitest** (decided 2026-10-19, ADR 0016) through `npm test`, which runs the `ui`, `site` and `admin` `test` targets in turn (ADR 0027). Specs sit beside the code they test (`*.spec.ts`). Accessibility target is WCAG 2.2 AA: keyboard operation, visible focus, 200% zoom, narrow layouts, reduced motion.
