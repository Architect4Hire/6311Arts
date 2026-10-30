# 6311arts — Artist Accounts and Account Administration SCRUB Prompts

Atomic prompts that add two things the CMS and the site don't have yet:

- **an artist portal on the public site.** A roster artist signs in at `/signin`, lands on `/portal`, proposes
  changes to their own profile, uploads pictures for those changes, and reads their own artwork as Square has it.
  Nothing they do publishes.
- **account administration in Studio.** An `Admin` — the super user auth.md already describes as owning "user
  management" — creates accounts, updates them, assigns roles, sends password resets, locks, unlocks, ends every
  session, and deactivates. No password is ever typed, seen or stored by an administrator.

The architectural source of truth is `CLAUDE.md` plus `.claude/rules/auth.md`, `backend.md`, `content.md`,
`gateway.md`, `media.md`, `frontend.md` and `outbox.md`. Where this file and a rule disagree, stop and reconcile
them before writing code — several prompts below *change* a rule, and say so.

**Rules:** `auth.md` (the role model), `backend.md` (the seam), `content.md` (the lifecycle an approval drives),
`media.md` (uploads), `gateway.md` (the new edge routes), `outbox.md` (every email and revalidation)
**Skills:** `add-endpoint`, `add-content-entity`, `add-outbox-message`, `add-notification`, `add-media-feature`,
`new-component`
**Reviewers:** `@security-reviewer`, `@architecture-reviewer`, `@outbox-reviewer`, `@api-contract-checker`,
`@design-review`, `@test-gap-analyzer`

## Decisions this series takes

Two were settled before it was written, and the rest are its own.

| Decision | Settled as |
|---|---|
| The super user is **today's `Admin`**, not a fifth role | auth.md already gives `Admin` "everything, including … user management". The series builds the seam and the screens behind the existing `CmsAdmin` policy rather than splitting the role. |
| An artist is a **new fourth role**, `Artist`, on the public site | A roster artist is not staff: they never reach `/admin/**`, and they never write a published record. |
| An artist's profile edit is a **reviewed request**, not a write | The same shape as `SquareChangeRequest` (ADR 0032): the portal stages an audited request, staff approve it, and the approval is the write. Field ownership stays with the CMS. |
| An administrator **never sets a password** | Creation sends an invite link; a reset sends a reset link. Nothing in Studio accepts, displays or transports a password. |
| An account is **deactivated, never deleted** | `AuditLog.ActorUserId`, `UpdatedBy`, `DeletedBy` and `ReviewedBy` all point at user ids. Deleting a user would unname every row they touched. |

## Where it slots into the run order

```text
2.13 (CMS checkpoint) + 3.1b (publishing an image) ──> A.1 … A.9 ──> A.10 … A.14 ──> A.15 ✅
                                                                          │
Phase 5.1 + 5.4 (Square links and the first push) ────────────────────────┘ (A.14 only reads better after this)
```

| Prompts | Need | Notes |
|---|---|---|
| A.1 – A.5 | 1.9, 1.10, 2.2a | Identity, the gateway and the role model. No screens. |
| A.6 – A.8 | 2.2 (admin shell), A.4 | Studio's People section. Usable on its own: this is the super-user half. |
| A.9 – A.13 | 2.4 (Artists), 2.3 + 3.1b (media) | The portal and its review queue. |
| A.14 | 2.10 (shop items) | Works before Phase 5: an unsynced piece reads "Not in Square yet" (ADR 0038, 0042). |
| A.15 | everything above | Checkpoint: reviewers, the E2E test, the runbook. |

**Run A.6 – A.8 before A.9.** The portal needs an account, and an account needs the screen that makes one. Until
A.7 ships, the only way to make an artist account is a seed, and this series deliberately doesn't add one.

## The role model after this series

`IdentityRoles` gains `Artist`, and the sets around it get names. The new row is the whole change to auth.md's
table.

| Role | Surface | Email confirmed first | Password floor | Session (idle / absolute) |
|---|---|---|---|---|
| `Admin` | `/admin/**`, every CMS route, account administration | yes | 14 | 30 min / 8 h |
| `Editor` | `/admin/**`, CMS content | yes | 14 | 30 min / 8 h |
| `Artist` | `/portal/**`, `/api/v1/artist/**` | **yes** | **14** | **30 min / 12 h** |
| `Member` | the member area (Phase 5) | no | 8 | 7 days / 30 days |

