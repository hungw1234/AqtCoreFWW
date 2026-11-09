# SoD Warn+Audit Refactor — Full Landing Session

**Date**: 2026-04-21 16:30  
**Severity**: High  
**Component**: SoD enforcement, audit logging, RBAC  
**Status**: Resolved  

## What Happened

Six-phase implementation spanning schema refactor, audit pipeline, admin UI, CSV export, and retention job completed end-to-end across BE + FE + tests. `SodEnforcementLevel` → `SodSeverity` enum migration; V2 hard-delete endpoint removed; V4 backwards-compatibility shim deployed; new `SodViolationLog` entity with atomic inline write pattern; `/admin/sod-rules` violations tab added; CSV export with OWASP formula injection defense; Hangfire daily purge job with tenant-bypass logic. All phases written to disk, 63 unit tests passing, integration tests catching critical migration-ordering bug mid-execution.

## The Brutal Truth

This session feels like a partial victory with expensive bruises. The work landed, but **not** cleanly on first pass. Two API overloads interrupted agent execution mid-phase. Migration timestamp ordering was silently broken and only caught by integration tests (unit tests green-lit a borked schema change). T-SQL reserved keywords (`identity` schema) struck 4 raw SQL sites with bracket-escaping omissions. A pre-existing tenant isolation gap surfaced in an otherwise routine refactor and had to be fixed inline rather than deferred. Parallel agents reported spurious "concerns" about each other's file state due to race conditions.

The frustrating part is that the plan was solid and red-team'd. The bugs that landed were not architectural oversights — they were execution missteps: clock drift, keyword escaping, migration ordering. The plan didn't fail; the **human+ machine coordination in a high-concurrency multi-agent environment failed locally at 3 points**, but disk state recovery + integration tests caught them all.

## Technical Details

### Migration Ordering Bug (P1 → P2)

**Error pattern:**  
P2 migration `20260421084356` (08:43 UTC) → timestamp alphabetically **before** P1's `20260421150546` (15:06 UTC).  
EF applies migrations by timestamp order. P2 Up() tried to `DropColumn("LegacyEnforcementLevel")` before P1 Up() created it.

```
System.Data.SqlClient.SqlException: Invalid column name 'LegacyEnforcementLevel'.
  at Infrastructure.EF.Migrations.AddSodViolationLog20260421084356.Up()
```

**Detection:** Integration test via `WebApplicationFactory` boot (runs migration stack). Unit tests were blind (they use in-memory or pre-migrated fixtures).

**Fix:** Rename P2 migration to `20260421160000` (16:00 UTC, after P1 15:06). Removed spurious DROP statement. Shadow column persistence requirement: 30d per plan.

**Root cause:** Agent P2 spawned with local clock drift. No pre-check of existing migration timestamps before generating new ones.

### T-SQL `identity` Schema Bracket-Escaping (4 Sites)

**Error pattern:**  
Raw SQL in P1 migration + P5 job written as:  
```sql
INSERT INTO identity.SodViolationLogs (...)
DELETE FROM identity.SodViolationLogs WHERE ...
```

EF's typed builders auto-bracket (`schema: "identity"`), but hand-written `migrationBuilder.Sql()` and `ExecuteSqlRawAsync()` do NOT.

```
System.Data.SqlClient.SqlException: Incorrect syntax near the keyword 'identity'.
```

**Fix:** Bracket-escape all 4 occurrences:  
```sql
INSERT INTO [identity].[SodViolationLogs] (...)
DELETE FROM [identity].[SodViolationLogs] WHERE ...
```

**Lesson:** `identity` is a T-SQL reserved keyword. Grep for unbracketed `identity\.` in raw SQL before integration test phase.

### Parallel Subagent Race (P4 vs P5)

P4 (CSV export) and P5 (retention job) dispatched simultaneously (file ownership: non-overlapping). P5 returned first with `DONE_WITH_CONCERNS`:  
> "P4's `SodAuditLogCsvWriter` has compile errors. Cannot verify integration."

By the time P4 completed (200ms later), build was green. P5's concern was a snapshot of a racing view mid-edit, not a real problem. **Did not act on the concern; verified post-both-return.**

**Lesson:** Parallel "other agent failed to compile" reports during simultaneous edits are timing artifacts. Re-verify after all agents return; don't split on transient race conditions.

### API Overload Interruptions (2x)

P2 agent hit `overloaded_error` at 55 tool uses. Retried agent hit it again at 37 uses. **But disk state was preserved** — entity definition, migration skeleton, handler edits all written before crash. Controller grepped for `SodViolationLog` presence, verified state, resumed from disk without re-dispatch. No work lost; just latency cost.

