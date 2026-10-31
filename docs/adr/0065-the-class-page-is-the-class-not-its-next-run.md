# 0065 — The class page is the class, not its next run

- **Status:** Accepted
- **Date:** 2026-10-31
- **Rules affected:** `.claude/rules/frontend.md`, `.claude/rules/content.md`
- **Builds on:** [0042](0042-site-read-api-and-its-cache.md) (the read this page needs, which already existed),
  [0044](0044-class-runs-with-their-own-instructor-and-seats.md) (runs and sessions),
  [0045](0045-one-slim-rich-text-allow-list.md) (the "More details" body),
  [0063](0063-the-sites-preview-page.md) (the preview page), [0064](0064-exhibitions-get-a-page-of-their-own.md)
  (the same bug, found first on exhibitions)

## Context

`/classes/{slug}` was built from the spike's data, where a class *was* a dated event. It read the whole calendar
(`/api/v1/site/classes`), found the first entry with a matching slug and rendered that **run** — so the page was a
run wearing the class's name. Three things follow from that, and all three are wrong:

- **A published class answers 404 on its own address once its last session ends.** The calendar read only carries
  runs still to come, so a class between terms is absent from it, and the page falls through to the site's
  not-found. `ClassesBusiness.ListSitemapAsync` keeps naming every published class in the sitemap, so the site
  advertises pages that 404 — exactly the bug ADR 0064 found on exhibitions, on a type that changes far more often.
- **A class's other dates were hidden.** `toArtClasses` flattens a class into one entry per run, so a class with
  two terms appears twice in the calendar, both entries link to the same address, and the page shows whichever is
  soonest. The second date existed and was unreachable.
- **The "More details" body was never rendered.** Staff write sanitized HTML per class (ADR 0045) and no page on
  the site read it.

It surfaced through previews (ADR 0063). A draft class has no dates yet — dates, instructor and price are required
at publish, not before — so previewing one showed the not-found page, and 0063 papered over it with a message
saying which dates were missing. That message was treating a page defect as a content state. The client's
acceptance test for the preview story is that a previewed record renders **as its page**, which this page could not
do for a large share of real classes.

`GET /api/v1/site/classes/{slug}` has been there all along, returns `SiteClassServiceModel` with `BodyHtml`, and
documents its `Runs` as "the runs that have not ended yet… **empty once the class is over**". Nothing called it.

## Decision

1. **The page reads the class, not the calendar.** `SiteApi.classPage(slug)` calls `/site/classes/{slug}` and
   answers three ways, as the exhibition read does (ADR 0064): the class, `undefined` for no such published class,
   `null` for a failed read — which renders a "try again shortly" band while the server answers 503, never a 404.
2. **`ClassPage` is the class and its dates**: slug, title, summary, `body` (the sanitized "More details"), type,
   level, image, and `runs` — every run still to come, soonest first, mapped by the same `toArtClasses` the
   calendar uses, so a run renders identically wherever it appears.
3. **The soonest run still leads the page.** The eyebrow, the schedule, the instructor, the seats, the price and
   the reserve button are that run's, exactly as before, so a class with something on the calendar looks the way it
   looked. What changes is what happens when there is **no** such run: the copy stands, the facts say "No dates on
   the calendar just now", and the buy block becomes a line pointing at `/classes`.
4. **A class's other dates are listed**, in a band above "More coming up", as the cards the calendar already uses.
   The bands keep alternating, so a class with one date renders the same shades it did.
5. **The body is rendered** where staff meant it: under the lead, in the detail column, through `[innerHTML]` —
   the HTML is sanitized on write (ADR 0045), as the CMS page's body already is.
6. **A preview seeds `classPage(slug)`, keyed on the draft's slug**, and leaves `classes()` to the real API so the
   calendar below is the live one. ADR 0063's "class with no dated run" special case is **removed**: there is no
   such thing any more, because the page renders the class.
7. **JSON-LD only when there is a date.** `eventLd` describes an event, and a class with nothing on the calendar is
   not one; the page still carries its title, description, canonical and image.

## Options considered

- **Keep the page run-shaped and let the preview explain the gap** (what ADR 0063 shipped). Rejected: it left the
  live 404s and the hidden dates in place, and a preview that explains why it cannot show the page is not a
  preview.
- **Have the preview fabricate a run so the page renders.** Rejected: inventing dates on a draft is a lie, and the
  page would show a date staff never set.
- **Keep reading the calendar and add a second read for the body.** Rejected: the calendar read is the reason the
  page 404s between terms; one read that answers for the class fixes both.
- **A "notify me" form for a class with no dates.** Out of scope here — the forms module owns that, and it needs
  its own prompt. The page points at `/classes` for now.

## Consequences

- **A published class keeps its address between terms**, so the sitemap stops advertising 404s for the type that
  turns over most often, and a link in an email or on Instagram survives the end of a term.
- **Every previewable type now previews as its real page, with no exceptions** — which is the acceptance test for
  ADR 0063's story. `preview.spec.ts` compares the previewed class's markup against what `/classes/:slug` renders,
  markup for markup, as it does for an exhibition.
- **A class with two terms shows both**, and the "More coming up" band moves down the page when it does.
- **The class body appears on the site for the first time.** Classes imported from WordPress carry bodies, so this
  shows copy that has been written but never seen — worth a read-through before launch.
- **`product()` still collapses "missing" and "failed"**, the flaw ADR 0064 named. The shop page has the same 404
  risk on a blip, and fixing it is a change to the shop's failure behaviour, so it keeps its own prompt.
