# 0022 — Gateway edge policy and the internal-token transform

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/gateway.md`, `.claude/rules/auth.md`, `.claude/rules/aspire.md`
- **Implements:** the signing half of [0002](0002-gateway-session-es256-internal-token.md) and the single origin of [0001](0001-single-origin-yarp-gateway-bff.md); discharges the gateway obligations [0021](0021-api-host-conventions-and-internal-token-verification.md) recorded

## Context

The gateway was a three-line host. ADR 0001 makes it the only public endpoint and ADR 0002 makes it the only holder of the ES256 signing key, but neither had been built, so the API was reachable only by talking to it directly — exactly what the single-origin rule exists to prevent. Prompt 1.7 taught the API to verify a token nothing yet signed.

The forces: the browser must see one origin; the API must trust one caller; a client must not be able to present a credential, forge a forwarding header, or discover an internal route; and none of it may require a host or port to appear anywhere in the repo.

## Decision

- **The route table is configuration**, in `appsettings.json`, exactly `gateway.md`'s table: `/webhooks/square` (POST, 256 KB, rate limited), `/media/**`, `/api/**`, `/admin/**`, `/**`. Precedence is ASP.NET's most-specific-wins, so no `Order` is needed. `GatewayRouteTableTests` asserts every row, and that there is nothing beyond them — an extra route is public surface nobody reviewed.
- **Destinations resolve through Aspire service discovery** as `https+http://<name>`, the documented scheme-priority form. `WithReference` is what populates it; no host or port exists in gateway configuration.
  - **The resolved value must be a full URI**, not a bare `host:port`. With a bare authority the resolver keeps the *first* scheme of `https+http`, so an http-only destination is dialled over https and every request fails with 502. Aspire's own injection is a full URI; the test fixture matches it.
- **The full five-route table ships before `site` and `admin` exist.** The alternative — adding routes as their resources land — spreads one table across three prompts and leaves it out of step with `gateway.md` until the last of them.
  - Until 2.2 and 3.3, `/` and `/admin` answer **502**, not the 503 one might expect. Service discovery's pass-through provider resolves an unknown name to a `DnsEndPoint` for that bare name, so YARP has a destination and fails to connect to the host `site`, rather than having no destination at all. Verified by running it; worth knowing because a 502 here means "not built yet", not "the site is down".
- **Middleware order**, which is the substance of this change:

  | Position | Why there |
  |---|---|
  | `UseExceptionHandler`, `UseStatusCodePages` | first, so a fault or an empty status from anything below still answers `ProblemDetails` |
  | `UseForwardedHeaders` *(only when a proxy is trusted)* | before anything reads the client IP or scheme |
  | `UseTrustedHeaders` | immediately after, removing whatever an untrusted caller left |
  | `UseHsts` *(outside Development)* | before the response is written |
  | `UseSecurityHeaders` | before the proxy starts streaming; headers set later would be too late |
  | `UseResponseCompression` | after the headers, skipping anything the destination already encoded |
  | `UseRouting` | explicit, so the two routing-aware middlewares below do not depend on where minimal hosting would insert it |
  | `UseRateLimiter` | after routing: it reads the matched route's `RateLimiterPolicy` metadata |
  | `UseInternalPathBlock` | after edge policy, before the proxy |
  | `MapDefaultEndpoints`, `MapReverseProxy` | last |

- **`UseForwardedHeaders` runs only when `Gateway:TrustedProxies` is non-empty.** `ForwardedHeadersMiddleware` validates the sender against `KnownProxies`/`KnownIPNetworks`, and **that check cannot fail when there is no peer address** — so with nothing configured it honours a spoofed `X-Forwarded-For` on any connection without a remote IP and sets `RemoteIpAddress` from it. Not running it at all when nothing is trusted removes the case, rather than resting on a peer address always being present. This was found by a test, not by reading.
- **Client forwarding headers are stripped inbound, not by a transform.** YARP's `X-Forwarded` transform owns those header names on the proxy request and overrides a `RequestHeaderRemove` for the same name, so the removal has to happen before YARP sees the request. `TrustedHeadersMiddleware` drops `X-Forwarded-For/Proto/Host/Prefix` and RFC 7239 `Forwarded`; the transform then writes the true values.
- **Credentials and internal headers are stripped by the transform**, on every route: `Authorization`, `Cookie` (the gateway reads it for its session from 1.10 and never forwards it), and `X-Internal-Token`/`X-Gateway-User`/`X-Gateway-Session`. A **deny**-list, not YARP's `RequestHeadersAllowed` allow-list: an allow-list is stronger but would mean enumerating every legitimate header, including ones later prompts add, and a forgotten entry breaks a route silently. Recorded as later hardening once the header surface is known.
- **The internal token is attached only on routes whose cluster is `api`,** decided at transform-build time from the route's cluster id. The site and admin apps have no use for one, and a credential handed to a process that does not need it is one more place it can be logged or forwarded on. The header strip applies to every route regardless.
- **The internal token is attached only when there is a signed-in person.** An anonymous request carries no token at all rather than one saying "anonymous", so the API's `[AllowAnonymous]` routes decide what a caller with no credential may read. The token is ES256 with `iss`, `aud`, `sub`, `sid`, `role`, `token_use`, `nbf`, `iat` and `exp = nbf + 2 min` — every field ADR 0021 requires, including the `nbf` the API refuses a token without.
  - **Before 1.10 nothing is ever authenticated**, so the signing branch is unreachable end to end. `InternalTokenContractTests` covers it instead by signing here and validating with the API's own parameters, which is a stronger check than one request would have been.
  - An authenticated principal with no `sub` **throws** rather than signing: the API refuses such a token, so signing one would produce a 403 the caller cannot act on.
- **`InternalTokenDefaults` is duplicated** in the gateway. The gateway cannot reference the API — it holds no domain code, and `ProjectReferenceTests` keeps it to ServiceDefaults — and ServiceDefaults owns telemetry and health, not auth, so neither project can host a shared copy without widening its charter. The test project references both and compares them, including that every string constant on the gateway side exists on the API's with the same value.
- **The gateway injects `traceparent` itself.** YARP forwards through a bare `SocketsHttpHandler` with no `DiagnosticsHandler`, so it does not propagate the trace the way `HttpClient` would; without this the gateway's span and the API's are separate traces and CLAUDE.md's "one trace from gateway → API → SQL" is false. A client-supplied value is overwritten: ASP.NET Core has already parsed it into the current activity, which is the authority.
- **`/api/{version}/internal/**` is 404 from middleware**, matched on path segments so a future `v2` is covered without anyone remembering. 404 rather than 403 so a browser cannot tell an internal route from one that was never written, and the test asserts the API never saw the request — a 404 *from the API* would still have confirmed the route exists. Middleware rather than a YARP route because a route needs a cluster and there is nothing to forward to.
- **CSP is a default plus path-prefix overrides**, longest prefix winning. 5.14 adds the Square Web Payments SDK origins to checkout and member billing as configuration, with no code change. Rate limits are five named fixed-window policies (`bff-login`, `forms`, `checkout`, `webhooks`, `api`) plus a generous global limiter, all from configuration, partitioned by client IP; three have no route yet so that the prompts adding those routes reference a name rather than inventing a limit. A policy a route names but configuration never defines **stops the host at startup**.
- **Security headers are applied through `Response.OnStarting`,** not by setting them and calling `next()`. `ExceptionHandlerMiddleware` clears the response headers before writing its body, so on the 500 and 413 paths the headers vanish however early the middleware sits — moving it ahead of the exception handler does **not** fix this, which was established by watching a real 413 come back bare. An `OnStarting` callback runs after any such clearing and before the proxy streams. The same applies to the body: `AddProblemDetails`' customiser stamps `code` and `correlationId` onto the problems the framework writes for itself, which would otherwise carry neither.
- **The internal-path block decodes before it splits,** and cuts each segment at the first `;`. Without that, `/api/v1/internal%2Fsessions` and `/api/v1/internal;x/sessions` read as one segment that is not exactly `internal` and are forwarded. They 404 at the API today only because no route matches them — the API's routing being accidentally unhelpful to an attacker, not the block working, and 1.9 adds real routes under that prefix. Decoding first can only reveal a segment somebody tried to hide, never invent one.
- **`GatewayProblem` does not call `Response.Clear()`.** Clearing resets the whole header collection, discarding the security headers already written and any `Retry-After` the rate limiter just set. Only the status and body belong to it.
- **The gateway is the only resource with `WithExternalHttpEndpoints()`**, asserted on the AppHost model rather than left to review. It needs `WithHttpEndpoint()` first: `WithExternalHttpEndpoints` only promotes endpoints that already exist.

### Recorded limits and deferrals

- **`MaxRequestBodySize` is enforced by Kestrel, not by the in-memory test server.** YARP applies it through `IHttpMaxRequestBodySizeFeature`, which `TestServer` does not implement: a 256 KB + 1 body is forwarded with a 200 there. The configured value is asserted in `GatewayRouteTableTests` and the enforcement was checked against real Kestrel under `aspire run` (413, with the `request.payload_too_large` code and the security headers). There is deliberately **no** unit test asserting the refusal, because it would lock in the test server's behaviour as though it were the gateway's.
- **Only `/webhooks/square` has an explicit size limit.** Every other route falls back to Kestrel's 30 MB default. That is adequate while nothing accepts an upload; 2.3 (media) should set explicit limits on the routes that do.
- **`AllowedHosts` is `*`.** `AddXForwarded(Set)` copies the client's `Host` into `X-Forwarded-Host`, which the SSR site may use to build absolute URLs, so a forged `Host` is a path to poisoned reset links and cached responses. In run mode the host is `localhost` on a random port, so a strict list cannot be hard-coded here; **6.1 must set `AllowedHosts` for the deployed origin.** This is a deployment prerequisite, not a nice-to-have.
- **The rate-limit partition is the peer IP, truncated to /64 for IPv6.** A full IPv6 address would give one attacker a budget per address out of a typical /64 allocation. Behind Azure ingress with `TrustedProxies` still unset, every client shares one partition and one abuser can exhaust a policy for everybody — it fails closed, which is safe but is a denial of service. Another reason 6.1 owes `TrustedProxies`.
- **Compression and BREACH.** `UseResponseCompression` runs with `EnableForHttps = true`. When 1.10 adds `/bff/**` and antiforgery, the token must travel in a cookie or a header and **never in a compressed response body**, or the compression here becomes a BREACH oracle.
- **A destination's own response headers are appended, not replaced.** If the site or API ever sets its own CSP, the response carries two, and browsers enforce the intersection. That is safe but surprising, and the gateway is the place a CSP is meant to come from.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **YARP in a project (chosen)** | Full control of the pipeline, transforms and the signing key, which the token transform requires. More code than a container. |
| Aspire's `AddYarp` container resource | Less to write, but the route table and transforms move out of the repo and there is nowhere to hold the signing key. Rejected by ADR 0001. |
| Allow-list of forwarded headers (`RequestHeadersAllowed`) | Stronger default, but every legitimate header must be enumerated and a miss breaks a route silently. Later hardening. |
| `UseForwardedHeaders` always on, loopback trusted | One less branch, and it honours a spoofed `X-Forwarded-For` wherever there is no peer address. |
| Internal paths as a YARP route | Consistent with the rest of the table, but a route needs a cluster, and there is nothing to forward to. |

## Consequences

- `/` and `/admin` answer 502 until 2.2 and 3.3. That is expected, not a defect, and those prompts need no gateway change.
- `POST /webhooks/square` reaches the API and gets a 404 from it, because the webhook controller arrives in 5.6. The gateway's part — route, method restriction, size limit, rate limit — is in place and tested.
- Nothing is authenticated until 1.10, so every proxied request is anonymous and the API's `[AllowAnonymous]` routes are all that answer through the gateway. 1.10 adds the session and the signing branch starts firing with no change to the proxy.
- 1.10 also owes `/bff/**`, which it adds as gateway-own endpoints — they outrank the `site` catch-all by precedence — and the `CmsEditor` session gate on `/admin`.
- 6.1 owes `Gateway:TrustedProxies` for Container Apps' ingress. Until it is set, the gateway ignores forwarding headers entirely, which is correct for a laptop and wrong behind a real ingress — so this is a deployment prerequisite, not a nice-to-have.
- Adding a route means adding it to `appsettings.json` **and** to `GatewayRouteTableTests`, which is the point: the table is reviewed surface.
- The gateway duplicates three `ProblemDetails` fields and the internal-token constants. Both duplications are tested against the API's copies, and both are the price of the gateway holding no domain code.
