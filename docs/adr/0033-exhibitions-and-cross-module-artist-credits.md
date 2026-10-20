# 0033 — Exhibitions: artist credits facade to facade, calendar dates and UTC reception times, the content seed in MigrationService

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/content.md`
- **Builds on:** [0004](0004-module-first-domain-and-seam.md) (facade to facade),
  [0030](0030-media-library-compressed-masters-and-renditions.md) (`Restrict` foreign keys to `MediaAsset`),
  [0031](0031-artists-and-the-shared-content-lifecycle.md) (the shared lifecycle, slug lock, deferred effects)

## Context

Prompt 2.5 builds `Exhibition`, the same slice as 2.4's `Artist`. It adds:

- a title, summary and body
- start and end dates
- an opening reception
- a hero image
- credited artists, many to many

It is the first content type that points at another content type. The prompt requires cross-module reads of artists
to go facade to facade.

## Decision

1. **Same lifecycle as Artists.** The slice uses ADR 0031's kernel unchanged:
   - `PublishLifecycle`, `Slugs`, `EntityTag` and `ContentErrorCodes`
   - publish, unpublish and archive with `If-Match`, archive `CmsAdmin` only, every transition audited
   - the slug locked from first publish, and no DELETE route

   The audit vocabulary is `exhibition.*`.
2. **Credits are a join table owned by Exhibitions.**
   - `ExhibitionArtists` has the columns `ExhibitionId`, `ArtistId` and `Position`, with the key
     `(ExhibitionId, ArtistId)`. Each artist is credited once, and the order is kept: the site lists headliners
     first.
   - Deleting an exhibition cascades to its credits. The `ArtistId` foreign key is `Restrict`, the one place a key
     crosses into Artists ("entity types, only where a foreign key already crosses"). Artists has no delete, so this
     only guards the future.
   - A PATCH's `artistIds` replaces the list. Business updates a kept credit in place rather than removing and
     re-adding it, which the key would refuse in one save. Any change to the credits rotates the exhibition's row
     version, so `If-Match` covers them.
3. **Artists vouches for artists, through its facade.** `IArtistsFacade` gains two read methods for other modules:
   - `GetSummariesAsync(ids)`
   - `FindBySlugsAsync(slugs)`

   Each takes at most `ArtistPolicy.LookupMaxCount` (200); more is the caller's bug and throws. On create and edit,
   `ExhibitionsFacade` refuses an id that isn't on the roster or is **Archived**, as a field error on `artistIds`.
   Draft artists may be credited. This check doesn't depend on the exhibition, so it lives in the facade, like a
   missing hero. Business never sees the Artists module.
   - **Reads are finished in the facade.** Business returns an internal `ExhibitionDetail` holding the credited ids.
     The facade asks Artists to describe them (name, slug, status) and builds the `ExhibitionServiceModel`. The list
     returns `artistCount` only, so it asks Artists nothing.
   - **Publish doesn't re-check the artists.** An artist archived after being credited stays credited and shows as
     Archived. Phase 3 decides what the site shows for it. The window between the facade's check and the save, in
     which an artist could be archived, is accepted: the foreign key keeps the data valid, and the credit is visible
     as Archived.
4. **What publishing needs.** A summary, since it is the card, the meta description and the search result
   (content.md: SEO fields are checked at publish). The hero's alt text too, if there is a hero (media.md, as for a
   portrait). Both problems come back in one `validation.failed`, so the editor fixes everything in one pass. A
   Published or Scheduled exhibition can't clear its summary, or take a hero without alt text.
5. **Dates and times.**
   - `StartDate` and `EndDate` are calendar dates (`DateOnly`, SQL `date`, `format: date` on the wire). A show runs
     on days at the gallery; there is no instant to convert. The end may equal the start, never precede it.
   - The reception is two UTC instants, `ReceptionStartsAt` and `ReceptionEndsAt`, set together with the end after
     the start, plus an optional note. Input may carry any offset and is stored as UTC; output is `+00:00`
     (api-contract.md). The response carries `displayTimeZone` (`GalleryTimeZone.Id`, `America/Chicago`, a new
     kernel constant), as class sessions will.
   - The admin types and shows times on the gallery's clock and converts with `Intl` and that explicit zone, never
     the browser's (`core/time/gallery-time.ts`). In the autumn hour that happens twice, the first one is meant; a
     spring time the clocks skip moves forward.
6. **The body is plain text** (up to 10,000 characters, line breaks normalised to `\n`) until the sanitizer arrives in
   2.6, as the artist bio is (ADR 0031 §4).
7. **No outbox message yet: content.md's dated exception now names Exhibitions too.** Nothing outside the CMS reads
   exhibitions before Phase 3, and 2.8 designed `site.revalidate-requested` (ADR 0036; ADR 0031 rejected a
   do-nothing handler). `ExhibitionsModuleTests.PublishStagesARevalidationInTheSameSave` was written and skipped;
   2.8 added the message to `ExhibitionsDataLayer.TransitionAsync` and unskipped it. The hero hand-off ADR 0031 §5
   describes for portraits is still open, and belongs to 3.1.
8. **The development seed credits roster artists through the Artists facade.**
   - `ExhibitionDevelopmentSeeder` seeds the spike's eight shows (Published, no hero, with the Boats & Pianos
     reception). It resolves the credited names to artists with `IArtistsFacade.FindBySlugsAsync`.
   - Four of the spike's names aren't on the roster, so they aren't credited. Archived artists aren't credited either.
   - It runs after the artist roster's seeder: seeders run in registration order. It never overwrites an existing
     slug.
   - **MigrationService now composes Media and Artists in Development.** `ArtistsFacade` depends on `IMediaFacade`,
     whose blob gateway needs the `blobs` client. So in Development MigrationService adds
     `AddAzureBlobServiceClient("blobs")`, `AddMediaModule` and `AddArtistsModule`, and the AppHost gives
     `migrations` a reference to `blobs` **in run mode only**. A published MigrationService seeds no content and gets
     no storage access.

## Options considered

- **Business checks the artists through an injected `IArtistsFacade`.** Rejected: Business calls only its DataLayer
  (backend.md).
- **The admin fetches artist names itself, and the API returns ids only.** Rejected: every client would repeat the
  join, and the prompt asks for cross-module reads facade to facade.
- **A second, Media-free Artists facade for lookups, so MigrationService needn't compose Media.** Rejected for now:
  a parallel boundary for one seed. Revisit if the Worker or the importer needs artist lookups without Media.
- **Seed exhibitions without credits.** Offered and declined in favour of credited seed data.
- **Reception as local wall-clock strings on the wire.** Rejected: api-contract.md fixes UTC `+00:00` plus
  `displayTimeZone`, and the site's JSON-LD needs instants.
- **Refuse publishing while a credited artist is archived.** Rejected: an artist's retirement would block an
  unrelated show, and the credit is history.

## Consequences

- `IArtistsFacade` has callers outside the API. Its lookups must stay cheap and Media-free in practice (they are one
  indexed query), even though DI composes Media.
- MigrationService's Development composition grows with each module whose seed reads another module's facade.
- 2.7 relaxed `cms.slug_locked` in Exhibitions as in Artists. 2.8 added revalidation to
  `ExhibitionsDataLayer.TransitionAsync` and removed the skip (ADR 0036); the hero hand-off is 3.1's.
- Phase 3's site exhibition pages read credits through the same facade path, and decide how a Draft or Archived
  credit shows.
