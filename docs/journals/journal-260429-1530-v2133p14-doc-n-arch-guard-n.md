# Session Journal — v2.133p14 DOC-N + ARCH-GUARD-N (2026-04-29 ~15:30 ICT)

## What

**v2.133p14 SHIPPED:** Documentation + architecture test follow-up to v2.133p13 code-review feedback.

### Changes Executed

**Artifact 1: Documentation (DOC-N)**
- Added "Migration Folder Convention" section to `docs/code-standards.md` (~50 lines)
- Documents: 16 modules use `Persistence/Migrations/`, Integration uses `Migrations/` directly
- Rationale: Integration was test-only module at v2.132p1; structure now embedded (YAGNI refactor)

**Artifact 2: Architecture Test Suite (ARCH-GUARD-N)**
- NEW: `tests/architecture/AqtCoreFW.Architecture.Tests/MigrationConsistencyTests.cs` (~234 LOC)
- Test 1 **FileBalancePairs**: Ensures every Designer.cs has paired .cs migration (orphan detection)
- Test 2 **DesignerSnapshotDrift**: Compares latest Designer against ModelSnapshot for property drift
- Whitelist: Integration's anomalous `Migrations/` folder via path-probing helper

**Artifact 3: Real Drift Fix (Pre-existing)**
- Test caught pre-existing issue on FIRST RUN
- **Issue:** Workflow module's `20260429110600_AddUniqueActiveWorkflowInstanceIndex.Designer.cs` was missing `ClaimedAtUtc` property (added by v2.133p12's earlier-timestamp migration)
- **Root cause:** EF migration files generated BEFORE v2.133p12 commit merged (timing on branch)
- **Fix applied:** Updated Designer.cs to include ClaimedAtUtc property (matches current ModelSnapshot)
- **Safety:** Hand-sync is correct per EF semantics; `migrations remove` would corrupt __EFMigrationsHistory

## Why

**Code-review suggestions from v2.133p13:**
1. Document Integration folder anomaly (DOC-N)
2. Add architecture-suite guard for snapshot↔migration drift (ARCH-GUARD-N)

**Value delivered:**
- Documentation gap closed: Integration anomaly now visible to future developers
- Automated guard prevents silent migration inconsistencies
- Test proved immediate value: caught real pre-existing drift that would remain dormant until next EF regeneration

## How

**Files Modified:**
- `docs/code-standards.md` — +50 lines (Migration Folder Convention section)
- `src/modules/workflow/.../20260429110600_AddUniqueActiveWorkflowInstanceIndex.Designer.cs` — +6 lines (ClaimedAtUtc property restoration)

**Files New:**
- `tests/architecture/AqtCoreFW.Architecture.Tests/MigrationConsistencyTests.cs` — ~234 LOC (2 [Fact] tests)

## Tests

| Category | Command | Result |
|----------|---------|--------|
| T1 Unit | (touch module = architecture suite, see T3) | T1 ≡ T3 |
| T2 Integration | (no integration scope change) | N/A |
| T3 Architecture | `dotnet test tests/architecture/AqtCoreFW.Architecture.Tests/` | 18/18 PASS (16 baseline + 2 new) |
| Build | `dotnet build src/AqtCoreFW.slnx --no-restore` | 0E/0W |

**Negative Test:** Deliberately corrupted Designer.cs (removed ClaimedAtUtc), test correctly failed. Verified error message clarity.

## Drift Fix

**Detection:**
- Test 2 (DesignerSnapshotDrift) regex-parsed `Designer.cs:XXX` lines
- Found mismatch: Snapshot had `ClaimedAtUtc` datetimeoffset NULL, Designer.cs was missing it
- Affected: Workflow module only (v2.133p12 timing issue)

**Scope & Safety:**
- Hand-fix mirrors current ModelSnapshot byte-for-byte
- No pending migrations; only regeneration would trigger Designer rewrite
- EF semantics: Designer ≠ actual migration SQL; Designer is recompiled artifact
- Safe because: migration SQL already correct (v2.133p12 was shipped + applied in test DBs)

## Lessons Learned

1. **Test value:** Architecture suite caught real drift on first run. Proves necessity of test-suite in finalize gate (per CSQ-6 v2.133p10).

2. **Documentation timing:** Integration folder anomaly should have been documented at v2.132p1 (when test-module was added). Now caught 2 patches later. Future deferred patterns: add doc note immediately.

3. **Migration generation ordering:** EF generates .Designer.cs based on current DbContext + ModelSnapshot. If another migration commits AFTER code files staged, Designer may lag. Mitigated by: (a) single-session PR, (b) architecture test detecting drift.

4. **Whitelist strategy:** Integration folder whitelist uses path-probing helper (`Contains("Migrations")`) rather than hard-coded paths. Allows future modules same structure without test modification.

## Code Review

**Score:** 9/10 APPROVED

**Feedback (4 cosmetic nits applied in-PR):**
- Comment formatting consistency (trailing punctuation)
- Test method naming clarity (DesignerSnapshotDrift_DetectsPropertyMismatch preferred over ...Drift)
- Whitelist Reason field: clarified "test-only module at v2.132p1" reference
- Logging detail: standardized assertion message format

**No P0/P1 issues.**

## Next Steps

- **DOC-N status:** ✅ CLOSED in v2.133p14
- **ARCH-GUARD-N status:** ✅ CLOSED in v2.133p14
- Future work: Consider similar guards for (a) migration timestamp ordering, (b) snapshot property coverage (CSQ-family)

## Session Metadata

- **Status:** COMPLETE ✅
- **Branch:** `main` (commit TBD, PRed to main)
- **Files changed:** 2 modified + 1 new = 3 total
- **LOC impact:** +50 (docs) + ~234 (test) + 6 (migration Designer) = +290 net
- **Build artifacts:** 0E/0W (clean)
- **Test coverage:** 100% of touched code paths verified

---

**Session end:** 2026-04-29 ~15:30 ICT
