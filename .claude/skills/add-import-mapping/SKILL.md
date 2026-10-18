---
name: add-import-mapping
description: Add or change how one WordPress/WooCommerce source type maps into a 6311arts CMS type in Arts6311.Importer — mapping table, pure mapper with golden tests from the snapshot, staged load through the module facade, and report lines.
---

# Add an Import Mapping

Read `.claude/rules/import.md` and `.claude/rules/content.md` first.

## 1. Mapping table (in the plan, approved before code)

| WordPress source (endpoint · field) | CMS field | Transform | When missing |
|---|---|---|---|
| … | … | … | default / skip / `NeedsReview` |

Name the source endpoint(s) and the snapshot file pattern. Mark every field as **content**, **starting value** (price, stock, capacity — used once for Square), or **dropped** (with a reason).

## 2. Pure mapper

- `Map(SourceRecord, ImportContext) → MappedRecord | MappingProblem`
- It's **deterministic and does no I/O.** `ImportContext` supplies the media map, the artist map and the time zone (`America/Chicago`).
- **HTML bodies:** rewrite upload URLs through `ImportMediaMap`, strip shortcodes and builder markup (collecting them as warnings), then sanitize.
- **Dates:** WordPress or FooEvents local times are converted to UTC, keeping the display zone. Ambiguous or missing dates give `NeedsReview`, never a guess.

## 3. Golden tests from real data

- Copy 3–5 representative records from a snapshot into `src/Arts6311.Tests/Import/Fixtures/<type>/`, with personal data scrubbed.
- Assert the exact mapped output: one normal record, one with missing fields, one with HTML or shortcodes, and one edge case (sold out, multi-session, no image).

## 4. Load step

- Upsert through the **module facade** with the `LegacyWordPressId`. The result is recorded on the `ImportItem` (status, target id, error code).
- Never write `DbContext` across modules. Never publish. Never call Square.

## 5. Report lines

Add counts and problem lines for this type to the import report.

## Checklist before done

- [ ] Mapping table approved; every source field classified
- [ ] Pure mapper with golden tests (normal, missing, HTML, edge)
- [ ] Load via facade, idempotent on `LegacyWordPressId`, staff edits → `NeedsReview`
- [ ] Starting values go only to starting fields; no Square-owned projections touched
- [ ] Report lines added; `@architecture-reviewer` run