- **An `Artist` is held to the staff password floor and a confirmed address** because an artist account can change
  what the site says under someone's name. `IdentityRoles.Staff` keeps its meaning — the roles that reach
  `/admin` — and the password rule reads a wider set (`IdentityRoles.StrongPassword = [Admin, Editor, Artist]`).
  Renaming `StaffPasswordRule` is not worth the churn; its remark says which set it reads.
- **A combined session takes the shortest of every limit its roles carry**, which generalizes auth.md's existing
  "a session holding both a staff role and `Member` gets the staff limits". Role changes still move the absolute
  deadline forward only.
- **`Artist` is not staff.** `/admin/**` and `/api/v1/cms/**` stay `CmsEditor`, so an artist session gets a 403
  there, and the Studio nav is never rendered for one.

## Who may write what

The series adds one write path to the site and one to Studio, and nothing else moves.

| Record | CMS (`Editor`/`Admin`) | Portal (`Artist`) |
|---|---|---|
| `Artist` (bio, medium, portrait, slug, status) | writes, publishes | **never** — proposes an `ArtistProfileChange` |
| `ArtistProfileChange` | approves, rejects | creates one, withdraws their own |
| `MediaAsset` (`ArtistUpload` kind) | reviews, promotes on approval | uploads, within a quota, `Draft` only |
| `ShopItem` content, SKU, starting figures | writes | **never** — reads their own, no fields |
| Square projections (price, stock, sold out) | never (square.md) | never |
| `ArtsUser`, roles, lockout, sessions | `Admin` only, audited | own display name and password only |

## New routes

| Path | Policy | Added by |
|---|---|---|
| `GET/POST /api/v1/cms/users`, `GET/PATCH /api/v1/cms/users/{id}` | `CmsAdmin` | A.4 |
| `POST /api/v1/cms/users/{id}/{password-reset,email-confirmation,lock,unlock,deactivate,reactivate}` | `CmsAdmin` | A.4 |
| `POST /api/v1/cms/users/{id}/sessions/revoke` | `CmsAdmin` | A.4 |
| `GET /api/v1/cms/artist-profile-changes`, `GET .../{id}`, `POST .../{id}/{approve,reject}` | `CmsEditor` | A.12 |
| `GET /api/v1/artist/me` | `ArtistPortal` | A.9 |
| `POST /api/v1/artist/profile-changes`, `DELETE .../{id}` | `ArtistPortal` | A.11 |
| `POST /api/v1/artist/uploads` | `ArtistPortal` | A.13 |
| `GET /api/v1/artist/artwork` | `ArtistPortal` | A.14 |
| `POST /bff/change-password` | authenticated | A.10 |
| `/portal/**` (the site bundle, gated like `/admin`) | `ArtistPortal` | A.5 |
| `/account/accept-invite` | anonymous | A.8 |

---

## A.1 — ADRs for the role, the administration model and reviewed profile changes

```
SCOPE: Write docs/adr/0055…0058 (claim the numbers now; renumber if something else lands first), one short
ADR each (context, decision, options considered, consequences, rules affected):
  0055 The Artist role and the artist portal — a fourth role on the public site, not staff; confirmed email
       and the 14-character floor; its own session tier; /portal gated at the gateway like /admin; the
       ArtistAccount link record rather than a user id on the Artist content entity.
  0056 Account administration in Studio — the existing Admin role and CmsAdmin policy, not a fifth role;
       invite-only creation with no administrator-set password; an invite token separate from the one-hour
       reset token; deactivation instead of deletion; the protected seeded admin; the self-administration
       and last-admin guards; every operation audited.
  0057 Artist profile changes as reviewed requests — why the portal stages a request (the SquareChangeRequest
       shape, ADR 0032) instead of writing a Draft; one pending request per artist; approval is a staff write
       that revalidates (content.md: an artist's every edit stages site.revalidate-requested).
  0058 Artist uploads — a new MediaAsset kind beside Submission (ADR 0047, 0030), Draft only, per-artist
       quota, alt text required from the uploader, out of the staff library list, promoted only by an approval.

CONSTRAINT: auth.md, content.md, media.md, gateway.md. Cite the rule file each ADR backs and the ADR it
extends (0018, 0023, 0024, 0030, 0032, 0047, 0052).

RESTRICTION: Do NOT write code, migrations or rule edits in this prompt. Do NOT decide the DECIDE items
listed at the end of this file — record the options and their costs.

UTILIZATION: Read auth.md, content.md and media.md first; docs/adr/README.md for the format.

BEHAVIOR: Draft all four, wait for my review, then commit.
```

