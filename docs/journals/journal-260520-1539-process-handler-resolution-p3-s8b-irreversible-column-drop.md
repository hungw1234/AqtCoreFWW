# Journal — PHR-V2 P3 Slice 8b: RequiredPermissionCode column-drop (IRREVERSIBLE)

**Date:** 2026-05-20 15:39
**Branch:** `feat/process-handler-resolution-p3` @ `ab20b0a0` (LOCAL, 24 ahead `origin/main`)
**Version:** v2.137p80
**Slice scope:** IRREVERSIBLE M2 column-drop; S2 backup-table sole recovery source; schema hardened
**Phase 3 progress:** 8/11 → 8/11 = 72.7% (split S8 into S8a + S8b; S8a counted, S8b completes it)

---

## Outcome

Slice 8b SHIPPED LOCAL. The legacy `WorkflowTransition.RequiredPermissionCode` column is now **gone from schema**; assignment-based authz (S5 contract) is the definitive, sole, irreversible path. Down() throws NotSupportedException with explicit SQL recovery steps embedded in the exception message. S2 migration backup table is preserved as the single recovery source per plan ruling RT2-16 (KISS — no auto-restore at MVP).

**1 commit behind: branch HEAD = `ab20b0a0` (single commit, schema now hardened)**

---

## What shipped

16 files modified, +1861/-235 LOC delta. Migration new → Designer +1672L.

| Category | Files | Change |
|----------|-------|--------|
| **Migration (NEW)** | `20260520074505_DropWorkflowTransitionRequiredPermissionCode.cs` + `.Designer.cs` | Up = single `DropColumn("RequiredPermissionCode")`; Down = `throw NotSupportedException` with embedded recovery SQL (3 steps + cite S2 + plan reference) |
| **Snapshot regen** | `WorkflowDbContextModelSnapshot.cs` | RPC property + column removed (isolated delta; no S3-S8a drift bundled per A1 pre-flight) |
| **Test deletion** | `RequiredPermissionCodeToAssignmentPrepTests.cs` | Entire S2-prep test file deleted (184L, 2 facts; D2-a ratified) |
| **Test helper** | `InlineWorkflowDefinitionFactory.cs` | Delete `SetRequiredPermissionCodeAsync` method (sole consumer = deleted test file; D1 ratified) |
| **Comment guard** | `RequiredPermissionCodeToAssignmentSql.cs` | NEW LOAD-BEARING header: "consumed by S2 migration on fresh-DB bootstrap; post-S8b the live test was retired; migration chain is still sole path; do NOT modify without re-running full Workflow suite" |
| **Stale comment cleanup** | `WorkflowTransition.cs`, `WorkflowTransitionConfiguration.cs`, `ExecuteTransitionCommand.cs`, `GetAvailableTransitionsQuery.cs`, `E2ETestDataSeeder.cs` (×2 NOTE lines) | Reference S8a strip + S8b column-drop plan intent |
| **Docs sync** | `phase-03-...md`, `plan.md`, `changelog.md`, `roadmap.md`, `dependency-map.md` | Phase 3 progress 8/11 (72.7%); S8b M2 completion logged; carry-forward to S9 |

---

## Architectural hardening (schema + migration defense)

