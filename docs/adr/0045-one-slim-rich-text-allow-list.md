# 0045 — One slim rich-text allow-list: one heading level, no links, no pictures

- **Status:** Accepted
- **Date:** 2026-10-21
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/import.md`
- **Amends:** [0034](0034-html-sanitizer-and-pages.md) (the allow-list and the editor's toolbar),
  [0037](0037-classes-and-the-shared-catalog-sku-registry.md) (the class body, now labelled "More details")

## Context

ADR 0034 gave every HTML field one allow-list and one Tiptap editor: paragraphs, headings 2–4, bold, italic, lists,
quotes, links and library pictures. Pages, class bodies (ADR 0037) and shop item descriptions (ADR 0038) all use it.

In review, the owner found the editor heavier than the content needs. A class has a summary on the classes page
and its longer text on its own page. That text needs paragraphs, a list ("What to bring") and some emphasis, and
nothing more. Every record that shows a picture already has one: the hero on a class, the gallery on a shop item.
Pictures inside the text were a second, less controlled way to do the same thing, with their own alt-text and
reference-row rules. Links raised similar questions about where staff would send visitors and what the site would
style. The privacy and terms pages still need sections.

## Decision

1. **One allow-list for every HTML field, the same everywhere:** `p`, `h2`, `ul`, `ol`, `li`, `strong`, `em`, `br`,
   and no attributes. `HtmlPolicy` holds it, as before.
   - **One heading level.** `h1` and `h3`–`h6` are renamed to `h2` in the sanitizer's first pass, so an imported
     section break stays a heading rather than running into the text around it.
   - **No links at this time.** An `a` keeps its words; the audit says "a link (its text kept)".
   - **No pictures at this time.** An `img` is removed, even a described library rendition; the audit says "a
     picture".
   - **No quotes.** A `blockquote` loses its tag and keeps its paragraphs.
2. **The admin's `<cms-rich-text>` matches it:** Text, Heading, Bold, Italic, Bullets, Numbers, Undo, Redo. No link
   or picture panel and no media picker. StarterKit runs without the link mark or the blockquote node, and the Image
   extension is gone (the `@tiptap/extension-image` package is removed). A pasted link keeps its words and a pasted
   picture is dropped, as the server would drop them.
3. **The class body is labelled "More details"** in the admin and in its messages. The field and the API keep the
   name `body`. The site will show it on the class page below the summary when the page is wired to the site read API.
4. **Picture support stays in place, unused.** The `PageMediaReferences`, `ClassOfferingMediaReferences` and
   `ShopItemMediaReferences` tables, the facades' Media checks on body pictures, Business's alt-text rule and
   `HtmlPolicy.IsAllowedLink` / `TryParseMediaImage` all remain. With no `img` surviving the sanitizer, they see
   nothing. Allowing pictures or links again means adding the tags and attributes back to `HtmlPolicy`, with an ADR,
   and restoring the tests that exercised those rules (see the history of `PagesModuleTests` and `HtmlSanitizerTests`
   before this ADR).
5. **An untouched placeholder is recognised under either list.** `PagesBusiness.IsPlaceholder` compares the stored
   body and the placeholder *after* sanitizing both, so a placeholder seeded with its old `mailto:` link is still
   refused at publish. The placeholders themselves now carry the address as plain text.

Existing rows keep any links or pictures until they are next saved. The import has not run, so no backfill is needed.

## Options considered

- **A plain-text field (blank lines make paragraphs).** Simplest to edit and render, but it loses lists, and the
  importer would have to flatten WordPress HTML. Declined.
- **A per-field allow-list (a full one for Pages, a slim one for classes and shop items).** Rejected by the owner:
  one set of rules everywhere is easier to explain to staff and to style on the site.
- **Remove the picture plumbing and drop the reference tables now.** Cleaner, but it is a migration and a wider
  change for something the owner said is off "at this time". Deferred.
- **No headings at all.** Rejected: the privacy and terms pages need sections. One level is enough.

## Consequences

- The site's prose style needs only `p`, `h2`, lists, `strong` and `em`.
- Upload-URL rewriting before sanitizing (import.md) no longer matters for bodies: every picture is removed. The
  importer reports what the sanitizer removed, as before.
- Widening the list again (links, pictures, more heading levels) needs an ADR, the sanitizer suite's payloads
  extended, and the editor's toolbar changed to match.
- The unused picture support has no tests of its own while pictures are off. Whoever turns pictures back on owns
  restoring those tests.