## A.2 — The `Artist` role in Identity

```
SCOPE: Modules/Identity: add IdentityRoles.Artist ("Artist") and IdentityRoles.StrongPassword
([Admin, Editor, Artist]); StaffPasswordRule reads StrongPassword (name kept, remark says which set);
the confirmed-email requirement covers Artist; IdentityPolicy gains nothing new. Seed the role in
MigrationService beside the existing three. The API gains an ArtistPortal authorization policy (Artist
only) registered beside CmsEditor / CmsAdmin / Member.

CONSTRAINT: auth.md; ADR 0055; the migration is generated, never hand-edited.

RESTRICTION: Do NOT touch session lifetimes (A.3), the gateway (A.5) or any route yet. Do NOT widen
IdentityRoles.Staff — /admin stays Admin + Editor. No seeded artist account.

UTILIZATION: @security-reviewer, @architecture-reviewer.

BEHAVIOR: Plan, wait, implement; tests: a short or listed password is refused for an Artist target at
reset as for staff, an unconfirmed Artist is refused at credential check, IdentityRoles.IsStaff is false
for Artist, the ArtistPortal policy refuses Admin/Editor/Member. Report the migration diff.
```

## A.3 — Session tiers and the shortest-limits rule

```
SCOPE: Gateway session lifetimes: Gateway:Session gains an Artist tier (30 min idle / 12 h absolute,
configuration); the tier resolver takes the shortest idle and the shortest absolute limit across every role
the ticket carries, which keeps auth.md's "staff plus Member gets the staff limits" as a special case. Record
the resolved tier on the ticket so a role change at revalidation can only move the absolute deadline forward.

CONSTRAINT: auth.md ("Lifetimes", "Role changes move the absolute deadline forward, never back"); ADR 0024,
0055.

RESTRICTION: No new cookie, no change to the ticket's Redis keying, no tokens to the browser. Do NOT shorten
the Member defaults.

UTILIZATION: @security-reviewer.

BEHAVIOR: Plan the resolver as a table of role sets → limits, wait, implement; tests: an Artist session
expires at the Artist limits, an Artist + Member session takes the Artist limits, an Admin + Artist session
takes 8 h, promotion at revalidation never shortens an absolute deadline already in flight.
```

## A.4 — Identity: the account administration seam

