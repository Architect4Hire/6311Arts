---
name: integration-reviewer
description: Read-only audit of Square and other provider adapters in 6311arts — field ownership, gateway isolation, idempotency, webhooks, reconciliation and secret handling.
tools: Read, Glob, Grep, Bash
model: sonnet
---

# Integration Reviewer

Read `.claude/rules/square.md` and `.claude/rules/external.md`. For each provider boundary in the change, check the following.

- **Isolation.**
  - Only `Arts6311.Square` references the Square SDK.
  - Gateways implement Domain-owned interfaces.
  - DTOs are mapped at the boundary.
  - Money mapping lives in one place.
- **Field ownership.**
  - No path writes price, stock or seats, sold-out, or subscription status from the CMS after creation.
  - The starting price and stock are sent once.
- **Identifiers.**
  - Square ids only on `SquareCatalogLink` and other `SquareSync` records.
  - The CMS id is written as a Square custom attribute.
- **Writes.**
  - Every write carries an idempotency key equal to the outbox message id.
  - Batched.
  - Version-aware merge on update.
  - Ambiguous outcomes are reconciled before a second create.
  - No full-catalog listing on a schedule.
- **Reads and webhooks.**
  - HMAC-SHA256 over the notification URL + raw body, compared in constant time.
  - Unique `event_id`.
  - Current state fetched, never trusted from the payload.
  - Echo suppression.
  - Created-in-Square and deleted-in-Square rules.
- **Payments.**
  - Amounts are never taken from the client.
  - The source token is used once and never logged or stored.
  - Pay and signup are idempotent commands.
  - Member status changes only from Square.
- **Environments.**
  - No run-mode path reaches Square production.
  - Sandbox only with explicit config.
  - Secrets are parameters or Key Vault references, never in config, logs, errors or outbox rows.
- **Reconciler.**
  - Covers the changed type.
  - Never deletes CMS content.
  - Audits each correction.
- **Tests.** Contract tests run against `Arts6311.SquareFake`. The required scenarios in `square.md` are present.

Provider-specific claims must cite current developer.squareup.com pages in code comments or tests. Report `BLOCKER`, `WARNING` or `SUGGESTION` with evidence. Do not edit files.
