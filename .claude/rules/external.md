---
paths:
  - src/Arts6311.Square/**
  - src/Arts6311.Domain/**/Gateways/**
  - src/Arts6311.Importer/**
---
# External Integrations

External systems are replaceable adapters behind application-owned interfaces. Square has its own deeper rule (`square.md`); this one applies to every provider. That includes Square, email (Mailpit / Azure Communication Services), the future newsletter vendor and the WordPress importer source.

## Gateways

- **One interface per provider boundary,** declared in the Domain and implemented in a provider assembly or folder. Provider DTOs are mapped immediately and never reach Business or ServiceModels.
- **Typed `HttpClient` or SDK client** with timeouts and circuit breaking. Retries are only for operations documented as safe, and honour `Retry-After` and rate-limit headers.
- **Provider ids, cursors and sync status** live in integration records, never on core entities.
- **A provider outage never blocks the site or CMS reads.** Writes queue in the outbox and drain when the provider returns.

## Secrets

- **Credentials** (API tokens, webhook signature keys, SMTP / Azure Communication Services keys) are Aspire secret parameters locally and Key Vault references in Azure.
- **Never put a credential** in source, config files, logs, `ProblemDetails`, outbox payloads or `LastError`.

## Webhooks

1. Verify the signature before parsing.
2. Persist the provider event id with a unique index.
3. Stage an outbox message.
4. Return 200.

Process it in the Worker, idempotently.

## Imports

Imports create traceable source records, are idempotent on the source id, and never silently overwrite staff edits.

## Tests

- success
- auth failure
- rate limit (429 + `Retry-After`)
- timeout
- malformed response
- duplicate webhook
- retry after an ambiguous outcome
- secret redaction in logs and errors