```
SCOPE: Modules/Identity, the full Controller → Facade → Business → DataLayer → Repository seam for account
administration, and the API routes under /api/v1/cms/users (CmsAdmin):
  GET    /api/v1/cms/users                      list: search by display name or address, filter by role and
                                                state, paged, each row carrying its ETag
  POST   /api/v1/cms/users                      invite: { displayName, email, roles[], artistId? } — creates
                                                the account with no password, sends the invite email
  GET    /api/v1/cms/users/{id}                 one account: display name, address, confirmed, roles, lockout,
                                                deactivation, last sign-in, linked artist, ETag
  PATCH  /api/v1/cms/users/{id}                 display name, address, roles, artist link (If-Match)
  POST   /api/v1/cms/users/{id}/password-reset  sends a reset link; never returns or sets a password
  POST   /api/v1/cms/users/{id}/email-confirmation   resends a confirmation
  POST   /api/v1/cms/users/{id}/lock | /unlock  Identity lockout, set and cleared
  POST   /api/v1/cms/users/{id}/sessions/revoke rotates the security stamp ("log out everywhere")
  POST   /api/v1/cms/users/{id}/deactivate | /reactivate
ArtsUser gains DeactivatedAt, DeactivatedBy, ProtectedAccount (set by IdentityUserSeeder) and LastSignInAt.
The ETag is Identity's ConcurrencyStamp; a missing If-Match on a write is a 428, a stale one a 409.
Guards, each its own error code and its own test: identity.self_administration (nobody changes their own
roles, lockout or deactivation), identity.last_admin (the last enabled Admin cannot be demoted or
deactivated), identity.protected_account (the seeded admin cannot be demoted, deactivated or have its
address changed), identity.artist_link_required (the Artist role needs a linked artist — A.9 supplies the
link, so until then the role is refused), identity.email_taken. Every operation writes an IdentityAudit row
naming the actor, the target and what changed. A deactivation and an address change rotate the security
stamp. Emails are staged as email.send-requested in the same save, never sent inline.

CONSTRAINT: auth.md ("Sensitive operations need an explicit policy and an audit row"); backend.md; add-endpoint;
add-notification; outbox.md; ADR 0056.

RESTRICTION: No endpoint accepts, returns, logs or stages a password or a token. No account enumeration from
these routes beyond what CmsAdmin already sees. No DELETE of a user. Do NOT touch the /bff or internal
session routes. The invite and reset tokens are minted at send time by the composer, never put in an outbox
payload (auth.md).

UTILIZATION: add-endpoint, add-notification, add-outbox-message; @security-reviewer, @outbox-reviewer,
@architecture-reviewer, @api-contract-checker.

BEHAVIOR: Plan the seam, the ViewModels and ServiceModels, the error codes and the migration; wait;
implement. Tests: every guard above, a 428 and a 409 on each write, an Editor gets 403 on every route, an
invite stages exactly one email and leaves the account unable to sign in, a deactivated account fails the
credential check and its live session ends at revalidation, an audit row exists for every operation, and a
failed save leaves neither the user change nor the email message. Report.
```

## A.5 — The edge: artist routes, the gated portal, limits

```
SCOPE: Gateway configuration and transforms: route artist (/api/v1/artist/**, ArtistPortal policy,
token-bearing, new artist rate limit) and artist-upload (POST /api/v1/artist/uploads, as artist with
MaxRequestBodySize from MediaPolicy's artist-upload limit); route portal (/portal/** → the site
destination, ArtistPortal policy, NOT token-bearing — the site bundle has no API call to authorize, the
same carve-out as admin) and its Cache-Control: no-store; cms-users needs nothing new (it is under
/api/v1/cms/**). Add Disallow: /portal to robots.txt. An unauthenticated navigation to /portal redirects
to /signin?returnUrl=/portal; an Editor or Member session there is a 403.

CONSTRAINT: gateway.md (the route table, the token-bearing rule and its test, the named rate limits);
auth.md; ADR 0055. A policy a route names but configuration omits must still stop the host at startup.

RESTRICTION: No business logic in the gateway. Do NOT make /portal token-bearing, and do NOT move /signin
behind a session. No new cookie. Do NOT relax the global request-size limit.

UTILIZATION: @security-reviewer.

BEHAVIOR: Plan the route rows and the configuration diff, wait, implement; tests: route resolution for each
new row, /portal without a session redirects to /signin with the returnUrl, a Member session on /portal and
on /api/v1/artist/me is 403, an Artist session on /admin and /api/v1/cms/** is 403, /api/v1/artist/** reaches
the API carrying a user token, the upload route's size cap, a 429 per the new limit, and robots.txt.
```

## A.6 — Studio: the People section and the account list

```
SCOPE: projects/admin: a People section (path 'people', icon, Admin-only roles, summary) in
features/layout/sections.ts replacing nothing else, its lazy list screen in features/people/, and a typed
UsersApi in services/ over A.4's routes with models mirroring its ServiceModels exactly. The list: search,
role and state filters, paging, a row per account showing display name, address, confirmed, roles, state
(Invited / Active / Locked / Deactivated) and last sign-in, with loading, empty, error and success states.

CONSTRAINT: frontend.md ("The shell", one component per folder, three files never one); design-system.md;
ADR 0050, 0051, 0056.

RESTRICTION: No editor, no dangerous actions and no password anything in this prompt. No HttpClient in a
component. No session or account data in browser storage. The section is Admin-only: an Editor must not see
the nav entry or reach the route.

UTILIZATION: new-component; @design-review, @api-contract-checker.

BEHAVIOR: Plan the screen states and the models, wait, implement, npm test, and walk me through
/admin/people through the gateway URL as an Admin and then as an Editor.
```

