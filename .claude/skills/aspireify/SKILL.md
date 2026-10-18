---
name: aspireify
description: Wire the 6311arts repository into its Aspire AppHost after scaffolding, or when the resource graph changes.
---

# Aspireify 6311arts

1. **Inspect** the solution, projects, Angular workspace scripts, configuration, health checks and external dependencies.
2. **Propose the resource graph** in [references/apphost-wiring.md](references/apphost-wiring.md) before editing. Call out every change to it.
3. **Keep the split.** `Arts6311.AppHost` stays declarative. `Arts6311.ServiceDefaults` owns telemetry, health, resilience and discovery, and every .NET project calls `AddServiceDefaults()`.
4. **Add each project and resource once,** with its stable logical name.
5. **Wire real dependencies.** `migrations` completes before `api` and `worker`, and the `gateway` waits for `api`, `site`, `admin` and `cache`.
6. **No literals.** Secrets are parameters (generated and persisted where possible). Endpoints come from service discovery.
7. **Keep run mode offline-capable** (`Square:Mode=fake`). Keep publish mode Azure-shaped with the same names.
8. **Verify a clean start** from empty volumes: health, traces, migrations completed, and the site and admin reachable only through the gateway.
