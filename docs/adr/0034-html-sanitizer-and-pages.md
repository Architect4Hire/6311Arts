# 0034 — The shared HTML sanitizer and Pages: HtmlSanitizer 9.2, a small allow-list, library-only images, Tiptap in the admin

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/content.md`
- **Builds on:** [0030](0030-media-library-compressed-masters-and-renditions.md) (`Restrict` keys to `MediaAsset`, the
  delete guard, `/media/{id}/{rendition}`), [0031](0031-artists-and-the-shared-content-lifecycle.md) (the shared
  lifecycle, dated effects), [0033](0033-exhibitions-and-cross-module-artist-credits.md)

## Context

Prompt 2.6 adds the shared HTML sanitizer and the `Page` type (slug, title, HTML body), and seeds placeholder
privacy and terms pages. content.md requires HTML to be sanitized on write by an allow-list, and scripts, styles
and inline handlers never to be stored. The sanitizer must have its own suite of known XSS payloads. The importer
(4.x) will run the same sanitizer last over WordPress bodies, after rewriting upload URLs to `/media/…`.

## Decision

1. **Library: Ganss.Xss HtmlSanitizer 9.2.1039** (MIT). Its dependencies are AngleSharp 1.7 and AngleSharp.Css (both
   MIT). It is mature, widely used and tested against the usual XSS and mXSS vectors. It sits behind our own
   `IHtmlSanitizer` in `Managers/Html/`, is registered by `AddArtsKernel`, and is referenced by the Domain only.
   **No paid or copyleft dependency.** The 10.x line is still beta, so it is not used.
2. **The allow-list is `HtmlPolicy`, and it is small.**
   - Elements: `p`, `h2`–`h4`, `ul`, `ol`, `li`, `strong`, `em`, `a`, `br`, `blockquote`, `img`.
   - Attributes: `href`, `src` and `alt`.
   - No CSS, classes, ids, `data-*`, `target` or `rel`.
   - Links: site-relative (`/…`, never `//…` or `/\…`), `#…`, or `http(s):`, `mailto:` and `tel:` with a host.
   - Images: a library rendition, `/media/{id}/{width}.{webp|jpg}` with an optional `?v=`, and nothing else.
   - The page title is the `h1`, so a body `h1` is unwrapped.
3. **Three passes.**
   - **Before HtmlSanitizer**, on our own AngleSharp parse: script, style, template, iframe, object, form, SVG, MathML
     and the like are dropped *with their contents*, and `b`/`i` become `strong`/`em`.
   - **HtmlSanitizer** then applies the allow-list, with `KeepChildNodes`. An unknown wrapper (`div`, `span`)
     loses its tag but keeps its words, which matters for pastes and imports. The pre-pass is what stops a script's
     code surviving as text.
   - **After**: links and images are filtered by the rules above. A link left without an address is unwrapped, an
     image without a source is removed, empty blocks are dropped, and library images are listed.

   **Before any of that, a nesting limit.** A body nested more than `HtmlPolicy.MaxNestingDepth` (100) levels deep
   is refused. The depth is estimated from the raw tags without parsing, and void and optional-end elements don't
   count, so unclosed `<p>`/`<li>` never add up. The security review found that 66,000 nested `<b>` (under the
   200,000-character input cap) overflowed the stack in the tree walks and killed the process. A
   `StackOverflowException` can't be caught, so the input must never reach a parser. Pages turns a refusal into a
   `validation.failed` on `body` instead of storing an emptied body.

   The image rule is anchored with `\A…\z` and accepts only Media's own rendition widths
   (`HtmlPolicy.MediaRenditionWidths`). A test pins those widths to `MediaPolicy.RenditionWidths` and round-trips
   `MediaUrls.For`. Each kept attribute stays only on its own element (`href` on `a`; `src` and `alt` on `img`).
   Removal descriptions use only name characters and are capped at 30, because tag and attribute names are the
   input's to choose. AngleSharp and AngleSharp.Css are pinned explicitly (1.7.2, 1.0.2): the Domain compiles
   against them, and the payload suite is the gate for upgrading any of the three.

   The output is deterministic and idempotent, and the tests check it. `SanitizedHtml.Removed` describes what was
   taken out, in words. Pages records it in the audit summary, and the importer will put it in its report.
