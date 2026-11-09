# v2.133p13 Hot-fix: Integration Module Migration Drift Recovery

**Date**: 2026-04-29 14:40
**Severity**: High
**Component**: Integration.Infrastructure / Outbox migrations / Cross-module deployment safety
**Status**: Resolved

## What Happened

v2.133p12 commit `3f8b2815` (2026-04-29 ~14:25) shipped OUTBOX-1-DISP atomic claim refactor — added `ClaimedAtUtc datetimeoffset NULL` column to 14 modules' OutboxMessages tables via EF migrations.

Post-push audit discovered 2 untracked files in git status:
- `src/modules/integration/.../Migrations/20260429070508_AddClaimedAtUtcToOutboxMessages.cs`
- `src/modules/integration/.../Migrations/20260429070508_AddClaimedAtUtcToOutboxMessages.Designer.cs`

Cross-module scan via `git show --name-only 3f8b2815` confirmed drift: 13/14 modules committed both migration files. Integration's snapshot `.Designer.cs` was in commit, but the migration `.cs` file was left untracked. Structural problem: Integration uses `Migrations/` path, not `Persistence/Migrations/` like other 13 modules.

## The Brutal Truth

This was silently catastrophic. Deploy of v2.133p12 would have crashed Integration's `OutboxDispatcher` with `Invalid column name 'ClaimedAtUtc'` on first write. EF snapshot metadata claims column exists (updated in p12 commit), but the migration history would have no entry. ALTER TABLE never runs on deploy → OutboxMessages table stays at old schema → runtime 500 error, no graceful fail-back.

Worse: the drift pattern (schema mismatch between snapshot and migrations table) is invisible in CI — build succeeds, tests pass locally (developer's DB gets manual migrations), but production OOM on first startup. This is a deployment time bomb.

The reason I caught it: post-push `git status` habit + documented trust-but-verify protocol from memory.

## Technical Details

**Migration files missing from p12 commit:**
```
src/modules/integration/AqtCoreFW.Integration.Infrastructure/Migrations/20260429070508_AddClaimedAtUtcToOutboxMessages.cs
src/modules/integration/AqtCoreFW.Integration.Infrastructure/Migrations/20260429070508_AddClaimedAtUtcToOutboxMessages.Designer.cs
```

**Deployed snapshot (was in p12, now stale):**
```
Designer.cs line 234: modelBuilder.Entity("Integration.Domain.OutboxMessages")
  .Property<DateTimeOffset?>("ClaimedAtUtc")...
```

**Root cause: git add pattern asymmetry**
p12 likely used glob `**/Persistence/Migrations/*.cs`. Integration module organizes as:
```
AqtCoreFW.Integration.Infrastructure/
├── Migrations/              ← non-standard, no "Persistence" subfolder
│   ├── 20260429070508_*.cs  ← was skipped by glob
│   └── 20260429070508_*.Designer.cs ← was skipped by glob
└── (other modules use Persistence/ subfolder)
```

## What We Tried

1. **Scout audit** (git show cross-module): confirmed 13/14 modules committed both files, Integration missing `.cs` only.
2. **Remediation**: stage 2 Integration migration files, rebuild Integration.Infrastructure to verify no syntax errors (0E/0W), run integration outbox tests (4/4 PASS — they exercise the old schema, not new column yet).
3. **Pre-Finalize Test Gate (all categories PASS)**:
   - T1 Unit tests: 75/75 PASS (1.075s)
   - T2 Integration tests: 4/4 PASS (12s) — includes atomic claim race tests from OUTBOX-1
   - T3 Architecture suite: 16/16 PASS (13s) — no new cross-schema violations
4. **Code review**: no issues; static analysis clean.
5. **Docs sync**: 6-artifact standard finalize (roadmap, changelog, dependency-map, backlog, project-state, memory index).

## Root Cause Analysis

**Structural:** Integration module doesn't follow the 13-module folder convention. Other modules store migrations under `Persistence/Migrations/` subfolder; Integration stores directly under `Migrations/`. When git add glob was written, it was scoped to `**/Persistence/Migrations/*.cs`. This worked because snapshot designer files auto-generate during scaffolding (they ARE in the glob match or checked in separately), but the `.cs` migration file is hand-authored and easy to miss during commit when the path doesn't match the glob.

**Process:** No pre-commit hook validates migration file ↔ designer snapshot parity. The drift was asymmetric: designer updated (EF tooling auto-rewrote it), migration file left staging. Only caught by post-push audit.

## Lessons Learned

1. **Architecture suite gap**: no guard that asserts `every snapshot column has corresponding migration` or `every migration in history has snapshot entry`. This is a design-contract violation (schema state must be in-sync across all 3 sources: migrations table, migration file, snapshot metadata). Cost: 4 patches to catch last cross-schema slip (v2.133p5→p8 TenantBackfillSeeder), this time 1 hour post-push.

2. **Non-uniform module layout is a debt marker**: Integration's `Migrations/` vs others' `Persistence/Migrations/` is the root of the problem. Future YAGNI for refactoring — but flag it as P3 backlog item DOC-N.

3. **Trust-but-verify + post-push audit is essential**: memory entry `feedback-verify-subagent-reports.md` applies here too — always run `git status` after push, not just `git push`. Catches drift in the commit boundary itself.

4. **Memory feedback validation**: entry `feedback-journal-before-git-commit.md` (journal runs BEFORE git-manager) ensured this journal is part of the remediation commit, not a separate follow-up. Audit trail stays clean.

## Next Steps

1. **Immediate (this commit)**: Stage 2 Integration migration files, commit as v2.133p13, push to main. Git-manager agent owns the final push step.

2. **Future backlog items (P3, no blocker)**:
   - DOC-N: Document why Integration module has non-standard folder structure (Migrations/ vs Persistence/Migrations/) and whether to unify.
   - ARCH-GUARD-N: Add architecture suite rule: snapshot column presence check (all columns in Designer.cs must exist in migration history for that module).

3. **One-off validation**: after push completes, run production migration rollback test (scaffold new Migration against production schema, verify ClaimedAtUtc column already exists in __EFMigrationsHistory — proves p12 would have failed to apply p13's migration if not caught).

---

**Status:** Resolved  
**Files staged for p13 commit:** 2 (Integration Migrations)  
**All test gates PASS:** T1 (75/75), T2 (4/4), T3 (16/16)  
**Docs verified:** 6/6 artifacts synced + Grep-confirmed
