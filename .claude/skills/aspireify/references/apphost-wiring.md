# 6311arts AppHost Wiring Reference

```text
sql ── appdb ─┬─> migrations ─completed─┬─> api ──────┐
              │                         └─> worker    │
cache ────────┼──────────────> api, gateway           │
blobs ────────┼──────────────> api, worker            ├─> gateway (only external endpoint)
mail ─────────┴──────────────> worker                 │
squarefake (run mode, fake) ─> api, worker            │
site  (Angular SSR) ──────────────────────────────────┤
admin (Angular SPA) ──────────────────────────────────┘
devtunnel (opt-in, sandbox) ─> gateway      importer (explicit start) ─> appdb, blobs
```

| Logical name | Run mode | Publish mode (Azure Container Apps) |
|---|---|---|
| `sql` / `appdb` | SQL Server container, persistent volume | Azure SQL Database |
| `cache` | Redis container | Decide in an ADR: Azure Managed Redis, or a Redis container app |
| `storage` / `blobs` | Azurite | Azure Storage |
| `mail` | Mailpit | Azure Communication Services Email |
| `squarefake` | Project (`Square:Mode=fake`) | not published |
| `migrations` | Project, run-once | Container Apps job or a run-once container app |
| `api`, `worker`, `gateway` | Projects | Container apps (`gateway` external; others internal) |
| `site` | `AddJavaScriptApp` (SSR dev) | Node SSR container |
| `admin` | `AddJavaScriptApp` (dev server) | Static bundle host container |
| `devtunnel` | Opt-in, sandbox webhooks | not published |

Confirm exact package and API syntax against the installed Aspire version before editing.
