# 0050 — Component markup and styles live in sibling files

- **Status:** Accepted
- **Date:** 2026-10-21
- **Rules affected:** `.claude/rules/frontend.md`, `.claude/rules/design-system.md`,
  `.claude/skills/new-component/SKILL.md`
- **Amended by:** [0051](0051-one-component-per-folder.md) (the multi-component naming clause below is withdrawn)
- **Builds on:** [0025](0025-ui-library-tokens-and-primitives.md), [0027](0027-admin-shell-and-staff-sign-in-page.md)

## Context

Every component in `projects/site`, `projects/admin` and `projects/ui` was written as one `.ts` file holding its
markup in `template:` and its styles in `styles:`. The editors had grown past the point where that works:
`exhibition-editor.ts` was 1385 lines, `membership-plan-editor.ts` 1257, `shop-item-editor.ts` 1293. In a file
that size the class — the part with the logic worth reviewing — is a few dozen lines buried under a 15 kB string.

The forces:

- **Tooling treats a template literal as a string.** Editor HTML tooling, Emmet, tag matching and element folding
  work in an `.html` file and not inside a backtick. Angular's language service covers inline templates, but the
  surrounding editor affordances don't.
- **Prettier was already configured for it.** `src/web/.prettierrc` carries an `overrides` entry mapping `*.html`
  to the `angular` parser — a setting with nothing to act on while every template was inline.
- **One component already did it.** `pages/home.ts` used `templateUrl: './home.html'`, so the pattern was in the
  repo as a precedent of one, with no rule saying which way was right.
- **Diffs were unreadable.** A markup change and a logic change landed in the same file, and a reviewer could not
  tell from the file list which was which.
- **The style blocks are SCSS, not CSS.** They use `//` comments and nested selectors, so an extracted
  stylesheet has to be named `.scss`: a `styleUrl` is compiled according to its file extension, and a `.css` file
  would be read as plain CSS and fail on the first `//`.

## Decision

### A component is up to three files, never one

- `<name>.ts` — the decorator and the class, with `templateUrl: './<name>.html'` and, when the component has
  styles, `styleUrl: './<name>.scss'`.
- `<name>.html` — the markup.
- `<name>.scss` — the styles. A component with no styles has no stylesheet; there are no empty files.

`template:` and `styles:` do not appear in a `@Component` anywhere in the workspace — `projects/site`,
`projects/admin` and `projects/ui` alike.

### The one exception is a spec's host component

A throwaway host declared inside a `*.spec.ts` to drive the component under test keeps `template:` inline. Its
markup is the test's fixture, it is read next to the assertions that depend on it, and splitting it would scatter
one test across two files. Eleven spec files rely on this.

### Names follow the `.ts` file, and the class when a file holds several components

**Withdrawn by [ADR 0051](0051-one-component-per-folder.md):** no file declares several components any more, so
only the first rule below is in force. Kept here for the history of how the extracted files were first named.

- One component in the file: the pair takes the file's own stem — `artist-editor.ts` → `artist-editor.html`.
- ~~Several components in the file: each pair takes the kebab-cased class name~~ — this is how `shared/cards.ts`
  came to sit beside `class-card.html` and `info-pages.ts` beside `about.html`, which is exactly the confusion
  0051 removed by giving each component its own file and folder.

The class name rather than an index or a suffix, so a component's markup is findable from the component's name
without opening the `.ts` first.

### The migration was mechanical and verified by equivalence, not by eye

133 files were extracted from 75 `.ts` files, removing 11,070 lines of string literal. It was done with the
TypeScript compiler API rather than a regular expression: every `template`/`styles` initializer was confirmed to
be a `NoSubstitutionTemplateLiteral` — none of the 86 templates or 47 style blocks contained a `${}`
substitution — and each property assignment was replaced by range, back to front, so no offset shifted under an
earlier edit. Extracted content was de-indented by its common leading whitespace and formatted with Prettier.

Correctness was then checked by parsing each template before and after with `@angular/compiler`'s `parseTemplate`
at `preserveWhitespaces: false` (the compiler's default, which this repo does not override) and comparing the
ASTs with source positions stripped. Fourteen templates differed, every one of them in whitespace that CSS
discards: an edge space on text that is the sole content of a block element, a whitespace run moved between a
space and a newline, or a binding's source text reflowed onto one line. Where a space actually separated two
inline elements — `<ui-icon /> <a>` in `contact.html`, the text before `<a>` in `artist-detail.html` — Prettier
preserved it. The style blocks were compared whitespace-normalised: 46 of 47 were identical, the last differing
only where Prettier wrote `0.8s` for `.8s` and `cubic-bezier(0.2, 0.7, 0.2, 1)` for `cubic-bezier(.2,.7,.2,1)`.

## Options considered

- **Leave it inline.** No work, and one file per component is one thing to open. But it had already produced
  1300-line files, and the `.prettierrc` override and `home.html` showed the repo pulling the other way.
- **Extract only the large ones** (say, over 200 lines). Keeps small components terse. Rejected: a threshold is a
  rule nobody can apply without counting, it makes the layout unpredictable, and a component crossing the line
  later needs a second, gratuitous commit. A uniform rule is checkable with one grep.
- **Extract markup but keep styles inline.** Halves the file count. Rejected: the style blocks are the second
  bulk — 2.4 kB in `submission-detail.ts` — and `styleUrl` costs no more than `templateUrl`.
- **Hand-edit the extraction.** Rejected at this scale: 133 files is where transcription errors live. The AST
  pass plus the `parseTemplate` equivalence check is both faster and stronger evidence than review of a
  11,000-line diff.

## Consequences

- A component's logic is readable on its own. `exhibition-editor.ts` was 1385 lines, mostly markup; the class now
  stands alone, and a markup change touches an `.html` file a reviewer can pick out of the file list.
- Angular's HTML tooling, Prettier's `angular` parser and the editor's tag handling now apply to every template.
- `npm run build` and `npm test` still pass unchanged: 598 tests across 58 files, as before the change.
- **What we must not do now:** write `template:` or `styles:` in a `@Component` outside a spec; extract a spec's
  host component; name an extracted file anything but its `.ts` stem (0051 withdrew the kebab-cased-class
  alternative); or write an extracted stylesheet as `.css`, which would stop the SCSS in it from compiling.
- Component styles are no longer visible in the same file as the markup they style, so a change to either is a
  two-file read. That is the cost of the split and the reason the two files sit side by side with the same stem.
