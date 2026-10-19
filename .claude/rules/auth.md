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
- **Other BFF endpoints:** `/bff/session` (display name and roles, never ids or tokens), `/bff/logout`, `/bff/antiforgery`.
- **The internal routes the BFF calls** (`InternalService` token only, 404 from a browser):

  | Route | In | Out |
  |---|---|---|
  | `POST /api/v1/internal/sessions/credentials` | `{ email, password }` | `{ userId, displayName, roles, securityStamp }`, or `auth.credentials_invalid` / `auth.locked_out` / `auth.email_unconfirmed` |
  | `POST /api/v1/internal/sessions/validate` | `{ userId, securityStamp }` | `{ valid, roles }` |

  Emailed confirmation and reset links carry the token in the URL **fragment**, not the query string, so it never
  reaches a server log or a trace — `/bff` pages must read `location.hash` and POST it. Tokens last one hour. A
  reset request answers the same whether or not the address is registered, and `/bff/forgot-password` owes both a
  rate limit and a **per-user cooldown that behaves identically for known and unknown addresses**.

  **Those three failure codes are for the gateway and must not reach a browser.** The gateway needs them to word its message and to audit; a browser given them can enumerate accounts. `/bff/login` collapses them into one response (ADR 0023). The API itself never distinguishes an unknown address from a wrong password, and spends a password hash either way so the two cannot be told apart by timing.
- **Logout:** deletes the ticket and clears the cookie. "Log out everywhere" rotates the security stamp.
- **Lifetimes:** sessions carrying `Admin` or `Editor` expire after 30 minutes idle and 8 hours absolute (ADR 0018). Member sessions use the 1.10 defaults. All values are configuration.
- **Revalidation:** the gateway periodically revalidates the security stamp, so a password reset ends sessions.
- **CSRF:** every unsafe request needs antiforgery (the `X-XSRF-TOKEN` header). The exceptions are listed explicitly: `/webhooks/square`, which is signature-verified instead.
- **Token storage:** tokens never appear in localStorage, sessionStorage, IndexedDB, URLs or logs.

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
  - deleting or archiving content
  - adjusting seats
  - replaying or discarding a poisoned outbox message
  - user and role changes
  - secret rotation
  - redirect changes

## Tests

- Login and lockout. (Lockout *expiry* is not covered: `UserManager` compares `LockoutEnd` against the system clock, not `IClock`, so a fake clock cannot move it.)
- A short or listed staff password is refused.
- A staff session ends at its idle and absolute limits.
- "Log out everywhere" ends every other session.
- Every role-gated route gives a 403 for the wrong role.
- An internal route is unreachable from the browser.
- An expired or wrongly scoped internal token gets a 401.
- A missing antiforgery token gets a 400.
- Logout invalidates a copied cookie.
