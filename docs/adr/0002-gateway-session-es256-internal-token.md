# 0002 — Gateway session + ES256 internal token (no OIDC)

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/auth.md`, `.claude/rules/gateway.md`, `.claude/rules/aspire.md` (signing-key parameter)
- **Adopts:** creatorpantry **B-02** (BFF authentication) and **B-13** (gateway-to-API trust, no OIDC server), as-is apart from the 6311 differences listed below

## Context

Staff (Admin, Editor) sign in to the CMS, and members sign in to the public site. ASP.NET Core Identity in the API is the credential store. Tokens must never reach browser storage, URLs or logs. Sessions must be revocable: logout, a password reset and "log out everywhere" must end a copied cookie. The API must trust exactly one caller, the gateway (ADR 0001).

## Decision

- **Browser ↔ gateway.**
  - The gateway's `/bff/login` calls an internal API credential check (Identity, lockout, confirmed email). On success it creates a **server-side session ticket in Redis** (ADR 0009).
  - The browser gets only `__Host-6311.session` (`HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`), which holds just the ticket reference. Every unsafe request needs antiforgery; the signature-verified webhook is the only exception.
- **Gateway → API.**
  - The gateway strips client `Authorization`, `Cookie` and forwarding headers, and attaches a **gateway-signed ES256 JWT** (`iss` `6311-gateway`, `aud` `6311-api`, `sub`, `sid`, `role`, `token_use` `user`|`service`, lifetime ≤ 2 minutes).
  - The API holds only the public key and accepts no other scheme. `/api/v1/internal/**` accepts only `token_use=service`.
- **No OAuth/OIDC server, no OpenIddict, no refresh tokens.** The gateway revalidates the security stamp periodically.

**6311 differences from B-13:** issuer and audience names; a `role` claim; three roles (`Admin`, `Editor`, `Member`); TOTP 2FA required for Admin and Editor before `/admin`.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Gateway session + internal token (chosen)** | Revocable sessions. The API trusts one scheme. No key ring shared across hosts. Redis becomes a hard dependency for sign-in. |
| Identity cookie issued by the API, Data Protection key ring shared with the gateway (v1 plan) | Fewer parts, but a shared key ring across hosts and a cookie the gateway can't revoke centrally. |
| OpenIddict in the API, gateway as confidential client (code + PKCE, server-side refresh tokens) | Standards-based and ready for mobile or third parties, but an authorization server to run and secure. Rejected in B-13 for fewer moving parts. |
| Encrypted cookie-only ticket (no server store) | No Redis, but it can't be revoked server-side and breaks the logout rule (rejected in B-02). |

## Consequences

- Redis unavailability means sign-in is unavailable. Health checks must show it.
- The signing key is a generated, persisted secret parameter locally and a Key Vault secret in Azure. Rotation by `kid` is later hardening.
- A native app or public third-party API would need a new ADR, and probably an authorization server. Both are out of scope (CLAUDE.md).
