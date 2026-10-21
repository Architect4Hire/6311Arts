# 0044 — Class runs: one class, many terms, each with its own instructor and seats

- **Status:** Accepted
- **Date:** 2026-10-21
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/square.md`, `.claude/rules/backend.md`,
  `.claude/rules/api-contract.md`
- **Amends:** [0037](0037-classes-and-the-shared-catalog-sku-registry.md) (the SKU, the projections and the starting
  price move from the session to the run)
- **Builds on:** [0032](0032-website-items-managed-from-the-cms.md) (CMS-managed classes),
  [0042](0042-site-read-api-and-its-cache.md) (site reads), [0043](0043-instructors-as-their-own-content-type.md)
  (instructors)

## Context

ADR 0037 modelled a class as an offering with sessions, and made **each session its own Square variation**, with its
own SKU, price and seat count. Two things did not fit the centre:

- **The same class runs again, over different periods, with different teachers.** The spike lists "Intro to Screen
  Printing" (Saturdays, Gwen) and "Intro to Screen Printing (Sunday mornings)" (Nicholas) as two classes because it
  had nowhere else to put a second run.
- **A visitor buys a seat in the whole course, not in one evening of it.** The spike's card says "5-week course,
  $300, 4 spots left, with Gwen": one price and one seat count for a run of sittings.

## Decision

The aggregate is **offering → runs → sessions**, still one `ETag`, still replaced as a set by `PATCH`.

| | Offering | **`ClassRun`** (new) | `ClassSession` |
|---|---|---|---|
| Holds | title, slug, summary, body, type, level, hero, `OfferingNumber` | label ("Fall 2026"), **instructor** (FK to Instructors, `Restrict`), cadence, display time zone, **SKU**, **capacity**, **starting price**, projections `PriceCents` / `SeatsRemaining` / `SoldOut` | start and end only |
| Square | one `ITEM` (5.9) | one `ITEM_VARIATION` | nothing |

- **The run is what is sold.** Its SKU is registered in `CatalogSkus` as owner type `class-run` (replacing
  `class-session`) and suggested as `CLS-<offering>-<n>`, numbered in the order the runs start.
- **Freezing is per run.** `startingPriceEditable` and `squareHasRecord` are read from the run's own projections. A
  run Square holds cannot be removed; its starting price may be left out or sent unchanged but not changed (that is
  5.4a's command); its capacity may not drop below the seats already taken.
- **The instructor is a lookup.** The facade asks `IInstructorsFacade` about every instructor a write names
  (`ClassInstructorCheck`); Business refuses one that does not exist, and refuses an archived one only for a run that
  did not already have them, so a class whose teacher has since retired can still be edited. CMS answers are finished
  with each run's instructor as Instructors describes them now (`ClassDetail`).
- **Publishing requires** a summary, at least one run, and for every run an instructor, a session and a starting
  price (or a Square price), plus the hero's alt text. All problems are reported at once, keyed to `runs`.
- **Sessions keep their ids** when sent back, so an edit moves a sitting rather than replacing it.
- **Site reads** (`/site/classes`, `/site/classes/{slug}`) return each published class with its **upcoming runs**
  (a run is upcoming until its last session ends). A run carries its instructor credit (slug only while their page is
  published), cadence, zone, first start, last end, session count, every session, capacity and Square's price and
  seats. `fromPriceCents` is the lowest Square price among upcoming runs. `?instructor={slug}` narrows the list to the
  runs one published instructor teaches; any other slug is an empty list and is not cached. Every class read is tagged
  `class-offering` and `instructor`.
- **The CMS list** names each class's instructors (one lookup per page) and counts its runs.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Run as the sellable unit, sessions as times (chosen)** | A third level in the aggregate. Matches what is bought, gives Square one variation per term, and makes "same class, different teacher" one offering. |
| Keep each session sellable; runs only group sessions and name the instructor | Smaller change to 0037, but a five-week course becomes five products with five seat counts that can disagree, and the site still has to invent a course price. |
| A separate offering per term | No schema change, but the class's copy, hero and page are duplicated per term, and "Intro to Screen Printing" has as many URLs as it has terms. |

## Consequences

- **The CMS class contract breaks** (api-contract.md "Compatibility"): `ClassServiceModel`, `ClassSummaryServiceModel`,
  `ClassSessionServiceModel` and the class write models lose their session-level and offering-level price, instructor
  and cadence fields and gain `runs`. It stays `/api/v1/` rather than moving to a v2, because v1 has not been
  released: its only consumer is the in-repo admin, updated in the same change. Unknown members are ignored by the
  API, so an old-shape body is not refused — it simply changes nothing it named. **DECIDE:** whether
  `api-contract.md` should state that pre-release exemption in its Compatibility section.
- **Migration `AddClassRuns` cannot carry existing session rows across** (their SKUs and projections move to a table
  that does not exist yet, and the session's foreign key changes target). There is no imported or production class
  data yet, so local databases are reset rather than the generated migration being hand-edited.
- 5.4a's class price change is per run (`/classes/{id}/runs/{rid}/price-changes`); 5.9 pushes a variation per run and
  adjusts a run's seats; 5.15's roster is per run. The prompts in `docs/prompts/scrub-prompts.md` say so.
- `backend.md`'s "class sessions store the display time zone" now reads "class runs".
- 3.2 maps each upcoming run to one of the spike's `ArtClass` cards (`start`, `sessions`, `price`, `spotsLeft`,
  `instructor`), and the instructor page lists `/site/classes?instructor={slug}`.