## A.7 — Studio: invite and update an account

```
SCOPE: projects/admin features/people/: the account editor as a DETAIL_ROUTES entry (people/new,
people/:id) so it inherits the section's roles and the unsaved-changes guard. New: display name, address,
roles (checkboxes, with the Artist row demanding a linked artist through the artists picker), then "Send
invite". Existing: the same fields plus the state panel, If-Match on save, a 409 showing both versions and
keeping the edit. Each refusal from A.4 (self_administration, last_admin, protected_account,
artist_link_required, email_taken) is shown in place, beside the field it belongs to where it has one.

CONSTRAINT: frontend.md; design-system.md; A.4's contract; ADR 0051, 0056.

RESTRICTION: No password field, ever — the screen says an invite link is emailed and that nobody here can
see or set a password. No role checkbox the signed-in Admin could use on themselves. No delete action.

UTILIZATION: new-component; @design-review, @security-reviewer, @api-contract-checker.

BEHAVIOR: Plan the form and every error placement, wait, implement, npm test, and walk me through inviting
an Editor and an Artist, then editing each, through the gateway URL.
```

## A.8 — Studio: resets, lockout, sessions, deactivation — and the invite page

```
SCOPE: Two halves of one flow.
Studio (features/people/): the account's dangerous actions, each behind the shared delete-confirm-style
dialog naming what happens and that it is recorded under the Admin's name — "Send a password reset",
"Resend confirmation", "Lock" / "Unlock", "Sign out everywhere", "Deactivate" / "Reactivate". Each shows
its outcome in place and refreshes the ETag.
Site (projects/site pages/accept-invite/): /account/accept-invite, the invited person's one screen —
noindex, Cache-Control no-store, the userId and token read only from location.hash through LinkFragment,
the fragment cleared before anything else runs, the new password posted to /bff/accept-invite, success
sending staff to /signin and an artist to /portal. The gateway's /bff/accept-invite joins the bff-account
rate limit and calls an internal identity route that completes the invite (sets the password, confirms the
address, rotates the stamp) and refuses a spent or expired invite as one auth.token_invalid.

CONSTRAINT: auth.md (emailed links, ADR 0028, 0029: token in the fragment, one hour for a reset and the
invite's own lifetime from ADR 0056, one non-enumerating answer); frontend.md; add-endpoint.

RESTRICTION: The invite token never reaches the server in a URL, the SSR render, browser storage, a log line,
a trace attribute or an outbox payload. The password policy is checked after the token. An administrator
never learns the password that gets set. No new browser-facing auth endpoint in the API.

UTILIZATION: add-endpoint, new-component; @security-reviewer, @design-review.

BEHAVIOR: Plan both halves and the internal route, wait, implement; tests: each action's audit row, an
invite works once, an expired invite is one answer, the chosen password is held to the invited roles' floor,
the page keeps the token out of the DOM and the console; then walk me through invite → Mailpit → accept →
/signin → Studio, and reset → Mailpit → new password → old session gone.
```

## A.9 — The artist account link and `GET /api/v1/artist/me`

```
SCOPE: Modules/Artists: an ArtistAccount link entity — ArtistId (FK Artist, Restrict), UserId (the Identity
user id, Restrict), LinkedAt, LinkedBy — unique on each column, so one account belongs to one artist and one
artist has one account; the facade operations A.4's PATCH calls to set and clear it; and
GET /api/v1/artist/me (ArtistPortal): the signed-in artist's own record as the portal shows it — name, slug,
medium, bio, status, portrait, public address when published — plus their pending profile change once A.11
exists, and nothing else.

CONSTRAINT: backend.md (cross-module traffic is facade to facade; the entity crosses only where a foreign
key already crosses); content.md; add-endpoint; ADR 0055.

RESTRICTION: No user id on the Artist entity. The artist identity comes from the token's sub resolved through
the link, NEVER from a route parameter, a body field or a header — a test must prove a forged id changes
nothing. A soft-deleted artist's account reads as no artist (the global filter stands), not a 500. No write
path in this prompt.

UTILIZATION: add-endpoint, add-content-entity; @architecture-reviewer, @security-reviewer,
@api-contract-checker.

BEHAVIOR: Plan the entity, the configuration, the migration and the facade signatures; wait; implement;
tests: the link's two unique constraints, me returns only the caller's artist, a second account for the same
artist is refused, an Artist session with no link gets a 403 with its own code, a different artist's id in
the request changes nothing. Report the migration diff.
```

