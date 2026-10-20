# 0005 — Square field ownership

- **Status:** Accepted; superseded by [0032](0032-website-items-managed-from-the-cms.md) for the price and stock of artwork and classes (membership plans unchanged)
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/square.md`, `.claude/rules/content.md`, `.claude/rules/frontend.md` (admin read-only values), `.claude/rules/outbox.md`

## Context

Shop items, classes and membership plans exist in both the CMS and Square. Staff at the front desk change prices and stock in Square POS. Staff edit names, descriptions, images and dates in the CMS. Two-way sync where both sides can write the same field produces lost updates and ping-pong echoes.

## Decision

Each field has exactly one owner.

| CMS owns (pushed to Square) | Square owns (pulled into the CMS, read-only in admin) |
|---|---|
| Name, description, artist, categories, images, publish/archive state | Price after creation |
| Class details, session dates and times | Stock, seats remaining, sold-out |
| Membership plan name, description, perks | Plan pricing and billing cadence after creation |
| | Orders, payments, refunds, customers, cards, subscriptions, member status |

- **The CMS sends a starting price and initial stock or capacity exactly once,** when the object is first created in Square. After that, price changes happen in Square.
- **Capacity changes** go through an explicit, audited "adjust seats" command that sends an inventory adjustment (an outbox message).
- **Inbound handlers update only Square-owned fields.** Outbound pushes send only CMS-owned fields.
- **Member status changes only from Square** (webhooks or reads), never from what a signup endpoint assumed.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Per-field ownership, price/stock once at creation (chosen)** | No write conflicts on any field. Staff need to know "price lives in Square" (the admin shows "Managed in Square"). |
| CMS owns everything, including price and stock | One editing place, but it overwrites front-desk POS changes and fights Square's inventory. |
| Square owns everything; CMS is a read-only mirror | No conflicts, but Square's catalog can't hold the site's content model (artist, exhibitions, rich copy, images per page). |
| Last-writer-wins on shared fields | Flexible, but clock- and order-dependent lost updates. Echoes are hard to suppress. |

## Consequences

- A CMS code path must never write a Square-owned field. Reviewers (`@integration-reviewer`) check this.
- The admin shows Square-owned values read-only, with a sync status chip.
- The reconciler fixes drift only on Square-owned fields, and re-stages CMS pushes that never landed (ADR 0003).
