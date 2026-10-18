# 6311arts.com — Angular 22 front end

A server-rendered, SEO-first marketing site for 6311arts, styled after the CSoon template (black-and-white photo hero,
charcoal and pale-gray bands, uppercase letter-spaced type, square outlined buttons, thin line icons) and rebuilt as a
multipage Angular app with no Bootstrap or jQuery.

WordPress and WooCommerce stay exactly where they are. Staff keep adding classes, artists, products and pages in
wp-admin; this app reads them over the REST API and sends buyers to the existing WooCommerce cart and checkout.

## Stack

- Angular 22.2 (standalone components, signals, zoneless change detection)
- `@angular/ssr` with Express: hybrid rendering, hydration with event replay, HTTP transfer cache
- Plain SCSS design system in `src/styles.scss`; Open Sans; inline SVG icons
- Node 22.22.3+ or 24.15+ (required by Angular CLI 22)

## Run it

```bash
npm install
npm start                      # dev server on http://localhost:4200
npm run build                  # production build
npm run serve:ssr:site6311     # serve the build on http://localhost:4000
```

Environment variables for the server:

| Variable | Default | What it does |
|---|---|---|
| `PORT` | `4000` | Port to listen on |
| `SITE_URL` | `https://6311arts.com` | Used in sitemap and robots |
| `WP_ORIGIN` | `https://6311arts.com` | WordPress origin for the sitemap and redirects |
| `NEWSLETTER_WEBHOOK_URL` | unset | Where `/api/subscribe` forwards sign-ups (e.g. a Zapier/Make hook into MailPoet) |
| `CONTACT_WEBHOOK_URL` | unset | Where `/api/contact` forwards messages (e.g. a hook that emails info@) |

The browser-side WordPress origin is the `API_CONFIG` token in `src/app/core/site.ts`. If WordPress moves to a
subdomain (say `cms.6311arts.com`) so the Angular app can own `6311arts.com`, change it there.

## Pages

| Route | Rendering | Source |
|---|---|---|
| `/` | Server, per request | Exhibitions, classes and products |
| `/classes`, `/classes/:slug` | Server | `6311/v1/classes` (FooEvents products) |
| `/exhibitions` | Server | `6311/v1/exhibitions` |
| `/artists`, `/artists/:slug` | Server | `6311/v1/artists` and the Store API |
| `/shop`, `/shop/:slug` | Server | WooCommerce Store API |
| `/memberships`, `/about`, `/visit`, `/submit` | Prerendered at build | Copy in the components |
| `/privacy`, `/terms` | Server | WordPress pages `privacy-policy` and `terms-and-conditions` |

Server-rendered pages send `Cache-Control: s-maxage=300, stale-while-revalidate=86400`, so behind a CDN WordPress
is asked for data at most every five minutes per page.

## SEO

- Every page sets title, description, canonical, Open Graph and Twitter tags during server render (`core/seo.service.ts`).
- JSON-LD on every page: `ArtGallery` organization with address, geo and opening hours, plus `EducationEvent` for
  classes, `ExhibitionEvent` for shows, `Product` with offers for shop items and `BreadcrumbList`.
- `/sitemap.xml` is generated from live data; `/robots.txt` points to it.
- Old WordPress URLs 301 to the new ones: `/events`, `/gallery`, `/submit-your-work`, `/privacy-policy`,
  `/product/<slug>` (to `/classes/…` or `/shop/…`) and `/artist/<slug>`.
- Real 404 status for unknown pages. Scroll animations never hide content from crawlers or no-JS visitors.

## WordPress side

`wordpress/6311-headless.php` is a small read-only plugin. Install it as a must-use plugin. It:

1. Adds `GET /wp-json/6311/v1/classes`, `/exhibitions` and `/artists`.
2. Registers an **Exhibition** post type (title, excerpt, featured image, and `start_date`, `end_date`,
   `reception_start`, `reception_end`, `reception_note`, `artists` fields) so shows are edited in wp-admin instead of
   in Beaver Builder.
3. Hides `/wp/v2/users` from logged-out visitors (the user-list leak from the audit).
4. Sends CORS headers for the Angular origins.

For classes, staff add these product attributes in WooCommerce so the calendar can show them:
`instructor`, `type` (Printmaking, Drawing, Talk, Event…), `level` (Beginner, Intermediate, All levels),
`sessions` (number of weeks), `cadence` (e.g. "Saturdays") and `capacity`. Turn on stock management to get
"spots left". The FooEvents date meta keys used (`WooCommerceEventsDate`, `…Hour`, `…Minutes`, `…Period` and
their `…End` versions) should be checked against the installed FooEvents version before going live.

## Sample data

When WordPress can't be reached (or `mockOnly` is set in `API_CONFIG`), the app falls back to `src/app/data/*`:
the real classes, exhibitions, artists and a slice of the shop copied from 6311arts.com on 2026-10-18. Class
summaries and the "spots left" numbers there are drafts for illustration, not live data.

## Still to do before launch

- Drop in the real logo files (the header and hero use a typographic "6311 / arts" mark for now).
- Replace the draft marketing copy where noted and add staff photos for the About page.
- Write the Privacy Policy and Terms pages in WordPress (they're linked from every page).
- Point the two webhooks at MailPoet and the info@ inbox.
- Pick hosting for the Node server (any Node 22+ host works: Cloud Run, Render, Railway, Fly, a small VPS; or
  Firebase App Hosting / Netlify / Vercel with their Angular SSR adapters).
- The CSoon template is a WrapBootstrap commercial theme; make sure its license covers this use.
