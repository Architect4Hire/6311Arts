# 0024 — BFF session tickets, and antiforgery at the edge

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/auth.md`, `.claude/rules/gateway.md`
- **Implements:** the browser half of [0002](0002-gateway-session-es256-internal-token.md) and the ticket store of [0009](0009-redis-for-session-tickets.md); consumes the credential routes of [0023](0023-identity-module-and-email-through-the-outbox.md) and the edge of [0022](0022-gateway-edge-policy-and-internal-token-transform.md)

## Context

Everything was in place except the person. The gateway could sign an internal token but nothing was ever
authenticated, so the signing branch never fired; the API had credential and revalidation routes with no caller;
Redis was referenced by a gateway that never wrote to it. ADR 0022 left three obligations here by name: the
`/bff/**` endpoints, the `CmsEditor` gate on `/admin`, and antiforgery that cannot become a BREACH oracle.

The forces: the browser may hold nothing but an opaque reference; a signed-out session must be dead immediately
rather than at the end of a lifetime; a password reset must reach sessions that are already signed in; staff
windows are short because there is no second factor (ADR 0018); and the three credential failure codes the API
distinguishes must collapse into one before a browser sees them (ADR 0023).

## Decision

### The ticket

- **A server-side ticket in Redis, keyed by the SHA-256 of the cookie's value.** The cookie carries 256 random
  bits; the store never holds that value. Whoever reads the store — a dump, a DbGate window, a support session —
  cannot turn what they see into a usable cookie. No salt and no stretching: the input is already full-entropy
  and uniformly random, so there is nothing to guess.
- **`sid` is not the cookie.** The ticket carries a separate, non-secret session id, and that is what goes into
  the internal token's `sid` claim and into log lines. The value that would resume the session never leaves the
  browser and the ticket store.
- **The idle window is the key's time to live; the absolute deadline is a field on the ticket.** Each request
  resets the TTL with one `EXPIRE`, so an abandoned session disappears without a sweep, and the TTL is capped at
  the absolute deadline so an expired ticket cannot sit there being read and rejected. Staff: 30 minutes idle,
  8 hours absolute (ADR 0018). Everyone else: **7 days idle, 30 days absolute** — the "1.10 defaults" `auth.md`
  deferred, chosen so a member is not signed out between visits while a staff session is still measured in
  minutes. All four are configuration.
- **The cookie is `__Host-6311.session`, with no `Expires` or `Max-Age`.** A browser-session cookie: closing the
  browser ends it, and the ticket is the authority on everything else. A persistent cookie would have to be
  rewritten on every response to track the sliding window and would leave a usable reference on disk.
- **A sign-in mints a new ticket and deletes any previous one**, so a value a thief holds cannot survive the
  real person signing in again.
- **A ticket is only ever rewritten conditionally** (Redis `SET ... XX`). An unconditional write races with a
  logout: a request that had read the ticket and gone away to revalidate it would recreate what the logout
  deleted, and a copied cookie would come back for a whole idle window. `EXPIRE` is safe already — it does not
  create keys — so only the post-revalidation write needed it.
- **The absolute deadline is recomputed when roles change**, and only ever brought forward:
  `min(existing, created + new absolute)`. A member promoted to editor mid-session would otherwise keep the
  30-day deadline they signed in with, straight through the 8-hour staff cap that stands in for 2FA.

### Reading it

- **One authentication scheme, `GatewaySession`**, reading the cookie, loading the ticket, checking the absolute
  deadline, revalidating the security stamp when it is due, refreshing roles from that answer and restarting the
  idle window. That is what makes `HttpContext.User` non-anonymous, which is what makes ADR 0022's token
  transform fire — the proxy was not touched.
- **Revalidation every 5 minutes, not every request.** Per request it would put an internal call in front of
  every page load. The interval is the window in which a password reset, a role change or "log out everywhere"
  has not yet reached a live session.
- **An unreachable API refuses the request but keeps the ticket.** We cannot say the session is still good, so
  the request fails closed; deleting the ticket would turn an API restart into signing everybody out, and the
  answer is likely "still valid" a second later.
- **A challenge is a 302 for an HTML navigation and a 401 otherwise**, decided from the method and `Accept`.
  Someone who typed `/admin` wants a login form; a background fetch wants a status code, not an HTML page in its
  JSON parser. A *forbidden* request is never redirected: sending a signed-in person to a login page says "your
  session failed" when what happened is "your account may not do this".

### The route gate

- **`/admin/**` and `/api/v1/cms/**` carry `"AuthorizationPolicy": "CmsEditor"` in the route table**, which is
  YARP's own mechanism, so the gate is in the reviewed configuration rather than in a path list in code. The CMS
  API needed a route of its own, more specific than `/api/**`; `GatewayRouteTableTests` asserts it stays more
  specific, because if that ever stopped being true the CMS surface would quietly become anonymous.
- **A route that requires a session must also be token-bearing**, asserted as an invariant rather than left to
  whoever adds the next one. Without it a CMS request would reach the API with no credential and be refused
  there, pointing nowhere near the cause.
- **The sign-in page is `/signin`, on the public site — not `/admin/login`.** `/admin/**` is gated, so a login
  page inside it would be redirected to itself forever, and its scripts and stylesheets would be refused too, so
  the form could not render even if the document were let through. This was found by a test following the
  redirect. `BrowserSessionOptions.Validate` now refuses any `LoginPath` under a gated prefix. **2.2 owes that
  page** (a staff sign-in form on the site, `noindex`), and it posts to `/bff/login` like anything else.

### Antiforgery

- **Two cookies, no response body.** The secret half is `__Host-6311.xsrf` (`HttpOnly`); the half script reads to
  populate `X-XSRF-TOKEN` is `__Host-6311.csrf`. Neither is ever written into a body, which is what discharges
  ADR 0022's BREACH note: the gateway compresses responses, and a secret in a compressed body beside
  attacker-controlled content is an oracle. **3.3 and 2.2 owe the Angular side**: `withXsrfConfiguration` with
  that cookie name and header name.
- **Validated for every unsafe method at the edge, with exactly one exemption** — `/webhooks/square`, which is
  signature-verified instead. The header only, never a form field, so the request body is not buffered and
  uploads still stream.
- **The check runs after authentication and before authorization, and both halves matter.** An antiforgery token
  is bound to the signed-in identity, so validating before the session is read compares it against an anonymous
  principal and refuses every token a signed-in person holds — a 400 on every write after sign-in, looking
  exactly like a CSRF bug. This was found by a test, having been reasoned the other way round first. Before
  authorization, so a missing token on a protected route is the 400 `auth.md`'s test list calls for.
- **`/bff/login`, `/bff/logout` and `/bff/logout-everywhere` re-issue the pair**, replacing `HttpContext.User`
  first, because the identity the token is bound to is exactly what those endpoints change.

### Failures are one kind of failure

Every transport fault from the internal API — a socket error, the resilience pipeline's own
`TimeoutRejectedException`, an open circuit — is converted to `IdentityApiException` at the client. Each caller
has exactly one question to answer, "was that a no, or no answer?", and a caller catching only
`HttpRequestException` would have turned an API outage into a 500 instead of the 503 it means, and a due
revalidation into an unhandled exception. A cancellation the browser caused is rethrown untouched.

### The Data Protection key ring

Antiforgery needs one, and the default is per-process and in memory in a container: every token would die with
the process and two replicas would disagree about half the time. The ring therefore lives in **Redis**, which the
gateway already depends on, rather than in the `dataprotection` blob container the API and Worker share — that
one is reached through the Domain's helper, and the gateway holds no domain code (ADR 0022). The application
name matches theirs, mirrored as a constant and compared by a test.

### Mirrored constants

The gateway now mirrors the role names and the two response shapes it reads, for the same reason it mirrors
`InternalTokenDefaults`. None of those drifts would fail a build — a renamed role would simply stop reaching
`/admin`, a renamed field would deserialize to null — so `SessionContractTests` holds each against its original.

### Recorded limits and deferrals

- **"Log out everywhere" is not instant for the other sessions.** They end at their next revalidation, so a
  stolen staff cookie keeps working for up to `RevalidationInterval` (5 minutes) after the victim runs the
  remedy. Accepted: closing it means a per-user index of ticket keys in Redis, which is a second thing to keep
  consistent with the tickets themselves and a write on every sign-in. If a shorter window is ever wanted, the
  cheaper lever is a shorter interval for staff. **The session that asks is deleted immediately**, so the person
  doing it is never left signed in.
- **The Data Protection key ring is not encrypted at rest.** There is no `ProtectKeysWith*`, so the antiforgery
  keys sit in plaintext under `6311:dataprotection:gateway-keys`, and Data Protection logs that it has no
  encryptor. The exposure is small — Redis takes a generated password, sessions are server-side, and forging an
  antiforgery token would still need the victim's session cookie — but in run mode DbGate shows the key to
  anyone with the dashboard. **6.1 owes an encryptor** (a certificate, or a Key Vault key), and should confirm
  the Redis eviction policy is `noeviction`: evicting the key ring or a ticket would sign people out silently.
- **The credential call is inside ServiceDefaults' standard resilience pipeline, which retries.** A timeout
  after the API has already counted a failed password would let one attempt cost two towards lockout. Lockout is
  5 attempts and `bff-login` allows 10 in five minutes, so the effect is a slightly earlier lockout during an
  outage, not a security hole. Left alone rather than special-casing one client's pipeline; worth revisiting if
  anyone reports mysterious lockouts.
- **The idle window is not extended while the API is unreachable**, because the request fails before the touch.
  A staff outage longer than 30 minutes therefore signs staff out anyway. That is the same answer the absolute
  limit would give and needs no separate handling.
- **`Gateway:TrustedProxies` is still empty** (ADR 0022). Behind Container Apps' ingress every client would share
  one rate-limit partition, which turns `bff-login`'s 10-per-5-minutes into a *global* sign-in budget anyone can
  exhaust. 6.1 must set it before go-live; this is now a sign-in availability dependency, not only a logging one.
- **The CMS gate at the edge is coarse and not the only lock.** The API's CMS controllers must still declare
  `[Authorize(Policy = CmsEditor)]`: the open `/api/**` route would carry a Member's user token through for any
  path that failed to match `/api/v1/cms/**`. The first CMS controller should be checked for this.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Server-side ticket in Redis (chosen)** | Revocation is immediate and the browser holds nothing. Redis is on the sign-in path: if it is down, sign-in is down (ADR 0009 accepts this). |
| An encrypted cookie holding the session | No Redis on the request path, and no way to revoke before expiry — which `auth.md` requires for logout, reset and "log out everywhere". |
| `IDistributedCache` with `SlidingExpiration` | Less code, and the sliding and absolute clocks would live inside Redis' own implementation where a fake clock cannot reach them — the idle and absolute limits could not be tested at all. |
| Validating antiforgery before authentication | Reads as "an edge check that does not depend on the session", and refuses every token a signed-in person holds. |
| Gating `/admin/**` with the login page inside it | One prefix to reason about, and an infinite redirect for anyone not signed in. |
| Data Protection ring in the shared blob container | One ring for the whole system, and a storage dependency plus domain code in the gateway for one purpose. |

## Consequences

- **`/signin` does not exist yet**, so an unauthenticated navigation to `/admin` redirects to a 502 until 2.2
  ships the page. That is the same "not built yet" state ADR 0022 recorded for `/` and `/admin`.
- **The gateway now has three Redis dependencies**: tickets, the Data Protection ring, and the health check that
  comes with `AddRedisClient`. Redis being unreachable makes the gateway unhealthy, which is what ADR 0009 says
  it should do.
- **`RedisSessionKeyValueStore` has no unit test.** It is four calls with no logic, `StackExchange.Redis` has no
  usable test double, and this repo writes fakes by hand. Everything above it — hashing, serialization, both
  clocks — is tested over an in-memory store with the same expiry semantics, and the Redis class itself is
  covered by running the system. The same split ADR 0022 recorded for YARP's request-size limit.
- **The antiforgery token binds to the subject, not to the whole claim set**, so a role change at revalidation
  does not invalidate the token the browser holds. That was established by a test rather than by reading, and
  the test stays: had it bound to every claim, each role change would have refused the next write with a 400 no
  client could anticipate.
- **Tests drive the gateway over `https://localhost`.** `__Host-` and `Secure` cookies are not sent over http by
  a `CookieContainer`, so an http base address would have made every session test silently anonymous.
- **1.12's end-to-end test can now sign in through the BFF**, which is what it says it does.
- **Still owed, and not in this prompt's scope:** `/bff/forgot-password`, and the confirm and reset pages the
  emailed links point at (ADR 0023 lists them against 1.10). They need Identity operations that do not exist yet
  — there is no `ResetPasswordAsync` or `ConfirmEmailAsync` on the facade — and browser pages that cannot exist
  before 2.2. They move to the prompt that adds the sign-in page.
