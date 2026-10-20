# 0023 — Identity module, and email through the outbox

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/auth.md`, `.claude/rules/outbox.md`, `.claude/rules/external.md`, `.claude/rules/aspire.md`
- **Implements:** the credential store of [0002](0002-gateway-session-es256-internal-token.md) and the compensating controls of [0018](0018-no-two-factor-authentication.md); supplies the roles [0021](0021-api-host-conventions-and-internal-token-verification.md)'s policies read and the user [0022](0022-gateway-edge-policy-and-internal-token-transform.md)'s token signs for

## Context

The gateway signs internal tokens for a user that did not exist, and the API's `CmsEditor`, `CmsAdmin` and `Member` policies read a `role` claim nothing filled. ADR 0002 makes ASP.NET Core Identity in the API the only credential store; ADR 0018 removes 2FA and names what stands in for it — a confirmed email for staff, a ≥14-character staff password checked against a bundled list, lockout, and audited sign-ins. 1.10's BFF login needs all of it, and an account to log in with.

## Decision

### Identity's shape

- **One `DbContext`.** `ArtsDbContext` derives from `IdentityDbContext<ArtsUser, ArtsRole, string>`, so Identity's tables share the one context and one migration history. Naming a module's entity in the kernel is the single documented exception in `backend.md`, and `ModuleBoundaryScanner.KernelExemptions` already lists this file.
- **String keys, not `Guid`.** The kernel had already chosen: `AuditLog.ActorUserId` is a `string?` whose limit is documented as "the length of an ASP.NET Core Identity user id column", and ADR 0020 sizes idempotency subjects for `user:{id}` against the same assumption.
- **`AddIdentityCore`, not `AddIdentity`.** The latter adds cookie schemes, and the API takes no cookies — the gateway owns the browser session. There is no `SignInManager` either, which is why Business drives lockout itself.
- **Only the data-protector token provider,** not `AddDefaultTokenProviders()`: that would also register the email and phone token providers, whose purpose is issuing 2FA codes, and ADR 0018 says there is no 2FA for anyone.
- **`AddIdentityModule` returns the `IdentityBuilder`** so the *host* adds that provider. `DataProtectorTokenProvider` lives in the ASP.NET Core framework assembly, and referencing it from the Domain would put `HttpContext` and all of MVC within reach of the domain model. The Worker takes a `FrameworkReference` for it; the API has one already.
- **The password policy** is length plus the bundled list, with **no composition rules**: character classes push people towards predictable substitutions without adding entropy. Staff 14 (the one number `auth.md` fixes), everyone 8, lockout at 5 failures for 15 minutes — all in `IdentityPolicy`, so there is one place to change them.
- **The staff length rule is a plain rule, not an `IPasswordValidator`.** A validator runs inside `CreateAsync`, before any role is assigned, so it cannot tell staff from a member. Whoever creates the account knows which it is.

### The breached-password list

- **SecLists' `10k-most-common.txt`** (MIT), lower-cased, de-duplicated, sorted, gzipped to ~30 KB and **embedded** so no host has to copy it and no deployment can omit it — a silently missing list would switch the control off without failing anything, so loading it empty throws.
- **It applies to every password, not just staff.** Of its 10,000 entries **exactly one is 14 characters or longer**, so the staff minimum already rejects 9,999 of them on length alone. `auth.md` presents the list as a staff compensating control, where it is very nearly a no-op; it earns its place on **member** passwords, which have no such floor. A test pins both facts so the reasoning cannot go quietly stale.

### Email

- **`email.send-requested` is a kernel event** in `Managers/Outbox/Events/`, because many modules will write it and one handler delivers it. The handler resolves an `IEmailComposer` keyed by the message's kind, so the kernel never references a module and a new email type is a composer plus a registration.
- **The payload is `{ Kind, SubjectId }` — ids only.** No address and no token: `add-outbox-message` forbids both. The composer resolves the recipient and mints the Identity token at send time.
- **Idempotency keys on the outbox message id,** not `(kind, subjectId, recipient)` as `add-notification` suggests. Someone may legitimately request a second password reset, and keying on the subject would swallow it silently. A unique index on `OutboxMessageId` makes the database the arbiter, so two racing deliveries cannot both win.
- **Order is check → send → record, and that is at-least-once, not exactly-once.** A crash between SMTP accepting the message and the ledger write redelivers and sends twice. Recording first would instead lose the email whenever the send failed, which is worse: a duplicate reset is a nuisance, a missing one locks someone out. `outbox.md` already states at-least-once; the ledger narrows the window, it does not close it.
- **The ledger holds a hash, not an address** — enough to answer "did they get it?" in support without a second copy of everyone's email beside the user table.
- **SMTP lives in `Arts6311.Worker/Email/`.** Every email is an outbox message, so the Worker is the only process that ever sends, and MailKit stays out of the Domain, the API and the gateway.

### Internal routes

- `POST /api/v1/internal/sessions/credentials` and `/validate`, `InternalService` policy only, excluded from the OpenAPI document. The gateway also 404s `/api/*/internal/**` from a browser (ADR 0022), so there are two locks.
- **The failure codes distinguish a lockout from a bad password, and that is for the gateway, which is not a browser.** The gateway needs it to word its message and to audit. **1.10 must collapse them into one browser response** — `auth.md` records this as an obligation, because passing them through would restore the enumeration it forbids.
- **A missing account still costs a password hash.** Returning early would make "no such account" measurably faster than "wrong password", which is an enumeration oracle no amount of identical wording hides.

### Two accepted departures

- **The credential path makes two saves.** `UserManager.AccessFailedAsync` writes through its own store immediately; our audit row is a second save. That departs from `backend.md`'s one-`SaveChangesAsync` rule, and is accepted because nothing in that path stages an outbox message, so there is no commit-together guarantee to lose. `UserManager` sits at the **DataLayer**, as the adapter over Identity's store. The email paths stage their message and audit row in exactly one save and make no Identity write at all — asserted by giving those tests a `UserManager` over an empty database.
- **The seeded Admin is created with its email already confirmed.** `auth.md` requires staff to confirm before reaching `/admin`, and this is the documented exception: the confirmation link points at a browser route that does not exist until 1.10, so an unconfirmed seed would lock the only account out of the system it exists to administer. An audit row records that it was seeded this way. A rerun never resets an existing admin's password — that would hand the account back to whoever knows the parameter, after someone had changed it.

### A shared Data Protection key ring

Identity's confirmation and reset tokens are Data-Protection-protected, and the two halves of that flow live in different processes: the **Worker** mints the token when it sends the email, the **API** validates it when the link is opened. Per-process key rings — the default — would fail every emailed link, and fail intermittently as processes restarted. The token cannot be passed between them any other way, since an outbox payload may not carry one. So both hosts persist keys to the `dataprotection` blob container under a **fixed application name**, because the default is derived from the entry assembly and would isolate the rings even with shared storage. This is what that container, declared back in 1.2, was for.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Identity on `ArtsDbContext` (chosen)** | One context, one migration history, joins to domain tables possible. Identity's unused 2FA and passkey columns sit in the schema. |
| A separate Identity `DbContext` | Cleaner separation, and two migration histories plus no transaction across them — which `backend.md` forbids outright. |
| `(kind, subject, recipient)` email idempotency | Matches `add-notification`, and silently swallows a second legitimate reset request. |
| Record-then-send | Closes the duplicate window, and loses the email on any send failure. |
| Per-process Data Protection keys | Nothing to configure, and every emailed link fails. |
| Token in the outbox payload | Avoids the shared key ring, and puts a credential in a row every Admin can read. Forbidden by `add-outbox-message`. |
| Breached list for staff only | Matches `auth.md`'s wording, and is a control in name: one entry in 10,000 clears the staff length floor. |

### Recorded limits and deferrals

Found by review, fixed or deliberately deferred:

- **Fixed: a locked account was faster than an unknown one.** The locked path returned before any hash, so five
  bad guesses at any address revealed whether it existed — and would have kept revealing it after 1.10 collapsed
  the codes. It now spends the same dummy hash as the other two paths.
- **Fixed: parallel wrong guesses outran the lockout counter.** Identity increments it as a read-modify-write
  guarded by the concurrency stamp, so simultaneous guesses collided, some increments were lost, and the loser
  surfaced as a 500. The increment now retries on conflict. The gateway's `bff-login` rate limit remains the real
  backstop and is **not optional**.
- **Fixed: the token travelled in the query string.** The gateway's OpenTelemetry instrumentation records
  `url.query` and redacts only a few well-known key names, so a reset token would have landed in the Aspire
  dashboard and in whatever collects traces in Azure, as well as in `Referer`. It is now in the **fragment**,
  which a browser never sends. **1.10's confirm and reset pages must read `location.hash` and POST the token.**
- **Fixed: tokens lived 24 hours.** The framework default. Now one hour, so several outstanding links do not pile
  up. A used link rotates the security stamp, which invalidates the rest.
- **Fixed: the seeder re-asserted a known Admin credential.** Keyed on the configured address, so changing
  `seed-admin-email` or renaming the admin recreated an Admin with the parameter's password on the next migration
  run. Now keyed on whether *any* Admin exists.
- **Fixed: permanent SMTP refusals burned five attempts.** An unparseable address or a 5xx refusal is now
  `EmailPermanentlyUndeliverableException`, poisoned on attempt 1 with its audit row.
- **Fixed: no last-attempt visibility.** `outbox.md` requires a handler that calls a provider to record something
  a person can see. It now writes an `email.delivery-failed` audit row on the final attempt — and deliberately
  **no** ledger row, so an Admin's replay is a real attempt rather than a silent no-op.
- **Fixed: unwrapped error paths could leak.** The ledger read and the record save were outside any wrapper, so a
  `SqlException` naming the server could reach `LastError`. Both are wrapped now. The send failure is also logged
  with its exception *type* and attempt number, which the original code did not do at all — the SMTP bug below
  cost five silent attempts partly because of that.
- **Deferred to 1.10 (decided in [0028](0028-password-reset-and-email-confirmation-routes.md)): a password-reset
  request is observable by timing.** An unknown address returns after the
  lookup; a known one performs an audit insert, an outbox insert and a save first. Equalising it means staging
  work for addresses we do not hold, which trades an oracle for unauthenticated growth in two tables. 1.10 owns
  the route and its rate limit, and must decide there. It also owes a **per-user cooldown**: nothing at this
  layer stops one address being mail-bombed, and any cooldown must behave identically for known and unknown
  addresses or it becomes the oracle itself.
- **Deferred to 6.1: the key ring is stored unencrypted.** `PersistKeysToAzureBlobStorage` with no
  `ProtectKeysWith…`, so `keys.xml` sits in the container in plaintext and anyone who can read it can derive the
  token protector (a forged token still needs the target's security stamp). 6.1 must add
  `ProtectKeysWithAzureKeyVault` and confirm the container has no public access.
- **Deferred to 6.1: email links are built from the gateway's HTTP endpoint** in run mode, and `EmailOptions`
  only requires an absolute URL. A reset token must never be mailed in an `http://` link outside Development.
- **Known: the ledger protects the ledger, not the mailbox.** Two overlapping runs of one message — a lease
  expiring under a slow relay — both see no row and both send. The SMTP timeout is set well under the lease to
  make that unlikely, but at-least-once is the contract.
- **Known: `RecipientHash` is an unsalted SHA-256** and so reversible by dictionary against any address somebody
  already has. It is a support correlation aid, not a privacy measure, and the comment says so.
- **Known: unknown-address sign-in attempts each write an audit row.** An unauthenticated caller can grow
  `AuditLog`; 1.10's login rate limit is the control.
- **Known: `auth.*` codes map to 400.** None is in `ApiProblemDetails`' status map, so `credentials_invalid`,
  `locked_out` and `email_unconfirmed` all return 400 with the code in the body. **The gateway must branch on
  `code`, not on status.**
- **Known: `StaffPasswordRule` has one caller** (two since [0028](0028-password-reset-and-email-confirmation-routes.md)'s reset). The seeder. 1.10's reset flow and any later user-management
  route must call it for Admin and Editor targets, because Identity's own floor is 8 — a staff password reset
  could otherwise set eight characters.

## Consequences

- **A `SmtpOptions` lesson, recorded because it cost real attempts:** MailPit supplies `Endpoint=smtp://host:port`, the keyed form, while its own XML documentation describes a bare `smtp://host:port`. The first parser accepted only the latter, and the result was five poisoned delivery attempts whose `LastError` — correctly sanitised to an exception type — said nothing about why. The parser now accepts both shapes, `ValidateOnStart` makes a bad value a startup failure instead of a poisoned message, and `SmtpOptionsTests` covers it. **Sanitising an error and diagnosing it are in tension; a startup check is how to have both.**
- **Lockout uses the system clock, not `IClock`.** `UserManager.IsLockedOutAsync` compares `LockoutEnd` against `DateTimeOffset.UtcNow`, so a fake clock cannot move it — which also means lockout *expiry* cannot be tested by advancing one, and there is deliberately no test for it.
- Adding a user-management screen (later) must call `StaffPasswordRule` for staff accounts; the rule exists and is tested, with the seeder as its only caller today.
- 1.9 adds **no route that triggers a reset**: `IIdentityFacade.RequestPasswordResetAsync` exists, succeeds whether or not the address is registered, and waits for 1.10's `/bff/forgot-password`. 1.10 also owes the browser-facing confirm and reset pages the emailed links point at.
- `dotnet ef` now goes through `ArtsDbContextDesignTimeFactory` rather than starting the API host, which refuses to start without its real configuration (ADR 0021). The tool gets its own way in rather than the host getting looser.
- API tests replace the key ring with an ephemeral one, so no test depends on Azurite. The blob-backed ring is verified under `aspire run`.
