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
| `POST /api/v1/cms/media` (`cms-media-upload`) | `https+http://api` | As `cms`, with `MaxRequestBodySize` set to `MediaPolicy.MaxUploadRequestBytes` (26 MiB): the only large-body route (ADR 0030) |
| `/api/**` | `https+http://api` | Internal token attached; client credentials stripped |
| `/webhooks/square` | `https+http://api` | POST only, 256 KB limit, no session, rate limited |
| `/media/**` | `https+http://api` | GET and HEAD only. Long-cache image delivery; a user token is attached when there is a session, for drafts |
| `/admin/**` | `https+http://admin` | CMS SPA bundle; requires a `CmsEditor` session |
| `/signin` | `https+http://site` | Part of the site catch-all. Where a gated navigation is redirected, so it must stay anonymous |
| `/**` | `https+http://site` | Angular SSR site |

Destinations resolve through Aspire service discovery. Nothing in the gateway names a host or port.

In run mode the gateway listens on **HTTPS** (the developer certificate), because its `Secure`, `__Host-` cookies and `SecurePolicy = Always` antiforgery refuse plain HTTP. Behind the ACA ingress it sees plain HTTP, so 6.1 must trust the ingress's forwarded headers (`Gateway:TrustedProxies`) before the BFF works there (ADR 0026).

The session gate is the route's own `AuthorizationPolicy`, in that same configuration — not a path list in code.
**A route that requires a session must also be token-bearing** (`GatewayTransforms.TokenBearingRoutes`), or its
requests reach the API with no credential; a test asserts it.

## Edge policy

- **Headers.** Strip client `Authorization`, `Cookie` (after reading the session), `X-Forwarded-*` and any internal header before forwarding. Forwarded headers come only from configured trusted proxies.
- **Rate limits**, named fixed-window policies from `Gateway:RateLimits`, partitioned by client IP. Five were defined in 1.8 so a prompt adding a route references a name rather than inventing a limit; a policy a route names but configuration omits stops the host at startup.
  - `bff-login` (1.10)
  - `bff-account` — `/bff/forgot-password`, `/bff/resend-confirmation`, `/bff/reset-password`,
    `/bff/confirm-email` (2.2a/2.2b, ADR 0028, 0029); its own window so reset traffic cannot spend the sign-in
    budget
  - `forms` (submissions, newsletter, contact — 2.12)
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
- **Limits and correlation.**
  - Request size limits: **1 MiB for every route** (`Kestrel:Limits:MaxRequestBodySize`, in the gateway and the API), with per-route YARP `MaxRequestBodySize` above or below it. `/webhooks/square` is capped at 256 KB; `cms-media-upload` is raised to 26 MiB. Uploads are streamed.
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
