---
paths:
  - src/Arts6311.Gateway/**
  - src/Arts6311.Api/**
  - src/Arts6311.Domain/Modules/Identity/**
  - src/Arts6311.Domain/Modules/Members/**
---
# Authentication and Authorization

6311arts is web-first. The browser talks only to `Arts6311.Gateway` and holds only a session cookie. The gateway-to-API trust model is creatorpantry's B-13, adopted as-is.

## Identity

- ASP.NET Core Identity in the API is the only credential store. It owns users, password hashing, lockout, email confirmation, recovery and security stamps.
- Roles:
  - `Admin` — everything, including deletes, sync replay and user management
  - `Editor` — CMS content
  - `Member` — the member area on the public site
- **No 2FA for anyone** (ADR 0018): no TOTP, SMS, email codes or recovery codes, and login is a single step. The password is the only factor, so these controls are mandatory:
  - Admin and Editor accounts require a confirmed email before reaching `/admin`.
  - Staff passwords are at least 14 characters; everyone's minimum is 8, with **no composition rules** (length plus the list, not character classes). The numbers live in `IdentityPolicy`.
  - Every password, member as well as staff, is checked against the bundled common/breached-password list (offline, no network call). **It is applied to everyone deliberately:** only one of the list's 10,000 entries reaches 14 characters, so for staff the length rule already does the work and the list earns its place on member passwords (ADR 0023).
  - Identity lockout and the `/bff/login` rate limit are never relaxed without a new ADR.
  - Staff sign-ins, successful and failed, are audited through `IAuditWriter`.
- The first Admin is seeded by MigrationService from secret AppHost parameters, **with its email pre-confirmed** — the documented exception to the rule above, because the confirmation link points at a browser route that does not exist until 1.10 (ADR 0023). A rerun never resets an existing admin's password. A seed password that fails the staff rule stops the migration run.
- There's no OAuth/OIDC server, no OpenIddict and no refresh tokens. Adding any of them needs a new ADR.

## Browser session (the gateway is the BFF)

- **Sign-in:** the gateway's `/bff/login` calls the API's internal credential check. On success it creates a server-side session ticket in Redis.
  - The cookie is `__Host-6311.session`: `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`. It carries only the ticket reference.
- **Other BFF endpoints:** `/bff/session` (display name and roles, never ids or tokens), `/bff/logout`,
  `/bff/logout-everywhere`, `/bff/antiforgery`, and the emailed-link routes `/bff/forgot-password`,
  `/bff/resend-confirmation`, `/bff/reset-password`, `/bff/confirm-email` (below, ADR 0028, 0029).
- **The ticket.** Redis holds it, keyed by the **SHA-256 of the cookie's value**, so the store never holds a
  usable cookie. The `sid` claim is a separate non-secret id, not the cookie. The idle window is the key's time
  to live, capped at the absolute deadline on the ticket (ADR 0024).
- **The internal routes the BFF calls** (`InternalService` token only, 404 from a browser):

  | Route | In | Out |
  |---|---|---|
  | `POST /api/v1/internal/sessions/credentials` | `{ email, password }` | `{ userId, displayName, roles, securityStamp }`, or `auth.credentials_invalid` / `auth.locked_out` / `auth.email_unconfirmed` |
  | `POST /api/v1/internal/sessions/validate` | `{ userId, securityStamp }` | `{ valid, roles }` |
  | `POST /api/v1/internal/sessions/revoke` | `{ userId }` | `204`, or `resource.not_found`. Rotates the security stamp: "log out everywhere" |
  | `POST /api/v1/internal/identity/password-reset` | `{ email }` | `202`, registered or not |
  | `POST /api/v1/internal/identity/email-confirmation` | `{ email }` | `202`, registered, unknown or already confirmed |
  | `POST /api/v1/internal/identity/password-reset/complete` | `{ userId, token, newPassword }` | `204`, or `auth.token_invalid` / `auth.password_rejected` (+ `errors.newPassword`). Rotates the stamp |
  | `POST /api/v1/internal/identity/email-confirmation/complete` | `{ userId, token }` | `204`, or `auth.token_invalid`. Does not rotate the stamp |

  Emailed confirmation and reset links carry the token in the URL **fragment**, not the query string, so it never
  reaches a server log or a trace. The site's `main.ts` takes it out of the address bar before Angular starts, and
  the pages POST it (ADR 0029). Tokens last one hour, and travel only in request bodies: never a URL, a log line, a
  trace attribute or an outbox payload.

  **The three credential failure codes are for the gateway and must not reach a browser.** The gateway needs them to word its message and to audit; a browser given them can enumerate accounts. `/bff/login` collapses them into one response (ADR 0023). The API itself never distinguishes an unknown address from a wrong password, and spends a password hash either way so the two cannot be told apart by timing.
- **Emailed links (ADR 0028, 0029):**
  - `/bff/forgot-password` and `/bff/resend-confirmation` are **always 202**, and answer **before the API is
    asked**: they apply the cooldown, queue the address in memory, and a gateway background dispatcher calls the
    internal route afterwards. That is what stops the response's timing from saying whether an address is
    registered (or, for a confirmation, already confirmed). Never make either await the API.
  - The **cooldown** is a Redis `SET NX` on the SHA-256 of the address as Identity normalizes it, one key per kind
    (`Gateway:AccountEmail:Cooldown`, 5 minutes). The gateway cannot tell a known address from an unknown one,
    so it behaves identically for both.
  - All four routes carry antiforgery and the `bff-account` rate limit, separate from `bff-login`.
  - A reset checks the **token before the password policy**, so `auth.password_rejected` is only reachable with a
    valid link. Then `StaffPasswordRule` for Admin and Editor, then Identity's floor and the bundled list for
    everyone. Success rotates the stamp: the link is spent and every session ends at revalidation. Lockout is not
    cleared.
  - A confirmation works once because an already-confirmed address is refused; it does **not** rotate the stamp,
    so a member confirming while signed in stays signed in.
  - Every unusable link is one `auth.token_invalid`. Only a missing or oversized field is `validation.failed`; an
    unreachable API is a 503, never "this link has expired".
- **Logout:** deletes the ticket and clears the cookie, so a copied cookie dies with it. `/bff/logout-everywhere`
  calls the internal revoke route first, then ends this session; the others end at their next revalidation —
  **up to `RevalidationInterval` later**, which ADR 0024 records as accepted.
- **Lifetimes:** sessions carrying `Admin` or `Editor` expire after 30 minutes idle and 8 hours absolute (ADR 0018). Members get 7 days idle and 30 days absolute. A session holding both a staff role and `Member` gets the staff limits. All values are configuration (`Gateway:Session`).
- **Role changes move the absolute deadline forward, never back.** A member promoted to staff comes under the
  staff limits at the next revalidation.
- **Revalidation:** the gateway revalidates the security stamp every `RevalidationInterval` (5 minutes), so a password reset, a role change or a revocation reaches a live session. An **unreachable API refuses the request but keeps the ticket** — failing closed without signing everybody out over a restart.
- **The sign-in page is `/signin`, on the public site, and must never be behind the session gate.** A login page under `/admin/**` redirects to itself forever and cannot load its own scripts (ADR 0024). The gateway refuses a `LoginPath` under a gated prefix at startup.
- **CSRF:** every unsafe request needs antiforgery (the `X-XSRF-TOKEN` header). The exceptions are listed explicitly: `/webhooks/square`, which is signature-verified instead.
  - Two cookies: `__Host-6311.xsrf` (the secret, `HttpOnly`) and `__Host-6311.csrf` (what script reads). **The token is never written into a response body** — the gateway compresses, and that would be a BREACH oracle.
  - **The token is bound to the signed-in identity.** Validation runs after authentication and before authorization, and `/bff/login`, `/bff/logout` and `/bff/logout-everywhere` re-issue the pair. Either omission refuses every write a signed-in person makes.
- **Token storage:** tokens never appear in localStorage, sessionStorage, IndexedDB, URLs or logs.
  - **The one exception is the preview link** (ADR 0036), whose token is in the path because the site's server
    reads it while rendering and cannot be handed a fragment. It is a capability URL: 30 minutes, one read of one
    record, no write path accepts it, and `ServiceDefaults.RedactSecretSegments` replaces the segment after
    `/preview/` in every exported span. Another token in a URL needs its own ADR.

## Gateway → API trust

- **The gateway strips client headers.** It removes client-supplied `Authorization`, `Cookie` and forwarding/identity headers, then attaches a **gateway-signed internal token** with these claims:

  | Claim | Value |
  |---|---|
  | Algorithm | ES256 |
  | `iss` | `6311-gateway` |
  | `aud` | `6311-api` |
  | `sub` | user id |
  | `sid` | session id |
  | `role` | user's roles |
  | Lifetime | ≤ 2 minutes |
  | `token_use` | `user` or `service` |

- **Keys.** The private key exists only in the gateway (a generated, persisted secret parameter locally; Key Vault in Azure). The API holds only the public key and accepts no other authentication scheme.
- **Token use.**
  - Product routes accept only `token_use=user`.
  - `/api/v1/internal/**` accepts only `token_use=service`. The gateway returns 404 for those paths from browsers.
- **Secure by default.** Every API action requires an authenticated user. Public reads (site content, quotes, forms) opt out explicitly with `[AllowAnonymous]`.

## Authorization

- Route policies: `CmsEditor` (Admin or Editor), `CmsAdmin` (Admin), `Member`.
- Data-dependent rules live in Business. Example: a member reads only their own subscription.
- **Sensitive operations need an explicit policy and an audit row:**
  - deleting, restoring or archiving content (ADR 0052: the delete, the restore and the Deleted view are all `CmsAdmin`)
  - adjusting seats
  - replaying or discarding a poisoned outbox message
  - user and role changes
  - secret rotation
  - redirect changes

## Tests

- Login and lockout. (Lockout *expiry* is not covered: `UserManager` compares `LockoutEnd` against the system clock, not `IClock`, so a fake clock cannot move it.)
- A short or listed staff password is refused, at reset as well as at seeding.
- A reset link works once and ends other sessions at revalidation; an expired or reused link is refused; a
  confirmation link works once.
- `/bff/forgot-password` and `/bff/resend-confirmation` answer before the API does, and their cooldowns treat
  every address the same.
- The site's link pages never put the token in the page, browser storage or the console.
- A staff session ends at its idle and absolute limits, and a member session uses the longer defaults.
- "Log out everywhere" ends every other session.
- A rotated stamp ends a live session at the next revalidation, and an unreachable API does not.
- The three credential failure codes reach the browser as one answer.
- Every role-gated route gives a 403 for the wrong role.
- An internal route is unreachable from the browser.
- An expired or wrongly scoped internal token gets a 401.
- A missing antiforgery token gets a 400.
- Logout invalidates a copied cookie.
