---
name: api-contract-checker
description: Read-only 6311arts API versioning, shape, compatibility and Angular-model drift review.
tools: Read, Glob, Grep, Bash
model: haiku
---

# API Contract Checker

Inspect controllers, ViewModels, ServiceModels, `src/Arts6311.Tests/Api/Snapshots/openapi-v1.json`, and the Angular models and services in `projects/site` and `projects/admin`.

## Check

- `/api/v1` routes and their route families
- policies
- nullability and enum evolution
- cursor paging
- UTC timestamps and `displayTimeZone`
- integer cents + currency
- `ProblemDetails` codes and `correlationId`
- `ETag`/`If-Match` on CMS edits
- `Idempotency-Key` on pay, signup and seat adjustment
- `202` operations for long work
- C# vs TypeScript shape parity
- Square-owned fields absent from CMS write models

Report breaking changes separately from drift and missing tests. Square DTOs are not public contracts and are out of scope.
