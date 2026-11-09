# AUDIT-INCIDENT-DTO-DAPPER P1 Bug Fix

- **Date:** 2026-05-08 17:30 +07
- **Branch:** `fix/audit-incident-dto-dapper`
- **Commit:** `7b6d7d73`
- **PR:** [#90](https://github.com/thiennd212/AqtCoreFW/pull/90) — OPEN
- **Code review:** APPROVE 9.3/10 (zero P0/P1)
- **Effort:** ~30min as estimated

## What

`GET /api/v1/admin/incidents` returned **HTTP 500** because `GetSecurityIncidentsQueryHandler` Dapper-materialized into `SecurityIncidentDto`, a positional record whose ctor types didn't match SQL columns:

| Column | DB type | Old DTO type | Mismatch |
|--------|---------|-------------|----------|
| `Severity` | `int` (`HasConversion<int>`) | `AuditSeverity` enum | int → enum via positional ctor |
| `Status` | `nvarchar(32)` (`HasConversion<string>`) | `IncidentStatus` enum | string → enum via positional ctor |
| `CreatedAt` | `datetime2(3)` (entity = `DateTime`) | `DateTimeOffset` | DateTime → DateTimeOffset via positional ctor |

Positional record requires exact ctor match. Property-set path handles enum conversion. Pre-existing — surfaced 2026-05-08 by Phase-07 Tier D coverage.

## Fix

Single-file source change. Convert `SecurityIncidentDto` from `sealed record` (positional) → `sealed class` (parameterless ctor + public setters). Align `CreatedAt: DateTimeOffset → DateTime` to match domain entity `SecurityIncident.CreatedAt`.

```diff
- public sealed record SecurityIncidentDto(
-     Guid Id, ..., AuditSeverity Severity, IncidentStatus Status,
-     ..., DateTimeOffset CreatedAt);
+ public sealed class SecurityIncidentDto
+ {
+     public Guid Id { get; set; }
+     ...
+     public AuditSeverity Severity { get; set; }
+     public IncidentStatus Status { get; set; }
+     ...
+     public DateTime CreatedAt { get; set; }
+ }
```

XML doc on the class explicitly references the memory rule (`feedback-dapper-no-positional-records`) to prevent future "tidy refactor" regression.

## Test coverage

New `SecurityIncidentsApiTests` (2 tests) — first integration coverage for `/api/v1/admin/incidents`:

1. `List_WithViewPermission_Returns200_AndMaterializesSeededIncident` — full round-trip: seed `SecurityIncident.Report(...)` → GET → assert `severity` (string via JsonStringEnumConverter), `status`, `title`, `createdAt` all present. **Exercises both bug-prone paths** (enum + DateTime materialization).
2. `List_Unauthenticated_Returns401` — auth contract guard.

**Suite results:**
- New tests: **2/2 PASS**
- Architecture: **26/26 PASS**
- Audit.Application compile: **0E/0W**
- Audit.Integration full: 14/24 (10 **pre-existing** failures on `main` baseline confirmed via stash pop; out-of-scope: `RtbfEndpointsTests`, `AuditLogImmutabilityTests` SQL trigger drift, `Delete_GlobalDefault_Returns400`)

## Scope discipline

`LoginAttemptDto` and `AuditLogDto` (same file, same Dapper pattern) NOT touched. Justified by:
- `LoginAttempt.AttemptedAt` = `DateTimeOffset` matches DB `datetimeoffset` ✓
- `AuditLogDto` SQL projection (handler line 44) does NOT include `Severity`/`Category` — the only `HasConversion<int>` columns on `AuditLogEntry`. All projected cols are string/Guid/`datetimeoffset` ✓
- No live evidence either fails.

Code-review confirmed scope decision is **justified, not lucky**. Spawned 2 P3 follow-ups (AUDIT-DTO-FU1 = pre-emptive XML doc warnings; AUDIT-DTO-FU2 = test assertion UX) for future cleanup.

## Memory predicted exactly this

`feedback-dapper-no-positional-records.md` (existed pre-fix) said:
> Dapper DTOs must use class, not positional record (nullable params fail)

Phase-07 Tier D journal (`journal-260508-1700`) flagged this DTO by name as the next backlog item. End-to-end the memory system worked: bug surfaced → memory pinpointed pattern → backlog item filed → 30min cook closed it.

## Next

- Merge PR #90 manually (per memory `feedback-git-manager-merge-verify` — agent does not auto-merge).
- Backlog now has 2 fresh P3 follow-ups (AUDIT-DTO-FU1/FU2) — defer.
- Next priority candidates: **BE-LOGIN-USER-ENUMERATION (P2, ~1h)** or **PHASE7-TIER-D-RESIDUAL (P3 triage)**.
