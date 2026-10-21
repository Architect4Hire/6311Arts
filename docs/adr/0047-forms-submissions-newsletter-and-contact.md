# 0047 — Forms: artist submissions, contact messages and the newsletter list

- **Status:** Accepted, except how long visitors' details are kept: `DECIDE`
- **Date:** 2026-10-21
- **Rules affected:** `.claude/rules/api-contract.md`, `.claude/rules/media.md`, `.claude/rules/gateway.md`,
  `.claude/rules/aspire.md`
- **Builds on:** [0023](0023-identity-module-and-email-through-the-outbox.md) (email through the outbox),
  [0024](0024-bff-session-tickets-and-edge-antiforgery.md) (antiforgery on every unsafe request),
  [0030](0030-media-library-compressed-masters-and-renditions.md) (the media pipeline)

## Context

Prompt 2.12 gives the site's three forms a home in the platform: the artist submission form (a stub until now, which
replaced a Google Form), the contact form and the newsletter sign-up (both posted to Express routes that forwarded to
a webhook). The gateway already sends `/api/**` to the API, so those Express routes were unreachable through it.
They are public — anyone can send anything, as often as they like — and a submission carries pictures.

## Decision

### A `Forms` module

`Modules/Forms` owns three records:

| Record | What it holds |
|---|---|
| `ArtistSubmission` | Name, email and goals as columns, so the inbox can list and filter them. The rest of the answers are one JSON column, read whole and never queried. Status `New` / `Reviewed` / `Accepted` / `Declined`, staff notes, row version, and the pictures (`ArtistSubmissionImages`, a `Restrict` key to `MediaAsset`). |
| `ContactMessage` | Name, email, message, handled flag, row version. |
| `NewsletterSubscriber` | Email (trimmed and lower-cased; unique), consent time, consent wording version, source. |

### The public routes

`POST /api/v1/forms/submissions` (multipart), `/forms/contact` and `/forms/newsletter`: anonymous, behind the
gateway's `forms` rate limit (10 per 5 minutes per address) and antiforgery.

- **Every success is `202` with no body.** A honeypot hit (`company`) answers exactly as a real one and keeps nothing.
  A newsletter sign-up for an address already on the list refreshes its consent and answers the same, so the route
  says nothing about who is subscribed.
- **Validation accepts only what the site's form offers.** The choices are allow-lists in `FormsPolicy`, mirroring
  the form. Web addresses must be `http(s)`, because staff will click them.
- **A submission is a `submission` JSON part, then up to six `images` parts.** The answers are validated before a
  picture is read. Each picture streams into Media through a facade method made for it
  (`UploadSubmissionImageAsync`). If the submission is refused partway, the pictures it created are deleted again.

### Submitted pictures

They go through the whole media pipeline — file-signature check, metadata scrub, 2560 px master, renditions — as a
new asset kind, `Submission`:

- **10 MiB each** (`MediaPolicy.MaxSubmissionImageBytes`), below a staff upload's 25 MiB, because anyone can send
  one. A phone photo is 2 to 8 MB, and the master is 2560 px either way. The request is capped at **64 MiB** by
  `FormsPolicy.MaxSubmissionRequestBytes`, which both the API and the gateway's `forms-submissions` route apply.
- **Always a draft, never in the library list.** A picture Media already holds is linked, not refused. A staff
  upload is checked against the library only, so a hidden submitted picture never blocks staff adding the same one.

### Emails

Through `email.send-requested`, staged in the record's own save, one composer per kind:

| Kind | To | Carries |
|---|---|---|
| `forms.submission-received` | The artist | Fixed words only |
| `forms.submission-staff` | Staff | Name, address, goals, picture count, link into the CMS |
| `forms.contact-received` | The sender | Fixed words only |
| `forms.contact-staff` | Staff | The message and the sender's address |

- **Visitor confirmations never repeat what was typed.** Anyone can type anyone's address into a public form, so an
  echo would let a stranger send their words to someone else under the centre's name.
- **No email on a newsletter sign-up.** Double opt-in comes with the newsletter vendor (still `DECIDE` in
  external.md). Until then the list is consent records and a CSV export.
- **The staff inbox is per environment:** the AppHost's `forms-staff-email` parameter becomes the Worker's
  `Forms:StaffAddress`. Its default is a non-routable `staff@6311arts.localhost`, so nothing real is emailed unless
  an environment says so; only production sets the centre's inbox. Locally Mailpit catches everything. In Azure, a
  visitor's confirmation goes to whatever address they typed, so a non-production environment must restrict
  Azure Communication Services or send to a sink (recorded in prompt 6.1).