## A.10 — The portal shell, sign-in and account self-service

```
SCOPE: projects/site pages/portal/: the portal shell at /portal (noindex, no-store, its own lazy routes),
a portalGuard over /bff/session roles, a dashboard naming what the artist can do, and account self-service —
display name and "Change your password" (current password, then the new one, held to their roles' floor).
The gateway gains /bff/change-password (authenticated, antiforgery, bff-account rate limit) calling an
internal identity route that checks the current password, sets the new one and rotates the stamp — so every
other session ends and this one is re-issued. /signin keeps working for artists and honours
?returnUrl=/portal through safeReturnUrl; the site header's staff-access affordance shows "Your portal" for
an Artist session and the Studio link for staff.

CONSTRAINT: frontend.md (portal pages render on request, session from /bff/session only); auth.md; ADR 0024,
0027, 0055; add-endpoint.

RESTRICTION: No session or role data in browser storage. No portal page prerendered or cached. A password
change needs the current password — never a bare set. No Studio link for an artist, and no artist link in
the Studio nav.

UTILIZATION: add-endpoint, new-component; @design-review, @security-reviewer.

BEHAVIOR: Plan the routes, the guard and the change-password flow, wait, implement, npm test; tests: the
guard refuses Member and staff-only sessions, a wrong current password is refused without disclosing
anything else, a successful change ends a second live session and keeps this one, portal responses are
no-store and noindex; then walk me through artist sign-in → /portal → change password through the gateway
URL.
```

## A.11 — Artist profile changes: the request

```
SCOPE: Modules/Artists: ArtistProfileChange — ArtistId, SubmittedByUserId, Status
(Pending / Approved / Rejected / Withdrawn), the proposed values (Medium, Bio, PortraitMediaId, and the
ordered proposed images once A.13 lands), a note to staff, SubmittedAt, ReviewedAt, ReviewedBy, ReviewNote,
RowVersion — with one Pending row per artist (a filtered unique index) — plus
POST /api/v1/artist/profile-changes (create or replace this artist's pending request) and
DELETE /api/v1/artist/profile-changes/{id} (withdraw their own). Validation is the Artist editor's own: bio
length, plain text only (never HTML, ADR 0031), medium length. Submitting stages one
email.send-requested to staff; the payload carries the request id and nothing else.

CONSTRAINT: content.md (the Artist record's fields and that bio is plain text); backend.md; add-content-entity;
add-outbox-message; add-notification; ADR 0057.

RESTRICTION: This path NEVER writes the Artist record, its status, its slug or its name, and never stages
site.revalidate-requested — the approval does (A.12). The artist comes from the token through the link, not
the body. No staff fields (notes, status, review) are writable here. No content, address or picture bytes in
the outbox payload.

UTILIZATION: add-content-entity, add-endpoint, add-outbox-message, add-notification;
@architecture-reviewer, @outbox-reviewer, @security-reviewer.

BEHAVIOR: Plan the entity, the index, the seam and the message type; wait; implement; tests: a second
Pending request replaces the first rather than failing the index, HTML in the bio is refused, another
artist's request is a 404, withdrawing works once, exactly one staff email per submission and one on a
redelivery, a failed save leaves neither the row nor the message, the Artist record is untouched by every
one of these. Report.
```

## A.12 — Studio: the review queue

