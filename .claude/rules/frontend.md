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

## Calling the API

- **Browser calls** use relative `/api/v1/...` and `/bff/...` URLs, with credentials and the XSRF header (interceptor). There are no absolute origins anywhere in client code.
- **SSR calls** use the gateway origin injected by Aspire into the Node server environment. That's server-side config, never bundled.
- **Session** state comes from `/bff/session`. No tokens or user data in browser storage.

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
- **Legacy URL redirects** come from `/api/v1/site/redirects` (CMS-managed and imported), with the spike's hard-coded map as a fallback.
- **Checkout and member billing** load the Square Web Payments SDK. It runs in the browser only, never during SSR, and its host comes from config. Card fields stay inside Square's iframes.
- **`security.allowedHosts`** comes from configuration (local gateway host, Container Apps domains, `6311arts.com`).

## Admin

- **Every data surface** has loading, empty, error and success states.
- **Unsaved edits** survive recoverable errors and warn on navigation.
- **Square-owned values** (price, stock or seats, sold-out) show as read-only, with "Managed in Square" and the sync status chip.
- **A 409 conflict** shows both versions and keeps the editor's changes.

## Verification

From `src/web/`: `npm ci`, `npm run build` (all projects), `npm test`. Accessibility target is WCAG 2.2 AA: keyboard operation, visible focus, 200% zoom, narrow layouts, reduced motion.
