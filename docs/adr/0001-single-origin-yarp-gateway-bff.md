# 0001 — Single-origin YARP gateway as BFF

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/gateway.md`, `.claude/rules/auth.md`, `.claude/rules/aspire.md`
- **Adopts:** creatorpantry B-02 (the SPA talks only to the gateway)

## Context

The browser needs the public site, the CMS admin, the API, media and the Square webhook. If each one had its own origin, the platform would need CORS, cross-site cookies, several CSPs and several sets of rate limits. Square signs webhooks over the notification URL, so that URL must be stable and public. Session security (ADR 0002) needs one place that owns the cookie and can strip client credentials.

## Decision

`Arts6311.Gateway`, an ASP.NET Core host running YARP, is the **only** resource with an external endpoint, locally and in Azure. It routes `/bff/**` itself, sends `/api/**`, `/webhooks/square` and `/media/**` to the API, `/admin/**` to the admin app and `/**` to the SSR site. Routes come from `ReverseProxy` configuration, and destinations resolve through Aspire service discovery. The gateway is a **custom host, not the Aspire YARP container**. It runs code a config-only proxy can't run: session tickets, ES256 token signing, antiforgery, header stripping and route-scoped CSP. It has no business logic, data access or Square calls.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Custom YARP host as BFF (chosen)** | One more .NET project to own. One origin; first-party `__Host-` cookie; one CSP and rate-limit point. |
| Aspire's YARP container (config only) | Less code, but it can't hold sessions or mint the internal token. A second auth component would still be needed. |
| Separate origins (`api.`, `admin.`, `www.`) + CORS | No gateway, but cross-site cookies, CORS preflights, CSP per app, and webhook and API exposed directly. |
| Azure Front Door / APIM as the edge | Managed edge, but it costs money, can't run in run mode offline (ADR 0008), and session logic still needs a host. |

## Consequences

- Browse only through the gateway URL. Apps on their own ports aren't a supported way to run the system.
- The gateway is on every request's path: its health, scale (min 1 in prod) and latency matter.
- Square's webhook notification URL is the gateway's public URL, from configuration.
- `/api/v1/internal/**` returns 404 from browsers, always.
- The temporary Static Web Apps review copy (ADR 0014) is a known, time-boxed exception to "one public endpoint".