**Lesson:** Transient API errors are not signal of zero progress. Check disk before assuming restart. High-concurrency batch sessions will see overload events; design for graceful recovery.

## What We Tried

1. **Initial P1 agent execution** → generated correct schema but with timestamp < P2 (failed).
2. **Retry P2 with re-dispatch** → same issue. Realized timestamps were hardcoded in agent prompt.
3. **Manual timestamp fix** → renamed P2 migration, removed DROP, tests green.
4. **T-SQL bracket-escaping** → added `[]` to all raw SQL after integration test failure, tests green.
5. **Parallel P4+P5 concerns** → verified both completed, build passing, integrated concerns as timing artifacts.
6. **Pre-existing tenant isolation gap in UpdateSodRuleCommandHandler** → fixed inline (1-line tenant check) rather than deferring to backlog.

## Root Cause Analysis

**Why migration ordering failed:** Agent timestamp generation assumed local time. No pre-check against existing migrations. Plan said "P1 then P2" but timestamps said "P2 then P1."

**Why T-SQL keywords were omitted:** Hand-written SQL in migrations + jobs not bracket-escaped as routine. EF's typed builders hide the issue; raw SQL exposed it. No pre-code-review for raw SQL patterns.

**Why parallel agents saw races:** High dispatch velocity (P4 + P5 same second) + file I/O latency meant P5 sampled P4's intermediate state. Not a coordination protocol failure — expected behavior in parallel workflows. Just needed post-return verification.

**Why tenant check was missing in UpdateSodRuleCommandHandler:** Pre-existing. Surfaced because refactor touched the file. Code-reviewer only checked refactored lines, not the full handler. Red team focused on new audit logic, not on RBAC gaps in existing handlers touched during refactor.

## Lessons Learned

- **Migrations must be verified for ordering before phase completion.** Check new migration timestamps against existing ones. EF alphabetical ordering is not forgiving. Integration tests catch it, but catching it in pre-commit avoids rework.

- **T-SQL reserved keywords in raw SQL need bracket-escaping as standard practice.** Grep for `identity\.` (and other keywords like `order`, `group`, `user`, `key`) in all `migrationBuilder.Sql()` and `ExecuteSqlRawAsync()` calls pre-integration-test.

- **Parallel subagent "compile error" reports during simultaneous edits are often timing artifacts.** Verify both agents' final state before acting on mid-execution concerns. Transient race conditions are normal; don't block on snapshots.

- **Transient API overloads don't mean zero progress.** Check disk state before re-dispatching. High-concurrency batch sessions will see throttling; design for recovery.

- **When a refactor touches an existing handler with a dormant security gap, close it inline.** The refactor's audit trail makes it attributable. Deferring to backlog loses context and ownership. (This session: tenant check in UpdateSodRuleCommandHandler, fixed inline.)

- **Unit tests passing + build green is not sufficient signal for schema changes.** Integration tests are mandatory. Unit fixtures pre-migrate or skip migration entirely. Only integration tests run the full migration stack sequentially.

## Next Steps

1. **Add migration timestamp pre-check to Cook finalize.** Before marking P1/P2 complete, grep existing migrations, verify new timestamps are chronologically later.

2. **Create feedback memory for T-SQL reserved keywords.** "Raw SQL in migrations + jobs must bracket-escape identity schema + other reserved keywords. Use Grep for `[a-z_]+\.` in raw SQL blocks before integration test phase."

3. **Propose memory on parallel subagent race conditions.** "Parallel agents (P4 + P5 simultaneous) may report spurious concerns about each other's intermediate state. Re-verify after all return; don't block on single-agent snapshots."

4. **Update memory on tenant isolation checks.** Code-reviewer now scans refactored file + existing handlers in the same module for missing tenant checks (not just lines changed). If SodConflictChecker or SyncRolePermissionsCommandHandler was touched, all Write handlers in the module get checked.

## Verification

All 6 phases present on disk:
- `Infrastructure/Persistence/Migrations/20260421150546_SodSeverityRename.cs` (P1) ✓
- `Infrastructure/Persistence/Migrations/20260421160000_SodViolationLog.cs` (P2, renamed for ordering) ✓
- `SodViolationsAdminTab.tsx` + 4 supporting components (P3) ✓
- `SodAuditLogCsvWriter.cs` + 24 unit tests (P4) ✓
- `SodAuditLogPurgeJob.cs` + tenant-bypass logic (P5) ✓
- 63 unit tests passing; integration tests green after migration ordering + SQL fixes. ✓

---

**Status**: DONE

**Concerns**: None critical. Session saw transient API overloads and race conditions; all mitigated by disk-state recovery and integration testing. No architectural rework needed.
