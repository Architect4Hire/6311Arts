# 0017 — Database-level immutability for the audit log (DECIDE)

- **Status:** **DECIDE**, before production (prompt 6.3)
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/auth.md` ("Sensitive operations need … an audit row"), `.claude/rules/backend.md`

## Context

`AuditLogs` (prompt 1.3) records sensitive actions: deletes and archives, seat adjustments, outbox replay or discard, user and role changes, secret rotation, redirect changes. Rows are meant to be written once and never changed.

Today that's enforced only in the application. `AuditLogImmutabilityInterceptor` rejects *tracked* updates and deletes at `SaveChanges`. `ExecuteUpdate`, `ExecuteDelete`, raw SQL, and any other connection to the database bypass it. In run mode the app connects as `sa`, so the database itself enforces nothing. Raised by `@architecture-reviewer` on 1.3.

## Decision

Not decided. Until it is, the docs say "application-enforced only", and `IAuditWriter` is the only sanctioned writer.

## Options considered

| Option | Cost | Trade-offs |
|---|---|---|
| **A. `INSTEAD OF UPDATE, DELETE` trigger** that raises an error, added through a new generated migration (`migrationBuilder.Sql` in a migration created with `dotnet ef migrations add`, never a hand-edited one) | Small: one migration plus a SQL Server test | Blocks every path except `DISABLE TRIGGER`, which needs `ALTER` permission. Works the same with `sa` locally. A future retention job would disable it deliberately and visibly. |
| **B. `DENY UPDATE, DELETE ON AuditLogs`** for a dedicated application login | Medium: separate app and migration logins in Azure SQL (managed identity per app), and a non-`sa` user locally | The strongest separation, and it fits the managed-identity plan in 6.1. It doesn't protect run mode unless run mode also stops using `sa`. |
| **C. Append-only ledger table** (Azure SQL / SQL Server 2022+ ledger) | Medium: ledger tables can't be altered later the same way; check EF Core support | Tamper-evident as well as tamper-resistant, but heavier than the need, and it ties the schema to ledger features. |
| **D. Accept application-only enforcement** | None | Simplest. It relies on review and the boundary tests to keep other writers out. |

## Consequences

- **Whichever is chosen,** add a SQL Server container test proving an update and a delete are refused. SQLite doesn't run the SQL Server DDL.
- **A or B must land before production,** with the evidence linked here.
