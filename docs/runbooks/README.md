# Runbooks

Step-by-step operating procedures: things a person runs, in order, and can check. Architecture and
rationale live in `docs/architecture/` and `docs/adr/`. Runbooks say *how*, not *why*.

Runbooks never contain secrets, connection strings or keys. They name the user-secret, AppHost parameter or
Key Vault reference instead.

## Planned

Each runbook is written by the build prompt shown here.

| Runbook | Written in |
|---|---|
| `wp-discovery.md`: what the live WordPress site exposes — ✅ written 2026-10-21 | Import I.1 |
| `wp-import.md`: read-only WooCommerce key, importer commands | Import I.2 |
| `import-report-<run>.md`: per-run import counts, skips and failures | Import I.8 (generated) |
| `square-sandbox.md`: sandbox app, dev tunnel webhook URL, user-secrets | 5.20 |
| `azure-dev.md`: dev environment via CI, Square sandbox webhook registration | 6.2 |
| `cutover.md`: content freeze, final delta, DNS, redirect crawl | 6.3 / Import I.14 |

## Template

```markdown
# Title

**When:** the situation this runbook is for.
**Needs:** access, tools, secrets (by name).

## Steps
1. …

## Verify
How you know it worked.

## Roll back
How to undo it.
```
