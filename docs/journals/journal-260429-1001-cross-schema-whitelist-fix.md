# Session Journal — v2.133p9: Architecture Test Whitelist Fix

**Date:** 2026-04-29 | **Version:** v2.133p9 | **Branch:** main | **Status:** SHIPPED

---

## Incident Summary

**v2.133p5 regression:** `TenantBackfillSeeder` (one-shot IHostedService seed at startup) shipped with cross-schema raw SQL reference `FROM [organization].[OrgUnits]` without whitelist entry in architecture guard.

**Guard timeline:**
- **v2.132p10 (2026-04-28):** Architecture test `NoModule_Should_Reference_AnotherModuleSchema_InRawSql` activated.
- **v2.133p5 (2026-04-28):** `TenantBackfillSeeder` shipped → violation flagged by guard, but test suite NOT run during finalize.
- **v2.133p6–p8 (2026-04-28 to 2026-04-29):** Regression silent for 4 patches (RBAC refinements). No one ran full architecture suite.
- **v2.133p9 (2026-04-29):** Regression detected. Whitelist entry added; all tests GREEN.

**Root cause:** Finalize checklist (Cook) runs unit/integration tests per-module only — not cross-module design guards. Architecture violations treated as optional QA, not compile-time contract errors.

---

## Trade-off Analysis: Whitelist vs Refactor

### Option A: Whitelist (CHOSEN)

**Code change:** 1 whitelist entry (~5 lines) in `CrossSchemaSqlScanner.cs:84-85`:
```csharp
new("TenantBackfillSeeder.cs", "organization",
    "Reason: one-time IHostedService backfill — derives Tenants from DISTINCT TenantId across AspNetUsers ∪ OrgUnits. Runs at startup, not request-path; no IOrgUnitModuleClient surface fits one-shot backfill query (would create dead client API per YAGNI).")
```

**Precedent:** 5 other seeders already whitelisted (BtcIdentitySeeder.Users, BtcAssignmentSeeder, BtcOrgUnitSeeder.L4Treasury, ReportDefinitionSeeder, OllamaReActAgent).

**Rationale:**
- Zero surface API change.
- YAGNI-aligned: no client method reuse.
- Drift control: Reason field forces code-review scrutiny of new entries.
- One-time startup query scope inherently different from request-path cross-schema joins.

### Option B: Refactor through IOrgUnitModuleClient (REJECTED)

**Code change:** NEW client method `IOrgUnitModuleClient.DeriveTenantsAsync(tenantId: Guid.Empty)` + 3 implementation files + 1 test.

**Caller:** TenantBackfillSeeder only (once per deploy at startup).

**Verdict:** Dead client API. Anti-pattern per YAGNI. Adds abstraction burden for zero reuse.

---

## Lesson: Design Guards Are Compile Errors

Architecture violations (cross-schema coupling, circular deps, reserved interface names) are **not lint warnings**—they are **compile-time contract enforcement** for maintainability at scale. Severity parity with syntax errors.

**Current finalize gate misses this:**
- ✅ Unit tests per-module
- ✅ Integration tests per-module  
- ❌ Cross-module design guards (architecture suite)

**Fix (CSQ-6, P2):** Add `dotnet test tests/architecture/` to Cook finalize test gate (before version-bump PR).

---

## Test Results

| Suite | Result | Notes |
|-------|--------|-------|
| Architecture (target) | 16/16 PASS | NoModule_Should_Reference_AnotherModuleSchema_InRawSql + 15 others |
| Architecture (full) | 16/16 PASS | All cross-module guards GREEN |
| Identity.Infrastructure | 66/66 PASS | Module unit tests (no changes this PR) |
| Build | 0E/0W | Clean |

---

## Files Changed

1. **tests/architecture/AqtCoreFW.Architecture.Tests/Helpers/CrossSchemaSqlScanner.cs** (line 84-85)
   - Added `TenantBackfillSeeder.cs → organization` whitelist entry with detailed Reason.

---

## Follow-ups

1. **CSQ-6 (P2, ≈30min):** Add architecture suite to Cook finalize test gate.
   - Created NEW memory file: `memory/feedback-architecture-tests-in-finalize-gate.md` (2026-04-29).
   - Backlog entry added to `backlogs/backlog.md`.
   - Artifact updated: `memory/MEMORY.md`.

2. **AUD-2 (P2, ≈0.5d):** Cancel semantics polish candidate for next phase (low-priority).

---

## Artifacts Synced

| Artifact | Change | Evidence |
|----------|--------|----------|
| 1. docs/dependency-map.md | N/A | Test infra only; no entity/endpoint/client change. |
| 2. docs/project-roadmap.md | ✅ UPDATED | v2.133p8→v2.133p9 header; new phase row added. |
| 3. docs/project-changelog.md | ✅ UPDATED | v2.133p9 entry (3 paragraphs: root cause, fix, trade-off, lesson). |
| 4. backlogs/backlog.md | ✅ UPDATED | Header v2.133p8→v2.133p9; CSQ-6 entry added. |
| 5. memory/project-state.md | ✅ UPDATED | Version bump + incident summary + lesson + follow-up pointer. |
| 6. memory/MEMORY.md | ✅ UPDATED | project-state.md row description updated with v2.133p9 summary. |
| 7. Session journal | ✅ NEW | This file (journal-260429-1001-cross-schema-whitelist-fix.md). |

---

## Commit Readiness

- **Branch:** main (v2.133p8 HEAD)
- **Uncommitted changes:** 7 doc files + 1 code file (CrossSchemaSqlScanner.cs)
- **Build status:** CLEAN (0E/0W)
- **Tests:** ALL PASS (architecture + identity.infra)
- **Message:** `fix(architecture): TenantBackfillSeeder cross-schema whitelist + finalize-gate lesson (v2.133p9)`

---

**Session end time:** 2026-04-29 10:01 UTC | **Duration:** ~20min | **Status:** Ready for commit + push.
