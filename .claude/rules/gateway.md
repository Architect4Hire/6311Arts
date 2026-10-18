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
| `/api/**` | `https+http://api` | Internal token attached; client credentials stripped |
| `/webhooks/square` | `https+http://api` | POST only, 256 KB limit, no session, rate limited |
| `/media/**` | `https+http://api` | Long-cache image delivery |
| `/admin/**` | `https+http://admin` | CMS SPA bundle; requires a `CmsEditor` session |
| `/**` | `https+http://site` | Angular SSR site |

Destinations resolve through Aspire service discovery. Nothing in the gateway names a host or port.

## Edge policy

- **Headers.** Strip client `Authorization`, `Cookie` (after reading the session), `X-Forwarded-*` and any internal header before forwarding. Forwarded headers come only from configured trusted proxies.
- **Rate limits**, with named policies:
  - `bff-login`
  - `forms` (submissions, newsletter, contact)
  - `checkout`
  - `webhooks`
  - a generous global limit
- **Security headers:**
  - HSTS (outside development)
  - `X-Content-Type-Options`
  - `Referrer-Policy`
  - `frame-ancestors 'none'`
  - CSP from configuration. The Square Web Payments SDK origins (script, frame, connect) are allowed **only** on checkout and member-billing routes, and the sandbox vs production SDK host comes from configuration.
- **Limits and correlation.**
  - Request size limits per route; uploads are streamed.
  - A correlation id is created or propagated and returned in `ProblemDetails`.
- **Caching.** Never cache personalized responses. SSR cache headers come from the site; the gateway adds compression only.
- **API docs.** OpenAPI and Scalar are proxied in Development only.

## Not the gateway's job

No business logic, data access, Square calls or content decisions. The gateway authenticates the browser session, enforces edge policy and forwards.

## Tests

- Route resolution for every row above.
- `/api/v1/internal/**` gives a 404.
- Client `Authorization` is stripped.
- A 429 per rate-limit policy.
- CSP differs between checkout and other pages.
- Security headers are present.
- `/admin` without a session redirects to login.
