# Journal — QĐ 742 Cấp 4 Phase-01 Slice A

**Date:** 2026-05-01 ~01:00 ICT
**Branch:** main
**Plan:** `plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md`
**Slice:** A of A→D (Phase 01 split)

## What shipped

Pure-domain audit category infra — zero DB / zero behaviour change yet:

- `src/shared/AqtCoreFW.SharedKernel/Compliance/AuditCategoryAttribute.cs` — `AuditCategory` enum (5 values, byte-encoded per QĐ 742 mục 3.1.b) + `[AuditCategoryAttribute]` decorator. Co-located with `DataClassificationAttribute` for codebase consistency.
- `src/modules/audit/AqtCoreFW.Audit.Domain/Services/AuditCategoryMapper.cs` — `Map(action: string, commandType: Type?) → (Category, Severity)`. Attribute on commandType wins; verb fallback handles both PascalCase outputs from `AuditBehavior.ExtractAction` ("Create", "Update", "Delete", ...) and SCREAMING_SNAKE_CASE constants from `AuditAction` ("ROLE_ASSIGN", "RTBF", ...). Underscores stripped in normalisation so the two encodings collapse to one switch table. Reflection lookup cached per `Type`.
- Unit tests: 10 attribute + 57 mapper. Drift regression guard auto-scans every `public static AuditAction` constant via reflection and asserts the mapper handles it without throw — catches future drift when new action constants are added.

## Tests

- `AqtCoreFW.SharedKernel.Tests` — 125/125 PASS (10 new + 115 existing untouched)
- `AqtCoreFW.Audit.Domain.Tests` — 164/164 PASS (57 new + 107 existing untouched)
- Downstream `AqtCoreFW.Audit.Infrastructure` builds clean — no caller broken

## Key drift caught during re-verify

Plan was 3 days old (approved 2026-04-28). Pre-execution scout caught seven drift items, surfaced in `phase-01-audit-log-baseline.md` "Re-verify Drift" table:

1. **AuditSeverity already exists** — `Domain/ValueObjects/AuditSeverity.cs` 4-value `Low/Medium/High/Critical` (used by SecurityIncident). Plan wanted to create new 3-value `Info/Warning/Critical` enum. Resolution: reuse existing 4-value (DRY, syslog-aligned). NC-1 severity table remapped.
2. **AuditAction has 24 values** (plan said 20). 4 Phase-D auth events (`PermissionGranted`, `RoleAssigned`, `GroupRoleAssigned`, `DataScopeChanged`, etc.) declared but **zero production callers** in `src/` — runtime audit goes through `AuditBehavior.ExtractAction` verb-parser. Mapper signature accepts string + optional Type to bridge both encodings.
3. **`SodAuditLogPurgeJob` is wrong target** — purges `SodViolationLogs` (Identity), not `AuditLogEntries` (Audit). Plan Step 12 misdirected. Slice D will create NEW `AuditLogPurgeJob`.
4. **No existing audit-log purge** — confirmed via grep. Treat as CREATE.
5. **NC-1 severity table referenced non-existent action constants** (`LOGIN_FAIL`, `MFA_DISABLED_*`, `IP_RULE_MUTATE`, ...). Defer adding those constants; map only the 24 currently declared.
6. **Compliance enum placement** — plan said `Audit.Domain/Enums/`; codebase precedent (`DataClassificationAttribute`) places shared compliance enums in `SharedKernel.Compliance`. Followed precedent.
7. **Phase scope too large for one cook session** — 17 steps / 5-7 days. Split into 4 slices A→D.

## What's next (Slice B)

- Extend `AuditLogEntry` with `Category`, `Severity`, `SchemaVersion` properties (no DB change yet — entity-only).
- Extend `AuditLogEntryConfiguration` with column mappings + indexes.
- Add `AuditRetentionPolicy` entity + EF Configuration + `DbSet` on `AuditDbContext`.
- Unit tests for entity factory + configuration round-trip.

Slice C is the high-risk one (M1 migration + backfill + HMAC v2). Slice D is the longest (decorate 135 commands, M2 trigger, controller + handlers, NEW purge job, CI guard).

## Surprises / lessons

- The 24-value `AuditAction` value object is essentially **catalog metadata, not a runtime contract** — production audit strings come from `AuditBehavior.ExtractAction` verb-parsing of the command type name. This is a foot-gun: developers may expect `AuditAction.Login` to land in `AuditLogEntry.Action` as `"LOGIN"`, but actually only `"Login"` (PascalCase verb) lands there. Slice D should consider whether to retire the value object entirely or wire it as the canonical source.
- Underscore normalisation (`_` → `""`) collapses two action encodings into one switch table — significantly DRY'd the mapper vs the original 60-line plan sketch.

## References

- Plan: `plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md` (drift section + slice plan added 2026-05-01)
- QĐ 742 source: `plans/reports/qd-742-fresh-extracted-260430-2233.txt` (515-line antiword extract)
- Codebase mapping baseline: `plans/260428-1740-cap-4-qd-742-compliance/visuals/qd-742-cap-4-mapping.html` v2.2
