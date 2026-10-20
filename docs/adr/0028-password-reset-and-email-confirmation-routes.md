# 0028 — Password reset and email confirmation: internal routes, BFF endpoints and the reset-request timing oracle

- **Status:** Accepted; amended by [0029](0029-emailed-link-pages-and-confirmation-resend.md) (the hand-off queue
  also carries confirmation requests; `Gateway:PasswordReset` is now `Gateway:AccountEmail`)
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/auth.md`, `.claude/rules/gateway.md`, `.claude/rules/api-contract.md`
- **Implements:** what [0023](0023-identity-module-and-email-through-the-outbox.md) deferred to 1.10 and
  [0024](0024-bff-session-tickets-and-edge-antiforgery.md) / [0027](0027-admin-shell-and-staff-sign-in-page.md)
  moved to 2.2a; the site pages are 2.2b

## Context

The reset and confirmation emails have gone out since 1.9, with the user id and token in the link's fragment, and
nothing answered them: `IIdentityFacade` could mint tokens but not use them, and the gateway had no route to ask
for a reset. ADR 0023 left three obligations here by name:

- `StaffPasswordRule` must be applied to Admin and Editor targets of a reset, because Identity's own floor is 8.
- `/bff/forgot-password` owes a rate limit and a per-address cooldown that behaves identically for known and
  unknown addresses.
- **The reset request is observable by timing.** A known address does an audit insert, an outbox insert and a
  save; an unknown one returns after the lookup. ADR 0023 said 1.10 "owns the route and its rate limit, and must
  decide there".

The forces are the usual ones for this surface: no enumeration by status, body, timing or cooldown; tokens only in
request bodies, never in a URL, a log line, a trace attribute or an outbox payload; and no Identity code in the
gateway.

## Decision

### The timing oracle: the gateway answers before the API is asked

`/bff/forgot-password` validates the shape, applies the cooldown, puts the address on a **bounded in-memory
queue**, and answers **202**. A gateway `BackgroundService` (`PasswordResetDispatcher`) drains the queue into the
API's internal route afterwards. Nothing that knows whether the address is registered runs before the browser has
its answer, so the answer's timing cannot depend on it — and that is testable deterministically: with the fake API
held forever, the 202 still arrives (`BffAccountTests.TheBrowserIsAnsweredBeforeTheApiIsAsked`).

- **At most once, from the gateway to the API.** A request still queued when the gateway stops is lost: the
  person was told "check your email", gets nothing, and asks again once the cooldown has passed. Accepted: the
  alternatives below either leak or grow tables.
- **A request that is dropped (full queue) or fails (sick API) gives its cooldown back**, so the person's own
  retry goes through rather than being silently dropped for the rest of the window. That is not an oracle: the
  browser's answer is 202 whatever the cooldown holds, so its state is never observable. Drops and failures are
  logged by exception type, never with the address; a metric for them is worth adding when the gateway grows its
  first meter.
- **Bounded:** `Gateway:PasswordReset:QueueCapacity` (256). A full queue drops the request and logs that it did,
  never the address. `TryWrite` on a `Wait`-mode channel, because `DropWrite` reports success for an item it threw
  away.
- **One trace still.** Each dispatch runs under a span parented on the request that queued it, from the gateway's
  own `ActivitySource` (the application name ServiceDefaults already exports), so gateway → API → outbox → Worker
  → Mailpit is one trace although the response went first. The span carries no address.
- **A failure is logged by exception type and dropped.** Nobody is waiting on the answer, and retrying would only
  hold addresses in memory longer while the API is sick.
- **An unreachable cooldown store is a 503**, not a 202: it is true for every address at once, so it says nothing
  about this one, and a 202 would promise an email nothing will send.

### The cooldown

- **Redis `SET NX`** on `6311:password-reset-cooldown:{SHA-256 of the normalized address}`, for
  `Gateway:PasswordReset:Cooldown` (5 minutes). The first request for an address in a window is queued; the rest
  get the same 202 and are dropped. One atomic command, so a race has one winner. It reuses the session store's
  seam (`ISessionKeyValueStore.AddIfAbsentAsync`) rather than adding a second Redis abstraction.
- **Identical for known and unknown addresses by construction:** the gateway never learns which an address is.
- **Normalized exactly as Identity looks addresses up** (`Normalize()` then `ToUpperInvariant()`, Identity's
  `UpperInvariantLookupNormalizer`), mirrored because the gateway holds no Identity code and pinned against the
  original by `SessionContractTests`. Every spelling that reaches one account shares one cooldown; a padded or
  otherwise different spelling reaches no account, so it has nothing to flood.
- **Anyone can spend a stranger's cooldown.** One request per window for a victim's address keeps their own
  requests dropped. Accepted: keying the cooldown per (address, client) instead would let many clients flood one
  mailbox, which is what the cooldown is for. The per-IP rate limit bounds how many addresses one client can hold.
- **Hashed, unsalted.** The store never holds a list of who asked. A dictionary attack against Redis by someone who
  already has candidate addresses would reveal "somebody asked for a reset for this address in the last five
  minutes", which is equally true of unknown addresses — the same limit ADR 0023 records for the email ledger.

### The rate limit

A new named policy, **`bff-account`** (10 per 5 minutes per client IP, the same numbers as `bff-login`), on all
three routes. A separate window so that a flood of reset requests cannot spend the sign-in budget; `bff-login` is
untouched. `Gateway:TrustedProxies` being empty (ADR 0024) applies here exactly as it does to sign-in: behind
Container Apps' ingress every client would share one partition until 6.1 sets it.

### Using a reset link

`IIdentityFacade.ResetPasswordAsync(PasswordResetCompletionViewModel)` — the `(userId, token, newPassword)` the
prompt named, in this module's ViewModel-plus-validator shape:

1. **The token is verified first.** Checked after the password policy, a too-short password would be refused with
   `auth.password_rejected` for staff and `auth.token_invalid` for a member, and a user id alone would reveal which
   the account is. So `auth.password_rejected` is only reachable with a valid token, and its reasons are the link
   holder's to see.
2. **`StaffPasswordRule` for Admin and Editor**, applied here because it needs the roles and so cannot be an
   `IPasswordValidator` (ADR 0023).
3. **`UserManager.ResetPasswordAsync`**, which verifies the token again, runs the 8-character floor and the bundled
   list for everyone, and **rotates the security stamp**. The rotation spends the link (Data Protection tokens
   embed the stamp) and ends every live session at its next revalidation (ADR 0024).
4. **Lockout is not cleared.** `auth.md` says lockout is never relaxed without an ADR, and this one does not relax
   it; a locked-out person who resets waits out the lockout window.
5. A second use that loses a race to the row (`ConcurrencyFailure`) is reported as a spent link, which it is. The
   repository reloads the entity first, so the refusal's audit save does not retry the losing write.

### Using a confirmation link

`IIdentityFacade.ConfirmEmailAsync(EmailConfirmationCompletionViewModel)`: the token, then **refuse an address
that is already confirmed**, then `UserManager.ConfirmEmailAsync`.

- **Confirmation does not rotate the stamp.** That narrows ADR 0023's "a used link rotates the security stamp",
  which is true of a reset and now deliberately not of a confirmation: rotating would sign out a member who
  confirms while signed in, for doing what they were asked. The token therefore stays verifiable for its hour, and
  the already-confirmed check is what makes the link work once.
- The check comes after the token, so only the link's holder learns that the address is already confirmed.

### One answer for an unusable link

`auth.token_invalid` — new — covers a wrong, expired or already-used token, an unknown user id, a token for another
user or purpose, and an address already confirmed. The API's audit row says which; neither the gateway nor the
browser is told. The gateway also collapses any other refusal code to it, so a future API code cannot become a
second answer by accident.

An unknown user id returns faster than a known one (no token unprotect, no audit write). That is not closed: the
id is a GUID that only ever arrives in the emailed link, so there is nothing to enumerate.

### The routes

| Route | In | Out |
|---|---|---|
| `POST /api/v1/internal/identity/password-reset` | `{ email }` | `202`, registered or not |
| `POST /api/v1/internal/identity/password-reset/complete` | `{ userId, token, newPassword }` | `204`, or `auth.token_invalid` / `auth.password_rejected` (+ `errors.newPassword`) |
| `POST /api/v1/internal/identity/email-confirmation/complete` | `{ userId, token }` | `204`, or `auth.token_invalid` |
| `POST /bff/forgot-password` | `{ email }` | `202`, no body |
| `POST /bff/reset-password` | `{ userId, token, newPassword }` | `204`, `auth.token_invalid`, or `auth.password_rejected` with reasons |
| `POST /bff/confirm-email` | `{ userId, token }` | `204` or `auth.token_invalid` |

The internal routes are `InternalService` only, excluded from the OpenAPI document and 404 from a browser. The
`/bff` routes carry antiforgery like every unsafe request, `no-store` from the group filter, and `bff-account`. An
unreachable API is a `503` (`auth.identity_unavailable`), never "this link has expired". Only a missing or
oversized field is a `400 validation.failed`, which does not depend on any account.

### Audit

`identity.password-reset-completed`, `identity.password-reset-refused`, `identity.email-confirmed` and
`identity.email-confirmation-refused`, each only for an account that exists — an unknown id has nothing to point
at, and writing one would let an unauthenticated caller grow the table. No token, password or address in a
summary. These are the same two-save shape as the credential path (Identity's store saves, then the audit row),
accepted for the same reason: nothing in these paths stages an outbox message (ADR 0023).

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Gateway answers first, dispatches in the background (chosen)** | Closes the oracle structurally and testably, persists nothing. At-most-once from gateway to API: a restart loses queued requests. |
| A fixed minimum response time | Simple, but the floor leaks once the known path is slower than it — under load, exactly when someone is probing — and a timing test would be flaky. |
| The same database work for unknown addresses | Closes the oracle at the cost ADR 0023 refused: unauthenticated growth in the audit and outbox tables. The payload could not carry the address anyway (outbox.md). |
| A cooldown in the API, keyed on the user | Cannot behave the same for an unknown address — there is no user — so it is the oracle ADR 0023 warned about. |
| Rotating the stamp on confirmation | Makes the token single-use by itself, and signs out a member who confirms while signed in. |
| Clearing lockout on a successful reset | Friendlier, and a relaxation of lockout that auth.md says needs its own ADR. Not this one. |
| The password policy before the token | Simpler to read, and tells someone holding only a user id whether the account is staff. |

## Consequences

- **The emailed links now have somewhere to land**, once 2.2b ships the site pages that read `location.hash`, clear
  it, and POST here.
- **`DataProtectorTokenProvider` reads the system clock**, not `TimeProvider` — `DataProtectionTokenProviderOptions`
  has only `Name` and `TokenLifespan` in .NET 10 (checked against the 10.0.13 reference assembly's documentation).
  Like lockout expiry, a fake clock cannot age a token, so the expired-link test uses a millisecond lifetime.
- **`IIdentityFacade.RequestPasswordResetAsync` takes a ViewModel now**, validated like the rest of the facade.
- **`outbox.md` names the gateway hand-off as the one sanctioned non-outbox queue**, and why it is not a
  precedent.
- **The secret-guard hook refuses `password = "…"` literals**, including in tests; test values are named constants.
- **The internal client retries POSTs** (ServiceDefaults' standard resilience pipeline, as ADR 0024 records for
  the credential call). A reset that succeeds but whose response times out is retried, refused as a spent link,
  and the person is told so although the new password works. Rare, and they can sign in; left alone rather than
  special-casing one client's pipeline, as 0024 did.
- **In Development a malformed `/bff` body is a 500**, because minimal APIs set `ThrowOnBadRequest` there and the
  exception handler maps the throw; elsewhere it is a 400. True of `/bff/login` already. A lone-surrogate address
  never reaches `Normalize()` — System.Text.Json refuses ill-formed UTF-16 while binding — and the endpoint's
  `ArgumentException` catch is the backstop if that ever changes.
- **Owed by 2.2b:** the three site pages, and a "Forgot your password?" link on `/signin`.
- **Owed by 6.1:** `Gateway:TrustedProxies` (as for `bff-login`); and running more than one gateway replica is
  fine for the cooldown (Redis) while each replica keeps its own queue. Also confirm the Redis instrumentation
  does not record keys in `db.statement` (`SetVerboseDatabaseStatements` is off by default): the cooldown key is
  an unsalted hash of an address, and telemetry is kept longer than a five-minute key.
- **Later hardening, not committed to:** a "your password was changed" email through the outbox, and treating a
  successful reset as proof of the mailbox (confirming the address). Both are reasonable; neither was asked for.
