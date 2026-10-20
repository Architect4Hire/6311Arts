# 0031 — Artists and the shared content lifecycle: status commands, slug lock, row-version tags, deferred effects

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/backend.md`, `.claude/rules/api-contract.md`,
  `.claude/rules/frontend.md`
- **Builds on:** [0004](0004-module-first-domain-and-seam.md) (seam; facade-to-facade),
  [0021](0021-api-host-conventions-and-internal-token-verification.md) (error-code map),
  [0030](0030-media-library-compressed-masters-and-renditions.md) (Restrict foreign keys to `MediaAsset`;
  `CheckAttachableAsync`; "Publishing an image" stays **DECIDE**, now owned by 2.8)

## Context

Prompt 2.4 builds the first CMS content type, `Artist`, with `add-content-entity` and `add-endpoint`. The rules
it follows assume pieces that arrive in later prompts:

- `site.revalidate-requested` and the scheduled publisher arrived in 2.8 (ADR 0036).
- The HTML sanitizer arrives in 2.6.
- Redirects, which a slug change after publish creates, arrive in 2.7.
- Site reads of artists arrive in 3.1.

Five more content types (2.5–2.11) will copy whatever this slice does, so the lifecycle has to live somewhere
they can share.

## Decision

1. **A shared publishing kernel.** `Managers/Publishing/` holds:
   - `PublishStatus` (Draft 0, Scheduled 10, Published 20, Archived 30; sent as names)
   - `PublishCommand` and the transition table `PublishLifecycle.Next`
   - `Slugs` (`FromName`, matching the spike's `slugify`, and `IsValid`)
   - `ContentErrorCodes`

   `Managers/Concurrency/EntityTag` is Media's `MediaEtag`, moved unchanged, plus a `Verify` that every editable
   record uses. A missing `If-Match` gets a 428, a malformed one a 400, and a stale one a 409.
   `EntityTag.Precondition` is the part that needs no database. A facade runs it **before** body validation or any
   cross-module call, so a write without `If-Match` is always a 428, whatever else is wrong with it.
2. **Transitions.** The allowed moves are:

   | Command | From | To |
   |---|---|---|
   | `publish` | Draft or Scheduled | Published |
   | `unpublish` | Published | Draft |
   | `archive` | Draft, Scheduled or Published | Archived |

   Anything else is refused with `cms.status_transition_invalid` (409), so a command never silently does
   nothing. Archived is final until a prompt needs unarchive. Status commands carry `If-Match` too, so nobody
   publishes a version they haven't seen. Every transition is audited. Archive is `CmsAdmin` at the route.
3. **`PublishedAt` is the first publish, and it locks the slug.** It survives an unpublish, because a URL that was
   public is still someone's bookmark. Until 2.7, a slug change on a record that has ever been published is
   refused with `cms.slug_locked` (409). From 2.7 the change goes through and creates a `Redirect` instead.
   `cms.slug_taken` (409) covers uniqueness: Business checks first, and the DataLayer maps a lost race on the
   unique index to the same code.
4. **The bio is plain text** (up to 5,000 characters, with line breaks normalised to `\n`) until the sanitizer
   lands in 2.6. Storing HTML before there is anything to sanitize it would break `content.md`.
5. **No outbox message in 2.4.** Nothing outside the CMS reads or caches artists before 3.1, so a commit has no
   effect to pass on. 2.8 added `site.revalidate-requested` to `ArtistsDataLayer.TransitionAsync` (ADR 0036). It
   left ADR 0030's "Publishing an image" open, now owned by 3.1; the recommendation stands (option 1:
   `media.publish-requested` staged by publish). Until then a published artist's portrait stays `Draft` in Media,
   which no anonymous request can see before Phase 3.
6. **The portrait goes through Media's facade.** Artists declares a `Restrict` foreign key to `MediaAsset.Id`, so
   Media's delete guard finds it without naming the module.
   - On create and edit, `ArtistsFacade` asks `IMediaFacade.CheckAttachableAsync`. A missing original is a field
     error on `portraitMediaId`. On an edit the answer goes on to Business, which decides whether alt text
     matters: a Draft may take an image without alt text, but a Published or Scheduled artist may not
     (`media.md`: no image without alt text on published content).
   - On publish, the facade reads the artist's portrait id through Business, asks Media, and passes the answer
     (`ArtistPortraitCheck`) to the publish. That is two Business calls in one facade operation. It is safe
     against **artist-side** changes because the publish is checked against the editor's `If-Match`: a portrait
     change in between moves the row version, and the publish is refused. Business also confirms the check is for
     the artist's current portrait.
   - **Residual window, accepted.** `If-Match` does not cover **Media-side** changes. Between Media's answer and
     the artist's save, someone could clear the portrait's alt text, or delete the image. A deleted image fails the
     save on the foreign key (500, data intact). Cleared alt text publishes the artist with an image that has lost
     its alt text. Both need two staff acting on the same image within milliseconds. Closing the window would need
     Media to refuse clearing alt text on an image used by published content, which belongs with the
     image-publishing hand-off, which 3.1 now owns (ADR 0036).
7. **No DELETE route.** The prompt's routes don't include one, and archive is how an artist is retired.
8. **Development seed.** `ArtistDevelopmentSeeder` (MigrationService, Development only) hands the spike's 51
   artists to `IArtistsBusiness.SeedAsync`. Business decides what is seeded (Published, no portraits, only slugs
   not in use), and the DataLayer only adds the rows. It is idempotent and never overwrites an edit.
   `UpdatedBy` is `system:development-seed`, so the importer's "edited by staff since?" check can tell seeded rows
   from staff ones. Elsewhere, artists come from the WordPress import.

## Options considered

- **Lifecycle per module rather than a kernel.** Rejected: five more types would copy it, and they would drift.
- **Treat `publish` on a Published record as a no-op 200.** Rejected: with `If-Match` required, a retry after
  success is already a 409, and a refusal is honest about what the editor's screen is showing.
- **Allow slug changes after publish now and add the redirect in 2.7.** Rejected: links would break in the
  meantime.
- **Check the portrait in Business through an injected Media facade.** Rejected: Business calls only its
  DataLayer (backend.md), and the layer scan would rightly flag it.
- **Stage `site.revalidate-requested` now with a no-op handler.** Rejected: it would be a message type with
  nothing to do, created ahead of the prompt that designs it.

## Consequences

- 2.5–2.11 reuse `PublishLifecycle`, `Slugs`, `EntityTag.Verify` and `ContentErrorCodes`. Each still writes its
  own Business rules and audit vocabulary.
- 2.7 must relax `cms.slug_locked` to "create a Redirect" in Artists, and in each type built by then.
- 2.8 added revalidation to the Artists DataLayer's transitions and unskipped
  `ArtistsModuleTests.PublishStagesARevalidationInTheSameSave` (ADR 0036). The portrait hand-off is still open and
  belongs to 3.1.
- `Bio` is `nvarchar(max)` in SQL Server (5,000 is beyond `nvarchar`'s 4,000 limit), with the length enforced by
  the validator.
- A portrait deleted in the instant between the facade's check and the save fails the save on the foreign key and
  answers 500. That is rare, and the data is safe; a dedicated mapping can wait until it is seen.