### The CMS routes

- **`/api/v1/cms/forms/submissions`** — list (by status), get, PATCH status and notes (`If-Match`). Editors; audited.
  The audit line says what changed, never what the notes say.
- **`/api/v1/cms/forms/messages`** — list (handled or not), get, PATCH handled. Editors; audited.
- **`/api/v1/cms/forms/newsletter`** — the list (Editors); `DELETE /{id}` and `GET /export` (CSV) for Admins only.
  Both are audited: the removal by row id rather than address, so the log does not keep what someone asked to have
  removed. The export defuses spreadsheet formulas (OWASP CSV injection) and is `no-store`.

### What the reviews changed

`@security-reviewer` and `@outbox-reviewer` read the backend before it shipped:

- **Submissions have their own rate limit,** `forms-submissions`: 3 an hour per address, separate from `forms`
  (10 per 5 minutes) for the newsletter and contact form. Each submission may carry six pictures to store and decode,
  and a flood of them must not spend the contact form's budget.
- **One confirmation an hour per address.** A visitor's confirmation is staged only if that address hasn't used the
  same form in the last hour (`FormsPolicy.ConfirmationCooldown`). Many senders can't flood one stranger's inbox under
  the centre's name, and staff still hear about every message.
- **Addresses are checked as the mail sender reads them** (`MailAddress` parse, no list or display-name characters).
  Anything looser would be accepted with a 202 and then poisoned at send time.
- **Two de-duplication pools.** A submitted picture is linked only to another submitted one, never to a library
  picture. Otherwise a visitor who has a public picture's bytes could pin it against deletion.
- **The newsletter sign-up commits in the submission's own save.** If it were a second save that failed after the
  submission committed, the artist would see an error and their retry would be a duplicate.
- **Picture clean-up never masks the error that caused it.** Each delete is independent, and a failed one is logged
  by type.
- **Production refuses a placeholder staff address** (`.localhost`, `.invalid`) at startup.
- **Composers return "nothing to send" only for a record that is gone.** Any other failure throws, so the email is
  retried rather than lost.

Considered and accepted:

- **Editors can read the newsletter list.** They already see submitters' and senders' addresses. Only taking a copy
  away (the export) and removal are Admin-only and audited.
- **Search terms in the newsletter list's query string are left to OpenTelemetry's default redaction of query values,**
  as the existing CMS search routes are.
- **The export is built in memory.** That is fine at the list's size; stream it if the list ever reaches the tens of
  thousands.

## Options considered

- **One `FormSubmission` table for every form.** Fewer tables, but a contact message has no status workflow and a
  submission has thirty fields; one shape would fit neither.
- **A column per answer.** Queryable, but nothing queries them, and every change to the form would be a migration.
  Declined for one JSON column beside the columns the inbox uses.
- **Pictures as attachments outside Media.** It would skip the metadata scrub and the type check, which are the
  point. Declined.
- **The staff limit of 25 MiB per picture.** One anonymous request could then be about 150 MiB. Declined.
- **Echoing the visitor's name in the confirmation.** Friendlier, and a spam relay. Declined.
- **Sending a newsletter confirmation now.** A mail-bomb vector without the vendor's double opt-in. Declined.

## Consequences

- The site's newsletter, contact and submission components post here from 3.3, which also deletes the Express
  routes.
- A picture a failed submission created and could not delete stays as a hidden draft, and so does one a crash
  stranded mid-request. It is never public. **Follow-up:** a Worker sweep that purges `Submission` assets older than a
  day that no `ArtistSubmissionImages` row names.
- **Follow-up before launch (security review):** submitted pictures still use the staff limits on dimensions (50 MP),
  and decodes are not bounded across requests. Lower the pixel cap for submissions, add a concurrency limit, and
  consider a challenge (Turnstile) on the submission form if abuse appears.
- **The newsletter list records consent but does not verify the address.** Anyone can sign up anyone's address.
  Double opt-in arrives with the vendor (external.md DECIDE); until then the list is not mailed from.
- `DECIDE`: how long submissions, messages and declined applicants' pictures are kept. A retention policy and a sweep
  are needed before launch.
- Using a submitted picture on site content (an accepted artist's work) means putting it in the library first. There
  is no such action yet.
