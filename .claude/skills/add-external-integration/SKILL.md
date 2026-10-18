---
name: add-external-integration
description: Add a non-Square provider adapter to 6311arts (email, newsletter vendor, WordPress import source) without leaking provider concerns into the domain.
---

# Add an External Integration

Square has its own skills (`add-square-sync`, `add-webhook-handler`). Use this for everything else.

1. **Read the provider's current first-party docs** and record the links in the plan: auth, rate limits, retries, idempotency, webhooks, deletion, terms.
2. **Declare an application-owned interface** in the Domain (for example `IEmailSender`, `INewsletterGateway`), and implement it in a provider folder or assembly. Provider DTOs never leave the adapter.
3. **Credentials** are Aspire secret parameters locally and Key Vault in Azure. They're never logged or returned.
4. **Client setup:** a typed client with timeout, safe-only retries, `Retry-After` handling and circuit breaking.
5. **Calls after commits go through the outbox.** Any provider call that follows a commit is an outbox handler, never inline in the request.
6. **Provider ids and cursors** go in integration records, never on core entities.
7. **Local substitute.** Every provider needs one so `aspire run` stays offline (Mailpit for email, a stub for a newsletter vendor).
8. **Define disconnect behaviour:** what happens to queued messages and stored references.

## Tests

- success
- auth failure
- 429 with `Retry-After`
- timeout
- malformed response
- retry after an ambiguous outcome
- secrets redacted

## Checklist before done

- [ ] Docs linked; interface in the Domain; adapter isolated
- [ ] Secrets as parameters; local substitute in the AppHost
- [ ] Post-commit calls via the outbox; tests pass; `@integration-reviewer` run