```
SCOPE: /api/v1/cms/artist-profile-changes (CmsEditor): the queue (filter by status, paged, ETags), one
request with its proposed values beside the live ones, and POST .../{id}/approve and .../{id}/reject (both
If-Match, both audited, reject requiring a note). Approve is the staff write: it applies the proposed values
to the Artist record as the reviewing staff member (UpdatedBy, RowVersion rotated), promotes the proposed
images, stages site.revalidate-requested in the same save — an artist's every edit stages it, published or
not (content.md, ADR 0042) — and stages the artist's outcome email. Plus the Studio screens in
features/people/ or features/artists/, wherever the plan argues better: a queue list, a side-by-side review
screen, and a count on the Dashboard.

CONSTRAINT: content.md (lifecycle, revalidation, audit); backend.md; frontend.md; add-endpoint;
add-notification; ADR 0057.

RESTRICTION: Approval changes content, never status: a Draft artist stays Draft and a Published one stays
Published. No publish, schedule or archive from this screen. A rejected request never touches the Artist
record. No Square-owned field is readable or writable here.

UTILIZATION: add-endpoint, add-notification, new-component; @architecture-reviewer, @outbox-reviewer,
@design-review, @api-contract-checker.

BEHAVIOR: Plan the routes, the approval's single save and the screens; wait; implement; tests: approve
writes the artist and stages exactly one revalidation and one email, approve twice is a 409 and writes once,
reject leaves the record alone and needs a note, status is unchanged either way, an Artist session gets 403
on every route here; then walk me through portal submit → Studio review → the site showing the new bio.
```

## A.13 — Artist uploads

```
SCOPE: Modules/Media: an ArtistUpload asset kind beside Submission (ADR 0047) — Draft always, its own
MediaPolicy limits (byte cap, per-artist quota and window), alt text required from the uploader, out of the
staff library list but present in A.12's review screen — and POST /api/v1/artist/uploads (ArtistPortal),
streamed through the gateway's artist-upload route, staging media.rendition-requested as a staff upload does.
A quota refusal is its own error code. The portal gains the uploader on its profile screen: pick a file, see
progress, type alt text, attach it to the pending request.

CONSTRAINT: media.md (the master, the metadata allow-list, the limits, delivery, "alt text required before
an asset can be attached to published content"); add-media-feature; add-outbox-message; ADR 0030, 0047, 0058.

RESTRICTION: Images only, by file signature. No SVG. Never public blob access, and a Draft asset is served
only to a session that may see it — an artist sees their own, staff see all. An artist never deletes, renames
or re-points a library asset, and never promotes one to Published. EXIF location data is dropped as always.

UTILIZATION: add-media-feature, add-outbox-message, new-component; @security-reviewer, @outbox-reviewer,
@design-review.

BEHAVIOR: Plan the kind, the policy numbers, the seam and the uploader; wait; implement; tests: an oversize
or non-image upload is refused before anything decodes, the quota refuses the next one, alt text is required,
renditions are made once for a redelivered message, another artist cannot read the Draft asset, the staff
library list does not show it; then walk me through uploading a portrait into a pending request.
```

## A.14 — The artist's own artwork, read-only

```
SCOPE: GET /api/v1/artist/artwork (ArtistPortal): the signed-in artist's own ShopItems — title, slug,
categories, lead image, the CMS's SKU, and Square's projections as they stand (price, stock, sold out, sync
status), each labelled as Square's. Built facade to facade: Artists resolves the caller, Shop answers for
their items. An unsynced piece reads "Not in Square yet" and an unpriced one "Price to be announced"
(ADR 0038, 0042), so the screen is honest before Phase 5. Plus the portal screen listing them.

CONSTRAINT: square.md (field ownership); backend.md (facade to facade); content.md; add-endpoint;
frontend.md ("Square-held values … are never edited in place").

RESTRICTION: Read-only, with no form, no command and no action — not a price change, not a quantity change,
not a sync replay. Soft-deleted and archived items are absent. Another artist's items are unreachable.
Nothing here writes a Square-owned projection, and nothing calls Square.

UTILIZATION: add-endpoint, new-component; @integration-reviewer, @architecture-reviewer, @design-review.

BEHAVIOR: Plan the read and the screen states, wait, implement; tests: only the caller's items come back, a
deleted or archived item is absent, an unsynced item's labels, no write verb is routed under
/api/v1/artist/artwork; then walk me through the portal's artwork list.
```

## A.15 — ✅ Checkpoint: the whole path, reviewed

