# Architecture Decision Records

One file per architectural decision, numbered in order: `NNNN-short-title.md`. CLAUDE.md requires an ADR for
every architectural decision. Rules in `.claude/rules/` cite the ADR they back.

## Status values

| Status | Meaning |
|---|---|
| `DECIDE` | Open. Options and costs are listed; nobody has chosen yet. Don't build on it. |
| `Accepted` | In force. |
| `Superseded by NNNN` | Replaced; kept for history. |

Never rewrite an accepted ADR's decision. Write a new ADR that supersedes it.

## Index

The baseline set (0001–0010) is written in prompt 0.3.

| # | Title | Status |
|---|---|---|
| — | — | — |

## Template

```markdown
# NNNN — Title

- **Status:** DECIDE | Accepted | Superseded by NNNN
- **Date:** YYYY-MM-DD
- **Rules affected:** .claude/rules/<file>.md

## Context
What forces the decision.

## Decision
What we do. (For DECIDE: leave empty and fill in Options.)

## Options considered
Each option with its cost and trade-offs.

## Consequences
What gets easier, what gets harder, what we now must not do.
```
