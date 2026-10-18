---
name: add-notification
description: Add a 6311arts transactional email (form confirmation, order confirmation, password reset, membership renewal failure) delivered through the outbox.
---

# Add a Notification (email)

1. **Define a stable kind**, for example `forms.submission-received` or `members.renewal-failed`. Give it a template (Razor or text) with only safe display data.
2. **Raise it after the commit.** The DataLayer of the originating operation stages `email.send-requested {kind, subjectId, recipientUserId|recipientAddressRef}` in the same save. Use `add-outbox-message`.
3. **The handler:**
   - reloads the subject through its facade
   - renders the template
   - sends via `IEmailSender` (Mailpit locally, Azure Communication Services in Azure)
   - is idempotent on (`kind`, `subjectId`, `recipient`)
4. **Links** are relative to the public site origin from config. They re-authorize when opened and never carry tokens beyond Identity's own confirmation and reset tokens.
5. **Never put card data, full addresses or staff-only notes in an email.**
6. **Never block the originating request** on email delivery.

## Tests

- duplicate delivery sends once
- provider failure retries, then poisons
- template contains no secrets
- links point at the configured origin

## Checklist before done

- [ ] Kind, template and recipients defined
- [ ] Staged in the originating save; idempotent handler
- [ ] Shows in Mailpit locally; tests pass