```
SCOPE: Close the series. Run @security-reviewer, @architecture-reviewer, @outbox-reviewer,
@api-contract-checker, @design-review and @test-gap-analyzer over everything A.1–A.14 touched, and fix what
they find. Add the Category=E2E test that walks it: an Admin invites an artist in Studio → the invite email
arrives in Mailpit → the artist accepts it and signs in at /signin → /portal → submits a profile change with
an uploaded portrait → staff approve it in Studio → the artist's page on the site shows the new bio and the
portrait → the Admin ends the artist's sessions and deactivates the account → the artist can no longer sign
in. Write docs/runbooks/accounts.md: how to make the first artist account, what an invite looks like, what to
do when someone's locked out, how to deactivate someone who leaves, and what an artist can and cannot do.
Update .claude/rules/auth.md (the role table, the Artist tier, the account-administration routes and their
guards), content.md (reviewed artist profile changes), gateway.md (the new route rows and rate limit) and
media.md (the ArtistUpload kind), and add the rows to docs/prompts/scrub-prompts.md's phase map pointing here.

CONSTRAINT: CLAUDE.md "Usage" (dotnet build -warnaserror, dotnet test from the root, npm run build and
npm test from src/web); the E2E test needs Docker.

RESTRICTION: Do NOT weaken a guard to make a test pass — bring it back here instead. No rule edit that
changes behaviour: the rules follow the code the series already agreed.

UTILIZATION: every reviewer above; playwright-cli for the E2E walk.

BEHAVIOR: Run the reviewers, show me their findings and your fix list, wait; then fix, write the test and the
runbook, edit the rules, run the full backend and frontend suites, and report: what each reviewer said, what
changed, and anything still open.
```

---

## What the series owes the rule files

A.15 makes these edits, but every prompt should be written knowing they're coming.

| File | Edit |
|---|---|
| `auth.md` | the fourth role and its row in the lifetimes table; the shortest-limits rule; `StrongPassword`; the `ArtistPortal` policy; the `/api/v1/cms/users/**` routes under "Sensitive operations"; `/bff/accept-invite` and `/bff/change-password`; the invite token's lifetime beside the one-hour reset |
| `content.md` | reviewed artist profile changes as the one content write that starts outside the CMS, and that an approval revalidates like any artist edit |
| `gateway.md` | the `artist`, `artist-upload` and `portal` route rows; the `artist` rate limit; `portal` as the second deliberate non-token-bearing gated route |
| `media.md` | the `ArtistUpload` kind, its quota, and that an artist sees only their own Draft assets |
| `frontend.md` | the portal's own guard and no-store rendering; the People section's Admin-only nav entry |

## Open decisions (`DECIDE` in A.1's ADRs)

- **The invite token's lifetime.** A one-hour reset token is right for a reset someone just asked for and wrong
  for an invitation that waits for a person to read their email. The recommendation is a separate token purpose at
  **7 days**, single use, with the account unable to sign in until it's accepted — but the number is a decision,
  and so is whether a lapsed invite is re-sent or re-created.
- **Who may send an administrative password reset.** `CmsAdmin` throughout this file. An argument exists for
  letting an `Editor` resend a *confirmation* (harmless, non-enumerating) while resets stay `Admin`.
- **An Admin-initiated address change.** Settled: the new address lands unconfirmed and a confirmation goes out.
  Open: whether the old address is notified, which is the only thing standing between a compromised Admin session
  and a silent account takeover.
- **A temporary-password escape hatch.** Deliberately absent. If the gallery ever needs to onboard someone with no
  working mailbox, it needs its own ADR, not a field on the editor.
- **Whether an artist may propose a slug or a name change.** Absent here: a slug is locked after publish and
  redirects when it moves (ADR 0035), and a name is how the roster is read. A request type for it is a later
  decision.
- **What else the portal eventually shows.** Class rosters for an artist who also teaches belong to the
  `Instructors` roster (ADR 0043), not this role, and member-facing billing is Phase 5's. Both stay out until
  someone asks.

## Verification, per CLAUDE.md

```text
dotnet build -warnaserror
dotnet test                              # from the repo root; E2E needs Docker
cd src/web && npm ci && npm run build && npm test
aspire run                               # browse only through the gateway endpoint
```

Every prompt that touches Square-adjacent reads passes against `Arts6311.SquareFake`. No prompt in this series
calls Square, and none may.
