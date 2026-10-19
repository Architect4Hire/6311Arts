# 0010 — CSoon template license (DECIDE before launch)

- **Status:** **DECIDE**, before launch
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/design-system.md` (rule 4), `.claude/agents/design-review.md`

## Context

The public site's visual direction comes from the **CSoon** template, a commercial theme sold on WrapBootstrap: black-and-white photo hero, charcoal and pale-gray bands, uppercase letter-spaced type, square outlined buttons, thin line icons, Open Sans. The spike rebuilt that look in plain SCSS with no Bootstrap or jQuery. `@6311/ui` tokens will be extracted from it (`design-system.md`). 6311arts is a nonprofit, and its site sells artwork, classes and memberships through Square.

**Facts still needed** (none confirmed in this ADR):

- **Purchase history:** has 6311arts, or a previous agency, already bought a CSoon license? If so, which tier and for which end product? The current WordPress site may be using it.
- **License terms:** what do WrapBootstrap's Standard and Extended licenses allow?
  - The official license page couldn't be fetched (HTTP 403).
  - Informal sources (WrapBootstrap forum replies, other marketplaces) say Standard covers one end product whose end users aren't charged *for the product itself*, and Extended covers a product that's sold.
  - It's unclear whether an e-commerce site selling *other* goods counts as "charging end users". Confirm from the official terms.
- **Shipped template material:** what does the new site actually ship from the template (code, images, icon set, copy), as opposed to a look recreated from scratch?

## Options considered (DECIDE)

| Option | Cost | Trade-offs |
|---|---|---|
| **A. Confirm an existing license covers the new site** | $0 if one exists | Needs proof of purchase and terms that allow a rebuilt site (one end product, same client). |
| **B. Buy the license tier the official terms require** | Item price from WrapBootstrap; not verified here | Clear rights. If e-commerce counts as charging end users, Extended may be needed. |
| **C. Clean-room design: keep the direction, ship nothing from the template** | Design time to confirm and replace any template-derived assets or markup | No dependency on the license. Needs an audit of `projects/site` (SCSS, icons, images) against the template, and a written record of the result. |
| **D. Ask WrapBootstrap or the author directly** | Time only | Authoritative for the edge case (nonprofit, e-commerce, rebuilt site). Can be combined with A or B. |

## Consequences

- **Launch (prompt 6.3) is blocked until this is decided** and the evidence (receipt, terms or audit) is linked here.
- **`design-system.md` rule 4 points at this ADR.** `@design-review` should flag new template-derived assets until then.
