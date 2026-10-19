# 0018 — No two-factor authentication

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/auth.md`, `.claude/agents/security-reviewer.md`
- **Amends:** ADR 0002 (removes "TOTP 2FA required for Admin and Editor before `/admin`")

## Context

ADR 0002 required TOTP 2FA for Admin and Editor accounts before they reach `/admin`, with recovery codes and a second BFF login step (prompt 1.11). The product owner doesn't want 2FA for anyone: not staff, not members. Nothing implementing it had been built yet.

Without a second factor, a staff password is the only thing standing between an attacker and the CMS. An Admin can delete content, replay or discard outbox messages, adjust seats and manage users. The controls below exist to make that single factor as hard to abuse as possible without adding a step for users.

## Decision

- **No 2FA of any kind** (TOTP, SMS, email codes, passkeys as a second factor) for Admin, Editor or Member. No recovery codes. `/bff/login` is a single step.
- **Compensating controls, all required:**
  - **Staff password policy.** Admin and Editor passwords are at least 14 characters and are checked against a bundled list of common and breached passwords. The check is offline: no network call, so run mode stays offline.
  - **Lockout, confirmed email and rate limiting stay.** Identity lockout on failed attempts, a confirmed email before `/admin`, and the gateway's rate limit on `/bff/login`.
  - **Shorter staff sessions.** Sessions carrying `Admin` or `Editor` expire after 30 minutes idle and 8 hours absolute. Member sessions keep the defaults set in prompt 1.10. Both values are configuration, not literals.
  - **"Log out everywhere".** Rotates the security stamp; every other session ends at its next stamp revalidation. Moved from prompt 1.11 into 1.10.
  - **Audit.** Staff sign-ins (success and failure) are written to the audit log (`IAuditWriter`), along with the sensitive operations auth.md already lists.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **No 2FA plus compensating controls (chosen)** | Nothing extra for users to do, and less to build (prompt 1.11 dropped). A phished or reused staff password gives full CMS access until lockout, stamp rotation or session expiry stops it. |
| TOTP for staff only (ADR 0002 as written) | Strong protection against stolen passwords. The product owner rejected it: enrollment, recovery codes and a second login step for a small nonprofit team. |
| Passkeys / WebAuthn as the only staff credential | Phishing-resistant and has no second step, but needs device support and a recovery story. Not requested; would need a new ADR. |
| Email one-time code for staff | Weaker than TOTP and depends on mailbox security. Still a second step, which is what was rejected. |

## Consequences

- **Prompt changes.** Prompt 1.11 is removed (its slot is kept so later numbers don't change). 1.10 gains "log out everywhere" and the staff session lifetimes. 1.9 gains the staff password policy. 1.12 and 2.2 drop their 2FA steps.
- **Reviewers stop checking for TOTP** and check the controls above instead.
- **Must not:** relax lockout, the login rate limit, the confirmed-email requirement or the staff session lifetimes without a new ADR. Without 2FA, those are the protection.
- **Later hardening, not committed to:** a "new sign-in to your account" email for staff, sent through the outbox (add-notification). Reintroducing 2FA, or adopting passkeys, needs a new ADR that supersedes this one.
