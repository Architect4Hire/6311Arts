# 0052 — Soft delete for CMS content: a query filter, a Deleted view, and a restore that comes back as a draft

- **Status:** Accepted
- **Date:** 2026-10-21
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/api-contract.md`, `.claude/rules/auth.md`,
  `.claude/rules/media.md`
- **Builds on:** [0031](0031-artists-and-the-shared-content-lifecycle.md) (the shared content lifecycle),
  [0030](0030-media-pipeline.md) (the media delete guard and its `Restrict` keys),
  [0035](0035-redirects.md) (a published slug change owes a redirect),
  [0036](0036-revalidation-scheduled-publishing-and-previews.md) (revalidation, the scheduled publisher, previews),
  [0037](0037-classes-and-the-shared-catalog-sku-registry.md) (the shared SKU registry),
  [0042](0042-site-read-api-and-its-cache.md) (the site reads and their cache)

## Context

`Archived` is the strongest end state content has (`PublishStatus`, `PublishLifecycle`). It retires a record from
the site, and that is all it does. An archived record still fills the admin list, still holds its slug and its SKU,
still appears under a status filter, and has no way out of `Archived` at all.

Staff asked for something stronger, in their words: *things will be created by mistake and need cleaning up, and if
it is deleted it needs not be shown on the website as well*. Archive answers neither half — the mistake stays in
the CMS, and staff have to reason about whether "archived" really means "off the site".

Two further facts shaped the decision:

- **A deleted record has to disappear from everything**, not just the obvious reads: the CMS list, the site reads,
  the sitemap, previews, the scheduled publisher's due list, slug-uniqueness checks, and another module's
  cross-module link check. There are over forty call sites keyed on `PublishStatus.Published` alone, so a rule of
  the form "remember to exclude deleted rows" would be wrong somewhere within a prompt or two.
- **A delete made by mistake is the same accident as a record made by mistake.** Whatever we build has to be
  reversible by the person who has the authority to delete.

This ADR decides the mechanism and lands it across the shared kernel and **all seven publishable types**:
`Page`, `Artist`, `Instructor`, `Exhibition`, `ClassOffering`, `ShopItem` and `MembershipPlan`.

## Decision

### Soft delete, with an Admin-only restore

`DeletedAt` and `DeletedBy` on the entity (`ISoftDeletable`), `null` while the record is live. The row is never
removed, so the audit trail stays whole and nothing a foreign key points at vanishes under it. There is no purge
screen; if one is ever wanted it is a later decision, not a gap in this one.

`DELETE /api/v1/cms/{type}/{id}` → `204`, and `POST /api/v1/cms/{type}/{id}/restore` → the record. Both are
`CmsAdmin`, both carry `If-Match`, both are audited (`<type>.deleted`, `<type>.restored`) — the same treatment
archive already gets under auth.md's "Sensitive operations". `GET /api/v1/cms/{type}/deleted` is the Deleted view:
a cursor page carrying each row's title, slug, the status it had when it went, when it went, and its `etag`.

### Deletion is not a status, and not a `PublishCommand`

`PublishStatus` and `PublishLifecycle.Next` are untouched. `Status` goes on saying what the record was when it was
deleted, which is what the Deleted view shows and what the restore's audit line reports. A `Deleted = 40` status
would have been a smaller diff and was rejected: it loses that fact, it silently widens every status filter in the
contract and in the admin, and it invites the reading that deletion is a lifecycle step staff should reason about
alongside publishing. It is orthogonal to the lifecycle, so it is modelled that way.

### Delete works from any status

Including `Published`. "This was a mistake" is not a transition, and a flow that insists on an unpublish first
teaches staff to unpublish without reading the dialog. A delete whose record was `Published` stages
`site.revalidate-requested` in its own save, so the page stops being served within the Worker's poll — the same
path, and the same few seconds, as an unpublish (ADR 0036, 0049). A `Draft`, `Scheduled` or `Archived` record
stages nothing, because nothing public renders one (content.md).

### One EF global query filter per entity, and the five places that look past it

`SoftDelete.Apply` puts `HasQueryFilter(x => x.DeletedAt == null)` on the entity, so "a deleted record is not
there" is the default for every query ever written against it. This is a new pattern for this codebase, so the
reads that must see deleted rows are named here and each is covered by a test:

| Looks past the filter | Why |
|---|---|
| The Deleted view, and the find behind a restore | They exist to work on deleted rows |
| `GetSlugsAsync` (the placeholder seed) | The seeder adds the slugs that are *missing*. Filtered, the next MigrationService run would seed `/privacy` again and undo the delete |
| The importer's legacy-id lookup (4.x, per type) | Same hazard: a re-import must not recreate what staff deleted |
| `MediaRepository.IsReferencedAsync` | **Correctness, not convenience** — see below |

`SoftDeleteTests` holds the model side for every type at once: a record with a publish status is `ISoftDeletable`
(so a new content type cannot quietly arrive without a delete), each one carries the filter, and each unique slug
index carries `SoftDelete.LiveRowsOnly`.

Every **unique** index on a soft-deletable table gains `HasFilter(SoftDelete.LiveRowsOnly)`, because a unique index
is a database constraint and knows nothing of the query filter. The exception is the importer's
`LegacyWordPressId` index, left unfiltered on purpose so one WordPress id still matches one row, deleted or not.

### Deleting releases the slug; a restore comes back as a draft

- **The slug is free immediately** (and, for the SKU-bearing types, the SKU is released through `ISkuRegistry` in
  the delete's own save). That is the point of the exercise: the mistake stops standing in the way of the record
  that replaces it.
- **A restore returns the record to `Draft`,** with `ScheduledFor` cleared and `PublishedAt` kept. Nothing goes back
  on the site by being restored; staff publish again deliberately. This also keeps a restore clear of the
  publish-time requirements (alt text, a body, a run's instructor) that may no longer hold, and `PublishedAt` is
  still what makes a later slug change owe a redirect (ADR 0035).
- **A restore whose slug was taken meanwhile is refused** with the existing `cms.slug_taken`, naming the address.
  Which of the two records keeps it is staff's decision, not ours.

### What a record names can still go missing, and publishing is where that is caught

The reference check above runs before the delete's save, so two windows remain open:

- **A concurrent write.** An exhibition crediting an artist can commit between the check and the delete. Accepted:
  the delete is Admin-only, the window is one request wide, and nothing a visitor sees breaks.
- **A reference from a record that was itself deleted.** Deleting an artist is allowed while only a *deleted*
  exhibition credits them — that is the point of the filter. Restoring that exhibition then brings back a credit
  to an artist who is gone.

Neither reaches a visitor: a credit is resolved through the other module's filtered read, so a missing artist is
simply not named, exactly as an unpublished one is not linked (ADR 0042). A restore comes back as a Draft, which
the site never renders, so the state is always looked at before it can go live.

**Where it is not acceptable, publishing refuses it.** A class run must have an instructor to publish (ADR 0044),
so `ClassesBusiness.PublishRequirementError` now also refuses a run whose instructor no longer exists — "Run
CLS-1-1's instructor has been deleted. Choose another, or restore them." The facade gathers that answer in the
same read it already does for the hero image, on publish, on schedule and in the Worker's scheduled publish, which
is the pattern the hero's own `HeroExists` check established. Optional credits (a piece's artist, an instructor's
linked artist, an exhibition's artists) are left to degrade: an absent credit is a correct rendering of "nobody is
credited", not a broken page.

### A deleted record keeps hold of its pictures

Media's delete guard walks the EF model for every `Restrict` foreign key to `MediaAsset` (ADR 0030), and it now
does so with `IgnoreQueryFilters()`. This is forced, not chosen: the guard stands in for a database constraint, and
the database has not forgotten the deleted row. Filtered, the guard would answer "nothing references it", the
delete would then break on the foreign key, the re-check would agree with the guard, and `media.in_use` would reach
staff as a 500.

So an image a deleted record points at still cannot be deleted. Accepted, with the way out written down: restore
the record, clear the image, delete it again. (Pages are unaffected in practice — ADR 0045 keeps pictures out of
bodies — but hero images and galleries make it real for the types that follow.)

### A referenced record is refused, not quietly unlinked

`cms.in_use` (409) joins `ContentErrorCodes`: an exhibition credits the artist, a class run names the instructor,
so that record cannot be deleted until the link is gone. The alternative — delete it and drop the join rows — was
rejected because a published exhibition would silently lose a credit and a class run would lose the instructor
publishing requires. It mirrors `media.in_use`, and the `Restrict` keys of ADR 0033 already point this way.

**Who still names a record is answered by the module that holds the reference**, not by the module being deleted
from: Artists may not ask Exhibitions anything (the dependency runs the other way). So the kernel declares
`IContentReferenceSource`, each referencing module registers one with `TryAddEnumerable` exactly as it registers
its `IScheduledPublisher`, and the kernel's `IContentReferences` fans out over them. Today: Exhibitions (credits),
Shop (artwork's artist) and Instructors (a linked artist) answer for `artist`; Classes (a run's instructor) answers
for `instructor`. Nothing names a page, an exhibition, a piece or a plan, so those four types have no source to
consult.

Each source asks through **its own module's DataLayer**, like everything else that reads from it, and adds no rule
of its own. (It first called the repository directly; the architecture review was right that this skips a layer,
and it was changed.) The **facade** asks the kernel, not Business — a cross-module question belongs there, beside
the facade's existing questions to Media (backend.md), and it hands Business a `ContentReferenceCheck` the way it
hands it an `ArtistPortraitCheck`.
Each source's query starts at its own soft-deletable entity, so the query filter settles the awkward case for free:
a credit on an exhibition that is itself deleted holds nothing up. The refusal names up to five records
("“Queue” still names this artist. Take it off that record first, then delete it."), so staff know where to go.

### The admin

**One Deleted view, not seven.** `shared/deleted-records/` renders the rows, the states, the confirm and the
restore for every type; each list screen passes a `DeletedRecordsSource` adapting its own service (and its own
`name`/`title` field) to the shared row. The alternative — threading a deleted mode through seven bespoke list
components — would have been seven places to fix the same bug.

Delete is an Admin-only action in the editor, apart from the publishing buttons and behind the confirm dialog,
whose wording says what will happen: the address stops working, the unsaved edits go too, the address is free at
once, and an Admin can restore it as a draft. Unlike the status commands it is offered while the form is dirty —
asking someone to save a page before throwing it away would be absurd.

The Deleted view is a filter beside All / Drafts / Published / Archived, shown only to Admins, with **Restore** per
row and no link to an editor (there is no editor for a record nothing else can see). It is read in the same
`(Title, Id)` order as the main list, on the same cursor machinery under its own scope, rather than newest-deleted
first: it is searched by name far more often than it is browsed by date, and the deleted date is in the row.

## Options considered

| Option | Cost |
|---|---|
| **Soft delete + restore** (chosen) | A new EF pattern to hold correctly, and a column pair and migration per type |
| Soft delete + a permanent purge | Everything above, plus a purge that has to cope with audit rows, media references and (from 5.x) Square links. No need for it yet |
| Hard delete with guards, like media | Smallest change, clean semantics — and a mistake is unrecoverable and the record leaves the history. Rejected against "created by mistake and need cleaning up": the same slip deletes the wrong thing |
| A `Deleted = 40` `PublishStatus` | Smallest diff of all. Loses what the record was, widens every status filter in the contract and the admin, and mislabels deletion as a lifecycle step |

## Consequences

- **Easier:** staff clean up mistakes, and "deleted" needs no interpretation — a deleted record is absent from the
  CMS, the site, the sitemap, previews and the scheduled publisher by construction, not by remembering to filter.
- **Harder:** a query that genuinely needs deleted rows has to say so, and anyone adding one to a soft-deletable
  type has to know the filter is there. The table above is the list; a new entry belongs in it, with a test.
- **We must not:** add a unique index to a soft-deletable table without `SoftDelete.LiveRowsOnly`; write a read that
  looks past the filter without a line in the table above; or let a restore return a record straight to the site.
- **A released SKU can be reused at once**, including the number the CMS suggests (`ART-<n>`, `CLS-<offering>-<n>`),
  because the suggestion counts live rows. That is intended: the deleted record is holding nothing. The restore
  that then meets its own SKU is refused with `cms.sku_taken`, which `ShopModuleTests` holds.
- **DECIDE — Square and a deleted record.** This is the one open question this ADR leaves. A delete releases the
  SKU locally and tells Square nothing: a `ShopItem`, a `ClassRun` or a `MembershipPlan` that Square holds stays
  sellable there, and a plan with live subscribers would keep billing. The delete will stage
  `square.catalog-delete-requested` when 5.x lands, and square.md already requires an ADR for the
  archive-versus-delete policy that message carries; **that ADR must also say whether a plan Square has
  subscribers for may be deleted at all.** Until then, deleting a Square-held record is a local act only, and
  staff retire it in Square themselves.
- **Still to come:** the importer's per-type legacy-id lookups (4.x) must read past the filter as the placeholder
  seed now does — the index is in place, the caller is not yet written.