**(1) Column-drop IRREVERSIBLE design per RT2-16:**

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    throw new System.NotSupportedException(
        "S8b column-drop of WorkflowTransition.RequiredPermissionCode is IRREVERSIBLE. " +
        "Backup data preserved in [workflow].[WorkflowTransitionRequiredPermissionCodeBackup] " +
        "(populated by migration 20260519102348_RequiredPermissionCodeToAssignmentPrep). " +
        "Manual recovery only: " +
        "(1) ALTER TABLE [workflow].[WorkflowTransitions] ADD [RequiredPermissionCode] nvarchar(100) NULL; " +
        "(2) UPDATE WT SET RequiredPermissionCode = B.RequiredPermissionCode " +
        "FROM [workflow].[WorkflowTransitions] WT JOIN " +
        "[workflow].[WorkflowTransitionRequiredPermissionCodeBackup] B ON B.WorkflowTransitionId = WT.Id; " +
        "(3) optionally apply S2 Down to clear assignments + backup. " +
        "See plans/260519-0421-process-handler-resolution-v2/phase-03... §Slice 8b Risk Summary.");
}
```

**Why:** KISS philosophy (RT2-16). Auto-restore via UpDown metadata would require:
- Tracking backup table state across migration boundaries
- EF-generated boilerplate (adds hidden complexity)
- Rollback test coverage (not in MVP scope)

Manual recovery is auditable, explicit, and forces operator intent.

**(2) Load-bearing comment on S2 migration SQL:**

The file `RequiredPermissionCodeToAssignmentSql.cs` (shared by S2 migration + its integration test) is still consumed during fresh-DB bootstrap by the S2 migration. After S8b the integration test was deleted, but the S2 migration is NOT. Future slices cannot modify this SQL without running the full Workflow.Integration test suite against a fresh Testcontainers DB to re-prove the data migration path.

---

## Inherited META-LESSONS that held or evolved

**(a) S5 META-L (a): Subagent under-states — REPEATED AGAIN.**  
S8a carried 25-file estimate from S5. S8b follows same pattern: commit msg lists 16 files, ~2000 LOC looks heavy until you realize Designer is auto-gen 1672L. Actual code edits = 5 file targets + comment cleanup. Orchestrator grep-confirmed before S8b locking.

**(b) S5 META-L (b): Worktree-diff — ESSENTIAL THIS SLICE.**  
Tester reported "G1 5/5, G3 28p/1f/2skip" (core suite). But S8b removes the RPC DTO field and deletes an entire test file. Three OTHER test files in `AqtCoreFW.Tests.Integration` project (NOT in the G3 dispatch) contain literal JSON payloads with `requiredPermissionCode` key. Planner had flagged them "benign — non-strict JSON binding ignores extra payload fields." Advisor escalated: enumerate ALL files containing the removed token, run them. Result: **24p/1f/25 worktree-diff vs e4b26313 baseline = PROVEN benign** (sole fail pre-existing: `GetAvailableTransitions_ForRunningInstance:213`).

**NEW INVARIANT (pinning for S9+):** When impl removes wire-format tokens (DTO field, projection col, request shape), advisor re-enumerates ALL integration test files containing the removed token BEFORE done. "Out-of-scope by planner" is NOT a substitute for empirical evidence.

**(c) S5 META-L (c): Tests-to-impl-lock — N/A (no new tests this slice; pure deletion).**

**(d) S5 META-L (d): Memory drift — CAUGHT IN S8a.**  
Memory said "25 files to strip" (inherited from S5); grep-verified to 3 real targets. Carry-fwd: memory entries decay; always re-grep before locking plan.

**(e) S7 META-L (e): Refactor rollback-paths inherit invariants — EVOLVED THIS SLICE.**  
S8b migration introduces a NEW rollback path: Down() throws. Prior slices (S6-S7) had rollback paths in catches + early-returns; all inherited the same invariant set (tx.Rollback, clear-state, return Result.Fail). Down() is different — it's not a failure path in normal ops, it's a DELIBERATE REFUSAL. The invariant for Down() is: **throw with explicit recovery steps embedded, cite the backup table, force operator judgment.** This is an evolution from S7 template (which was catch-based). S9+ carry-fwd: when new rollback path introduced, identify which invariant class it belongs to (catch-style vs deliberate-refusal), document the guarding assumptions.

**(f) S8a META-L (Dapper):** "When removing fields from Dapper-materialized DTOs, RE-RUN integration test BEFORE shipping." This was about DTO changes. S8b is about DB column changes — Dapper does NOT care about DB columns that went away (NULL handling upstream, late-materialization = fewer cols = NO-OP). So Dapper META-L does NOT apply to S8b. But the BROADER lesson is: **field removal can have silent ripple effects upstream; always verify full integration chain.**

**(g) S6 D1-α advisor catch (boundary preservation):** N/A this slice (no new boundary surfaces).

---

## 🆕 NEW META-LESSON #1: Pre-finalize advisor enumeration gate

**Discovery:** Tester reported 138 total facts (G1 5 + G2 101 + G3 31 + G4 1 = 138). But that's only 6 facts from `AqtCoreFW.Tests.Integration` project. The project is broader; planner had cataloged 3 additional files:
- `WorkflowInstancesApiTests.cs`
- `WorkflowDefinitionsApiTests.cs`
- `WorkflowDefinitionRbacTests.cs`

These carry JSON literals with `requiredPermissionCode`. Planner's rationale: "benign — non-strict JSON binding ignores extra payload fields." **That's a prediction, not evidence.** Advisor escalated: run worktree-diff on these 3 files vs baseline. Result: 24p/1f/25 across all 3 files, identical baseline (sole fail pre-existing).

**Why this matters:** When S8b strips a DTO field, JSON parsers MAY ignore the missing key (if deserialization is lenient). But the reverse — tests SENDING the old key after the field is gone — might seem harmless until the endpoint is tested under a stricter serializer, or the DTO is migrated to strict binding. Empirical proof matters.

**Carry-fwd invariant (pinning for S10+):** Advisor pre-finalize MUST enumerate all files containing the removed token BEFORE signing off. Grep pattern: `grep -r "requiredPermissionCode" {test-project}/ | grep -E "(json|JSON|body|payload)" | cut -d: -f1 | sort -u`. For each file, run worktree-diff to establish baseline. This is a MUST, not a SHOULD.

---

## 🆕 NEW META-LESSON #2: EF model drift accumulation across slices

**Discovery:** S3-S4 removed the `WorkflowTransition.RequiredPermissionCode` property from the domain entity without an accompanying migration (per plan — the column was LIVE, just orphaned). The snapshot continued to list the property definition (lines 1237-1239 of the pre-S8b snapshot) for ~5 slices. At S8a, code removal was complete. At S8b, migration time: `dotnet ef migrations add ...` reads the snapshot and entity class, finds drift (property gone, but snapshot lists it), and pre-flight `has-pending-model-changes` should have caught it.

**Fix applied:** A1 advisor constraint = **run `dotnet ef migrations has-pending-model-changes` BEFORE `migrations add`**. Result: PASS. Drift was clean (only RPC property removal, no S3-S8a side-effects bundled). Migration bundle = isolated.

**Why:** EF migrations bundle ALL model-to-db divergences into the migration file. If S3-S8a introduced side-effect drifts (e.g. AccidentallyRemovedColumnX from S6 that was reverted in S7), the S8b migration would hide them. Pre-flight check forces audit clarity.

**Carry-fwd invariant (pinning for S9+):** Before `migrations add` on any schema-touching slice, always run:
```bash
dotnet ef migrations has-pending-model-changes -p <Persistence.csproj>
```
This must report ZERO unaccounted divergences. If not, investigate before bundling.

---

## 🆕 NEW META-LESSON #3: FQN-based test failure tracking across projects

**Discovery:** Baseline worktree-diff reported "GetAvailableTransitions_ForRunningInstance failure pre-existing at line:213." Memory from S3 log said "SimultaneousRace pre-existing" in `Workflow.Integration.Tests` project. But during S8b advisor audit, we ran three ADDITIONAL test files from `AqtCoreFW.Tests.Integration` project (different project!). One of them also has a test called `GetAvailableTransitions_ForRunningInstance_Returns200WithTransitions` — **different test, same conceptual name, SAME assertion failure at 213.**

**Risk:** Memory says "SimultaneousRace is the pre-existing issue" (short name match). Code review says "don't break it" (ok). But if two tests with similar intent fail identically across projects, **the problem is probably not "pre-existing concurrency" but something structural about the test or assertion.**

**Fix applied:** Advisor verified by FQN + file path, not just test name. Both failures are legitimate pre-existing (proven via worktree-diff on exact HEAD^ vs HEAD), but they're NOT the same test.

**Carry-fwd invariant (pinning for S10+):** When baseline failures are cited, **always include FQN + file path, not just short name.** Test names can be reused across projects. Prevent future conflation by citing:
```
PreExistingFail: {namespace}.{class}.{method} @ {file}:{line}
```

---

## Cost analysis

- **Plan:** ~3 min (S8 scope already scoped S8a + S8b at plan-time)
- **Decisions:** ~2 min (D1 + D2-a ratification; S7 precedent applicable)
- **Migration file:** ~10 min (craft Down() exception with recovery SQL + cite backup; 3 recovery steps)
- **Snapshot + cleanup:** ~5 min (RPC removal isolated; grep stale comments)
- **Test deletion:** ~3 min (D2-a apply: delete file vs gut; KISS wins)
- **A1-A3 gates:** ~5 min (pre-flight + inspect + comment guard)
- **Test verification (G1-G4):** ~10 min (baseline establishment, worktree-diff on 3 surprise files, PROOF)
- **Advisor pre-finalize audit:** ~15 min (enumerate RPC-containing files, run worktree-diff on 3 files, establish baseline)
- **Commit + docs + journal + memory + carry-fwd:** ~10 min

**Total: ~63 min for a single-migration slice.** Most time = advisor pre-finalize enumeration (15 min, NEW META-LESSON #1 earned) + test verification (10 min, worktree-diff proof) + migration exception crafting (10 min, recovery steps).

**Insight:** S8b is TINY in code but CRITICAL in schema finality. The careful pre-finalize enumeration and Down() exception design is insurance against silent data-loss in prod. Worth the time.

---

## Decisions ratified (user gate Step 3)

| # | Choice | Why |
|---|--------|-----|
| D1 | Delete helper method `InlineWorkflowDefinitionFactory.SetRequiredPermissionCodeAsync` | Sole consumer = deleted test file (RequiredPermissionCodeToAssignmentPrepTests.cs); DRY cleanup |
| D2-a | Delete entire S2 test file (184L, 2 facts) vs Skip+gut | KISS principle; S7 precedent (ParallelBranchServiceStageOnlyTests.cs); no residual value in retaining empty test class |
| Schema | DOWN() throws NotSupportedException with embedded recovery SQL | Plan ruling RT2-16 (KISS, no auto-restore at MVP); forces operator intent + makes rollback auditable |
| Push | LOCAL only | Branch already 24 ahead; default per S5/S6/S7/S8a; merge-gate at S11 enforces atomicity |

---

## Carry-forward to Slice 9 (EscalationCheckJob pool-safety + aging)

- **Fresh-context slice:** S9 is independent of schema changes; brings new domain logic (job scheduling)
- **S8b invariants DO NOT carry to S9:** S8b was about finality (column gone); S9 is about runtime behavior
- **Apply META-L #1 (pre-finalize enumeration) only if S9 touches wire-format:** EscalationCheckJob is internal scheduling, no wire-format removal expected
- **Apply META-L #2 (EF drift audit) if S9 adds new entity properties:** Always pre-flight `has-pending-model-changes` before `migrations add`
- **S8b completion note:** Migration chain S2→S3→...→S8b is now locked; backup table in place; schema hardened

**Merge-gate to S11:** The 5 fine-resolver tests (G1 WorkflowExecuteTransitionAuthzGateTests 5/5 + WorkflowFineResolverGateTests 5/5) remain as the merge-gate proof that assignment-based authz is sole path. S11 must re-run these before final push.

---

## Unresolved

None. Schema is hardened. Backup strategy is in place. Recovery instructions are embedded.

---

## Test gate matrix (S8b baseline locked)

| Gate | At S8b ship | Baseline | Notes |
|------|-------------|----------|-------|
| Build | 0E/0W | locked | Migration script verified; no compilation errors |
| G1 ExecuteTransitionCommandHandlerTests | 5/5 | locked | S8a baseline carries; RPC removal verified at S8a |
| WorkflowFineResolverGateTests | 5/5 (incl A7 wire-spec-lock POST-DROP) | locked | A7 proves wire-format integrity post-RPC-strip |
| G2 Architecture | 101/101 | locked | No arch violations; schema changes do not touch allowlist |
| G3 Workflow.Integration | 28p/1f/2skip | e4b26313 | SimultaneousRace pre-existing (S7 worktree-diff proof) |
| G3+ WorkflowInstancesApiTests | 24p/1f/25 total | e4b26313 | Worktree-diff PROVEN identical baseline; RPC literal in JSON benign (non-strict binding) |
| G3+ WorkflowDefinitionsApiTests | (included in 24p/1f/25) | e4b26313 | Worktree-diff PROVEN; pre-existing fail at :213 (different file, same symptom) |
| G3+ WorkflowDefinitionRbacTests | (included in 24p/1f/25) | e4b26313 | Worktree-diff PROVEN identical |
| G4 SchemaUniversalsTests.PageCompression | 1/1 | locked | Backup table PAGE-compressed per S2 design |

---

## Code-review outcome

**APPROVE-WITH-NITS 9.4/10** (0 Crit / 0 High / 0 Med / 2 Low)

**Low-1 (deferred S10):** Orval regen will sweep `requiredPermissionCode` literals from generated `.ts` types. Three integration test files will be auto-clean on next regen. Not critical because JSON binding is lenient (already proven benign).

**Low-2 (deferred S10):** E2ETestDataSeeder comment lines reference "S8b column-drop"; after merge to main these comments can cite commit hash instead of plan reference. Low priority.

**Why 9.4 and not 10:** Pre-finalize advisor enumeration is NEW this session (META-L #1); it's correct and PASSED, but it represents a PROCESS addition (advisor responsibilities expanded). Code itself is clean.

---

## Phase 3 summary (post-S8b)

**Slices completed:** S1, S2, S3-S4 (bundled), S5, S6, S6.1, S7, S7.1, S8a, S8b = 10 distinct delivery units
**Progress:** 8/11 = 72.7% (S9, S10, S11 remain)
**Schema state:** Column-drop finalized; backup-table in place; S2→S8b migration chain locked
**Code state:** Assignment-based authz is sole path (proven G1 5/5 + A7); RPC property removed; wire-format spec-locked
**Merge readiness:** All 5 fine-resolver tests pass; migration script defensible; recovery path documented

**Next slices:**
- **S9:** EscalationCheckJob pool-safety + aging (independent, fresh-context)
- **S10:** 3-layer override completion + GetUserActiveStatusAsync live + orval regen (cleanup + side-effect sweep)
- **S11:** Atomic-merge gate verify (5 fine-resolver tests + migration script audit + GH UI review)

---

## Metadata for S9+ planning

- **Carry-forward NEW invariants (3):**
  1. Pre-finalize advisor enumeration: enumerate ALL files containing removed token; run worktree-diff; NO exceptions
  2. EF drift audit: pre-flight `has-pending-model-changes` before `migrations add` to detect silent accumulation
  3. FQN-based test failure tracking: short name conflicts across projects; cite {namespace}.{class}.{method} + {file}:{line}

- **Carry-forward EVOLVED invariants (1):**
  4. Rollback-path invariant classes: catches vs deliberate-refusal (S8b Down() pattern); document guarding assumptions per class

- **Retired invariants (0):** S5-S8a META-LESSONS remain in effect; none sunset.

- **Risk summary S9-S11:** Merge-gate is critical (single migration column-drop + 5 core tests). S10 orval regen is side-effect sweep (non-critical). S11 is final audit before push.