4. **Business sanitizes, on every write path.** Create, edit and the seed all go through it, so a stored body is
   only ever the sanitizer's output. The facade runs the same pass beforehand only to learn which images the body
   shows, so that it can ask Media. Business sanitizes again and doesn't trust the facade's copy.
5. **Images in bodies (your decision):**
   - Every `<img>` must be a library original that Media knows (facade to facade, `CheckAttachableAsync`), and must
     carry its own non-empty `alt`. The `alt` is required whatever the page's status, because it is what the site
     renders. Media's own alt-text field is not consulted for body images.
   - `PageMediaReferences (PageId, MediaAssetId)` has a `Restrict` key to `MediaAsset` and cascades from the page.
     It is kept in step with the body on every write, so Media refuses to delete an image a page shows (ADR 0030's
     guard finds the key by reflection).
   - Because of that reference, publishing asks Media nothing.
   - An image deleted in the instant between Media's check and the save fails the save on the key (500, data
     intact), as for portraits (ADR 0031).
6. **Pages follow 2.4's slice.**
   - Lifecycle, slug lock, `If-Match` on every write and command (428 first), archive admin-only, no DELETE route.
   - A body is required to publish, and once a page has left Draft (published, scheduled or archived) it can't clear its body.
   - **An untouched placeholder can't be published.** A body still equal to a seeded placeholder is refused, so a
     one-click publish of "This privacy policy has not been written yet" can't happen.
   - The body is capped at 100,000 characters *after* sanitizing; the raw input is capped at 200,000.
   - **No outbox message yet:** content.md's dated exception now names Pages too (2.8 adds
     `site.revalidate-requested`). A skipped placeholder test holds the place.
7. **Placeholder pages are seeded in every environment, as Drafts (your decision).**
   - MigrationService runs `PagePlaceholderSeeder` outside the Development block. It seeds `privacy` ("Privacy
     Policy") and `terms` ("Terms and Conditions"), matching the site's `/privacy` and `/terms` and its footer.
   - Each gets placeholder copy pointing at `info@6311arts.com`.
   - It is idempotent by slug and never overwrites. `UpdatedBy` is `system:placeholder-seed`, so the importer can
     tell an untouched placeholder from staff work.
   - Drafts, so a legal placeholder can't go public by accident. The seed composes only Pages' Business and below:
     no Media and no facade.
8. **The admin edits bodies with Tiptap 3** (`@tiptap/core`, `@tiptap/pm`, `@tiptap/starter-kit` and
   `@tiptap/extension-image`, pinned exactly to 3.31.4, MIT; 3.32.0 was a week old).
   - The schema is cut down to the allow-list: no code, strike, underline or horizontal rule; headings 2–4; links
     with no `target` or `rel`; images inserted from the media picker with their alt text.
   - So staff can't make what the server would strip, and the editor doesn't need a "something was removed" notice.
     The audit records any removal.
9. **Out of scope.** The artist bio and the exhibition body stay plain text (ADR 0031 §4, 0033 §6) until a prompt
   converts them. Site reads of pages are Phase 3.

## Options considered

- **Our own allow-list walker over AngleSharp.** Rejected: we would own every edge case (URL normalisation,
  serialization, mXSS) that HtmlSanitizer already handles. Offered and declined.
- **A contenteditable editor of our own, or an HTML textarea.** Rejected: fiddly to make robust and accessible, or
  unfriendly for staff.
- **No images in bodies until the importer.** Offered and declined in favour of library images with references now.
- **Seed placeholders in Development only, or Published everywhere.** Declined: production needs the records, and a
  published legal placeholder is a liability.
- **Rely on Media's alt text for body images.** Rejected: the HTML `alt` is what the site renders, so it is what
  must exist.

## Consequences

- Every new HTML field goes through `IHtmlSanitizer` in its Business, and every picture in it needs a `Restrict`
  reference row. Widening `HtmlPolicy` needs an ADR, and the sanitizer suite's payloads extended to match.
- Upgrading HtmlSanitizer or AngleSharp re-runs the payload suite, which is part of `dotnet test`.
- The importer (4.x) must rewrite WordPress upload URLs to library renditions *before* sanitizing, or its images are
  removed (and reported).
- 2.8 adds revalidation to `PagesDataLayer.TransitionAsync` and removes the skip. 2.7 relaxes the slug lock.
