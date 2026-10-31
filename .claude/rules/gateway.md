---
paths:
  - src/Arts6311.Gateway/**
---
# Gateway and Browser Boundary

`Arts6311.Gateway` (ASP.NET Core + YARP) is the only resource with an external endpoint, locally and in Azure. Everything the browser sees comes from one origin. Cookies, antiforgery, CSP and Square webhooks all depend on that.

## Routes (config-driven, in `appsettings.json` `ReverseProxy`)

| Path | Destination | Notes |
|---|---|---|
| `/bff/**` | gateway itself | Session endpoints (`auth.md`) |
| `/api/v1/internal/**` | — | 404 from browsers, always |
| `/api/v1/cms/**` | `https+http://api` | Requires a `CmsEditor` session; internal token attached |
| `POST /api/v1/cms/media` (`cms-media-upload`) | `https+http://api` | As `cms`, with `MaxRequestBodySize` set to `MediaPolicy.MaxUploadRequestBytes` (26 MiB): the CMS's one streamed upload (ADR 0030) |
| `POST /api/v1/cms/shop/import/**` (`cms-shop-import`) | `https+http://api` | As `cms`, with `MaxRequestBodySize` set to `ShopPolicy.ImportMaxRequestBytes` (8 MiB): a thousand-row CSV as JSON text, read whole (ADR 0053) |
| `/api/v1/forms/**` (`forms`) | `https+http://api` | The public forms (ADR 0047): anonymous, `forms` rate limit, antiforgery as for every unsafe request |
| `POST /api/v1/forms/submissions` (`forms-submissions`) | `https+http://api` | As `forms`, but its own `forms-submissions` rate limit (3 an hour) and `MaxRequestBodySize` set to `FormsPolicy.MaxSubmissionRequestBytes` (64 MiB: six 10 MiB pictures and the answers) |
| `/api/**` | `https+http://api` | Internal token attached; client credentials stripped |
| `/webhooks/square` | `https+http://api` | POST only, 256 KB limit, no session, rate limited |
| `/media/**` | `https+http://api` | GET and HEAD only. Long-cache image delivery; a user token is attached when there is a session, for drafts |
| `/admin/**` | `https+http://admin` | CMS SPA bundle; requires a `CmsEditor` session |
| `/signin` | `https+http://site` | Part of the site catch-all. Where a gated navigation is redirected, so it must stay anonymous |
| `/**` | `https+http://site` | Angular SSR site |

Destinations resolve through Aspire service discovery. Nothing in the gateway names a host or port.

In run mode the gateway listens on **HTTPS** (the developer certificate), because its `Secure`, `__Host-` cookies and `SecurePolicy = Always` antiforgery refuse plain HTTP. Behind the ACA ingress it sees plain HTTP, so 6.1 must trust the ingress's forwarded headers (`Gateway:TrustedProxies`) before the BFF works there (ADR 0026).

The session gate is the route's own `AuthorizationPolicy`, in that same configuration — not a path list in code.
**A route that requires a session and forwards to the API must also be token-bearing**
(`GatewayTransforms.TokenBearingRoutes`), or its requests reach the API with no credential; a test asserts it.
`admin` is the one session-gated route deliberately left out: it serves the CMS bundle from the admin container,
which has no API call to authorize. The rule is about routes whose destination is the API.

## Edge policy

- **Headers.** Strip client `Authorization`, `Cookie` (after reading the session), `X-Forwarded-*` and any internal header before forwarding. Forwarded headers come only from configured trusted proxies.
- **Rate limits**, named fixed-window policies from `Gateway:RateLimits`, partitioned by client IP. Five were defined in 1.8 so a prompt adding a route references a name rather than inventing a limit; a policy a route names but configuration omits stops the host at startup.
  - `bff-login` (1.10)
  - `bff-account` — `/bff/forgot-password`, `/bff/resend-confirmation`, `/bff/reset-password`,
    `/bff/confirm-email` (2.2a/2.2b, ADR 0028, 0029); its own window so reset traffic cannot spend the sign-in
    budget
  - `forms` (newsletter and contact — 2.12)
  - `forms-submissions` (artist submissions: 3 an hour, because each may carry six pictures — ADR 0047)
  - `checkout` (5.14)
  - `webhooks` — on `/webhooks/square` from 1.8
  - `api` — on `/api/**` from 1.8
  - a generous global limit (`Gateway:GlobalRateLimit`)

  A 429 is `ProblemDetails` with code `request.rate_limited` and a `Retry-After` header.
- **Security headers:**
  - HSTS (outside development)
  - `X-Content-Type-Options`
  - `Referrer-Policy`
  - `frame-ancestors 'none'`
  - CSP from configuration: `Gateway:Csp:Default`, plus `Gateway:Csp:Overrides` as a path prefix → policy map, longest prefix winning. The Square Web Payments SDK origins (script, frame, connect) are allowed **only** on checkout and member-billing routes, as override entries added in 5.14; the sandbox vs production SDK host comes from configuration.
  - **`script-src` carries three `sha256-` hashes and no `'unsafe-inline'`** (ADR 0039): the site's `js`-class
    script, Angular's event-dispatch contract and the `__jsaction_bootstrap` call that arms event replay. Never a
    nonce — SSR pages are cached with `s-maxage`, so a per-response nonce and the cached body drift apart. The
    bootstrap script's text embeds the event types the site's templates bind, so **a new event binding type in
    `projects/site` changes its hash**; the `Category=E2E` test
    `EveryInlineScriptOnTheRenderedSiteIsAllowedByItsOwnCsp` recomputes the hashes from the rendered page and names
    any that the policy no longer allows. A new inline script is refused by design — bundle it instead.
  - **`Gateway:Csp:AdditionalSources`** (directive → extra sources) appends to whichever policy a path resolved
    to, and only to a directive that policy already names. It is how an *environment* adds a source. No
    environment uses it today: the WordPress origins (`https://6311arts.com`, `https://i0.wp.com`) it once added
    in Development are gone, the site serves its own images from `public/img/`, and
    `NoEnvironmentAllowsTheWordPressOrigins` keeps them out of every policy (ADR 0039). **An environment never restates `Default`:** a second copy of the policy is a second copy
    of the hashes, and the dev copy will mask a stale one in the shipped copy.
- **Limits and correlation.**
  - Request size limits: **1 MiB for every route** (`Kestrel:Limits:MaxRequestBodySize`, in the gateway and the API), with per-route YARP `MaxRequestBodySize` above or below it. `/webhooks/square` is capped at 256 KB; `cms-media-upload` is raised to 26 MiB, `cms-shop-import` to 8 MiB and `forms-submissions` to 64 MiB. Uploads are streamed; the shop import's CSV is text in a JSON body, bounded by that limit.
  - Forwarded headers are honoured only when `Gateway:TrustedProxies` names the sender; with it empty the middleware that reads them is not in the pipeline at all, and client-supplied `X-Forwarded-*` and `Forwarded` are stripped inbound (ADR 0022).
  - A correlation id is created or propagated and returned in `ProblemDetails`.
  - `AllowedHosts` is `*` in run mode and **must be set for the deployed origin in 6.1**: the client `Host` becomes `X-Forwarded-Host`, which the SSR site may build absolute URLs from.
- **Session and CSRF.** The server-side ticket, the `__Host-` cookies and the antiforgery pair are `auth.md`'s,
  and ADR 0024 records the shapes. The middleware order they depend on: authentication, then antiforgery, then
  authorization.
- **Caching.** Never cache personalized responses. SSR cache headers come from the site; the gateway adds compression only.
- **API docs.** OpenAPI and Scalar are proxied in Development only.

## Not the gateway's job

No business logic, data access, Square calls or content decisions. The gateway authenticates the browser session, enforces edge policy and forwards.

## Tests

- Route resolution for every row above.
- `/api/v1/internal/**` gives a 404, and the API never receives the request.
- Client `Authorization` is stripped.
- A 429 per rate-limit policy.
- CSP differs between checkout and other pages.
- Security headers are present.
- `/admin` without a session redirects to `/signin`; `/api/v1/cms/**` without a session is a 401, and neither
  reaches the API.
- A `CmsEditor` session reaches the CMS surface carrying a user token; a `Member` session gets a 403.
- An unsafe request without antiforgery is a 400, and `/webhooks/square` is exempt.
