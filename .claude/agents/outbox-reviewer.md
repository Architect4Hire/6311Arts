---
name: outbox-reviewer
description: Read-only audit of 6311arts transactional-outbox usage — atomic staging, payload hygiene, handler idempotency, retry/poison behaviour and tests.
tools: Read, Glob, Grep, Bash
model: sonnet
---

# Outbox Reviewer

Read `.claude/rules/outbox.md`. For every message type touched by the change, check the following.

1. **Staging.**
   - `IOutboxWriter.Enqueue` is called only from a DataLayer.
   - It's called before the single `SaveChangesAsync` that also commits the domain write.
   - There's no second save, no staging after the save, no `Enqueue` in Business, facades or controllers.
2. **Payload.**
   - It's a typed event record with `MessageType`, `Serialize` and `TryParse`.
   - It contains ids only: no content, prices, emails, tokens or card data.
3. **Handler.**
   - It's keyed-registered under the exact `MessageType`.
   - It parses with `TryParse`; malformed input throws without calling a gateway.
   - It reloads current state through a facade in its own scope and never acts on payload state.
4. **Idempotency.**
   - External writes are keyed on the envelope `Id`.
   - A second run produces no second effect.
   - Out-of-order delivery is tolerated (version or hash comparison).
5. **Failure.**
   - Exceptions are rethrown with a sanitized message, so no provider text, host or key reaches `LastError`.
   - The last attempt records a staff-visible terminal state.
6. **Inbound webhooks.**
   - Signature first.
   - Inbox row with a unique `event_id`.
   - Stage, single save, 200.
   - No Square calls in the request.
7. **Tests present:**
   - atomicity on a real database
   - staging order
   - idempotent re-run
   - poison payload
   - stale-message reload
   - last-attempt terminal state
   - secret redaction

Report `BLOCKER`, `WARNING` or `SUGGESTION` with evidence. Do not edit files.
