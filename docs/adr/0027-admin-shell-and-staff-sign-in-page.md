# 0027 — The CMS shell, and the staff sign-in page on the site

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/frontend.md`, `.claude/rules/aspire.md`, CLAUDE.md "Usage"
- **Builds on:** [0001](0001-single-origin-yarp-gateway-bff.md), [0018](0018-no-two-factor-authentication.md),
  [0024](0024-bff-session-tickets-and-edge-antiforgery.md), [0025](0025-ui-library-tokens-and-primitives.md)

## Context

ADR 0024 finished the gateway's half of a browser session: tickets, the `/admin/**` gate, the antiforgery pair.
It left the browser's half to "the prompt that adds the sign-in page", and that is prompt 2.2. The forces:

- `/admin/**` is behind the `CmsEditor` gate, so the sign-in page cannot live in the CMS bundle (0024).
- The browser may hold nothing but cookies. The session's display name and roles come from `/bff/session` every
  time.
- `?returnUrl` comes from the address bar, so the page that honours it is an open redirect unless it is careful,
  and a real 6311arts sign-in page in front of a phishing site is the worst kind.
- Neither Angular app had a resource in the AppHost: the site's was planned for 3.2.

## Decision

### The sign-in page is a site route, rendered on the server and inert until hydrated

- **`/signin` on `projects/site`**, inside the site's own header and footer, `noindex`, and
  `Cache-Control: no-store` set through `RESPONSE_INIT` (which `server.ts` respects rather than overwriting with
  its `s-maxage`).
- **The form posts JSON to `/bff/login` through a typed service**, after a `GET /bff/antiforgery` for a pair bound
  to whoever the browser is at that moment. A stale-token refusal fetches a new pair and retries exactly once.
  Login failures arrive as the gateway's one answer (`auth.credentials_invalid`), and the page shows one message
  for all of them; a 429 shows its `Retry-After` in minutes.
- **Its fields stay disabled until the app runs in the browser**, and the form carries `method="post"`. Submitted
  natively before hydration — the event-replay script is blocked by the CSP today, so nothing would replay it — a
  form with no method would put the password in the query string.
- **A non-staff account is not sent into the CMS.** If the destination is under `/admin` and the new session has
  neither `Admin` nor `Editor`, the page says so instead of leaving for a 403.
- **`safeReturnUrl`** accepts only a path on this origin, and falls back to `/admin/` otherwise. It refuses:
  - anything not starting with exactly one `/`
  - a backslash or a control character anywhere (browsers read `\` as `/` and strip tabs and newlines)
  - anything that resolves to another origin
  - **a normalised path starting with `//`.** `/admin/../..//evil.example` passes every raw check and resolves to
    `//evil.example`. A unit test found this; the check runs on the parsed path, not only the raw string.
  - `/signin`, `/bff/**` and `/api/**` as destinations, compared after percent-decoding (`/%62ff/…` is `/bff/…`)
  - anything that is not a string: a repeated `?returnUrl=…&returnUrl=…` reaches the page as an array, and crashed
    the first version (found by the security review)

### The CMS shell

- **`SessionService`** holds a signal of `loading | signed-in | signed-out | unavailable`, in memory only, from
  `/bff/session`. `sessionGuard` sends a signed-out visit to `/signin?returnUrl=<the full /admin path>` by full
  navigation (another app), shows a signed-in non-staff account a "not available" page (never sign-in: 0024), and
  shows an "unavailable" page with a retry when the gateway cannot answer. `roleGuard` limits a section by role;
  **Sync is Admin-only** because it is where outbox messages are replayed or discarded (`auth.md`). Nav entries a
  role cannot open are hidden. These guards only shape the UI: the gateway and the API's policies are the locks.
- **XSRF is Angular's own interceptor**, configured with the gateway's names (`__Host-6311.csrf`,
  `X-XSRF-TOKEN`); it adds the header only to unsafe, relative requests, so the token never goes to another
  origin. An `antiforgeryInterceptor` (relative URLs only, for the same reason) handles `auth.antiforgery_required` by fetching `/bff/antiforgery` and
  resending once. **It sets the header itself on the retry**, because Angular's XSRF interceptor runs before
  every `withInterceptors` function and would not see the new cookie; and it waits for the response event, not the
  first event — the stream emits "sent" first, and acting on that fired the retry twice (found by a test).
- **`problemInterceptor`**: a 401 means the session ended, so the CMS goes to sign-in and comes back to the same
  screen — for every request except the `/bff/session` probe (`SESSION_PROBE`), whose 401 is the answer the guard
  acts on; any other failure becomes a toast carrying the ProblemDetails title and correlation id. A screen that
  shows a failure in place (a 409 conflict view, field errors) sets `SUPPRESS_PROBLEM_TOAST`. Toasts stay until
  dismissed (WCAG 2.2.1), in a live region that exists before anything is shown.
- **`unsavedChangesGuard`** asks in a `ui-dialog` ("Keep editing" / "Discard changes"); closing the dialog any other
  way is "keep". `warnBeforeUnload` covers closing the tab, which the router never sees.
- **No CRUD.** Each section is a lazy route to a placeholder; each later prompt replaces its `loadComponent`.

### Both apps run in the AppHost now

- `AddJavaScriptApp("site")` and `AddJavaScriptApp("admin")`, from the one `src/web` workspace, each with a
  non-external HTTP endpoint and a health check (`/robots.txt`, `/admin/`). The gateway references and waits for
  both. **The site arrives ahead of 3.2** because `/signin` is a site route; 3.2 keeps the site's API wiring.
- **`scripts/ng-serve.mjs`** bridges Aspire's `PORT` to `ng serve --port`, which an npm script cannot do portably.
  It also turns **live reload and HMR off**: the browser reaches the dev servers only through the gateway, which did
  not carry Vite's websocket (the handshake answered 400), and the client's fallback to the dev server's own port
  is refused by `connect-src 'self'` — correctly. Changes still rebuild; a refresh shows them.
- **Each app has Aspire's installer, and the admin's waits for the site's.** Both share one `node_modules`, and
  two installs in one folder at once corrupt it. Installs run with `--no-save`, so starting the system never
  rewrites `package-lock.json` — the first run without it did.

### The CSP allows Google Fonts, and nothing else new

`style-src` gains `https://fonts.googleapis.com` and `font-src` gains `https://fonts.gstatic.com`, in
`Gateway:Csp:Default`. Open Sans then renders exactly as on spike0 (design-system.md rule 6). Scripts stay
same-origin only; a test pins all four directives.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Sign-in on the site (chosen)** | One more site route; the page shares the site's chrome. |
| Sign-in inside the admin bundle | Redirects to itself forever and cannot load its own scripts (0024). |
| An ungated `/admin/signin` carved out of the gate | A path exception in the gateway's most important rule, for no gain. |
| **Google Fonts allowed in the CSP (chosen)** | Two third-party origins, and visitors' browsers contact Google. Pixel-identical to spike0. |
| Self-hosted Open Sans | No third-party origin and better privacy; changes the site's `index.html` and risks rendering drift against spike0. **DECIDE before launch.** |
| **Installers chained (chosen)** | A clean clone needs no npm step; the second install is a near no-op. |
| No installers, `npm ci` as a prerequisite | `WithNpm(install: false)` does not remove the installer `AddJavaScriptApp` already created (found by a model test), so this needs model surgery. |
| `RenderMode.Client` for `/signin` | No pre-hydration form at all, but the page loses its server-rendered header, footer and headers. |

## Consequences

- **`/signin` → `/admin` works end to end through the gateway**, and the E2E test walks it: 302 to sign-in, the
  page renders `no-store` and `noindex`, a BFF sign-in, then `/admin/` serves the CMS.
- **Still owed by 3.2:** the site's two inline scripts (`index.html`'s `js` class, Angular's event-replay
  contract) are blocked by `script-src 'self'`, so scroll reveals stay off (content stays visible) and early
  clicks are not replayed. They need a nonce or hashes. The site's WordPress images (`i0.wp.com`) are blocked too,
  until 3.2/3.3 serve them from `/media`.
- **Still owed by 6.1:** publish-mode shapes for both apps (a Node server for the site, a static host for the CMS).
- **The password reset and email confirmation pages** that ADR 0023 and 0024 deferred here are prompts **2.2a**
  (Identity routes and BFF endpoints) and **2.2b** (the site pages), so the emailed links have somewhere to land.
- **Owed by the first CMS editor (2.3+), from the 2.2 reviews:**
  - A 401 navigates to sign-in at once, so unsaved edits are lost when a session ends mid-edit. The first editor
    that can hold work owes a "your session ended" dialog (sign in in a new tab, keep the draft) instead of the
    redirect, wired to `unsavedChangesGuard`'s dirty state.
  - Toasts sit fixed at the bottom right; on a narrow screen they can cover the focused control (WCAG 2.2 2.4.11).
    Move them into the flow, or pad the scroll area, when the first screen with lower-page controls lands.
  - `@6311/ui` has no danger variant for a destructive confirm; add one before the first delete.
- **The Google Fonts allowance is in `Csp:Default`, so it covers every route**, `/signin` included. It adds no
  script or connect origin; self-hosting (the DECIDE above) removes it and the privacy cost together.
- **The CI E2E job needs Node**, since the AppHost now starts both dev servers.
- **`npm test` now runs three projects** (`ui`, `site`, `admin`), and `npm run build` builds all three.
