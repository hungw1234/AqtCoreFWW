# Session Journal — OUTBOX-1 Finalize (v2.133p11)

**Date:** 2026-04-29 (evening, ~11:37 ICT)  
**Project:** AqtCoreFW (SQL Server)  
**Version:** v2.133p11  
**Agent:** docs-manager (finalize sync)

---

## Summary

Finalized OUTBOX-1 implementation (unique filtered index + idempotent consumer catch) with 7-artifact documentation sync per primary-workflow.md Section 7 finalize gate. All artifacts updated with v2.133p11 version strings, backlog marked DONE, memory updated with new state entry, session journal created.

---

## Implementation Context (Prior Completions)

**OUTBOX-1 shipped v2.133p11 (2026-04-29):**
- Unique filtered index `UX_WorkflowInstances_Active_Entity` on `workflow.WorkflowInstances (EntityType, EntityId, TenantId) WHERE CompletedAt IS NULL`
- Idempotent consumer catch in `CreateWorkflowInstanceOnCaseCreatedConsumer` with reflection-based `IsDbUpdateUniqueConstraintViolation` helper
- Eliminates TOCTOU race where two replicas both saw no instance → both called `CreateInstanceByDefinitionKeyAsync` → duplicate rows
- Now atomic DB constraint fails, caught + idempotently re-probed
- OCE parity filter prepended per v2.133p10 AUD-2 pattern

**Test Results:**
- T1 (unit): 450/450 PASS (441/441 baseline + 9/9 race)
- T2 (integration): 23/27 PASS (deferred UniqueActiveInstanceIndexTests P3, 2 pre-existing auth unrelated, 2 skip)
- T3 (architecture): 16/16 PASS
- Build: 0E/0W

**Code Review:** APPROVED_WITH_MINOR (8/10) — P1 OCE parity fixed in-PR

---

## 7-Artifact Finalize Sync

### Artifact 1: docs/dependency-map.md
**Updated:** Line 3 (version header) + Lines 189-194 (new OUTBOX-1 section in Workflow module)
- Version: v2.133p8 → v2.133p11
- Added comprehensive OUTBOX-1 notes: index definition, consumer catch pattern, OCE parity, test coverage, integration details
- Deferred UniqueActiveInstanceIndexTests documented as P3 backlog

**Evidence:** Version line now reads `**Version:** v2.133p11 (SQL Server primary). OUTBOX-1 shipped:...`

### Artifact 2: docs/project-roadmap.md
**Updated:** Line 3 (header version pinned) + Table insertion (v2.133p11 row before v2.133p10)
- Version pinned: v2.133p10 → v2.133p11
- New row in phase table with OUTBOX-1 summary, test results, deferred follow-up
- Placement: newest version first (v2.133p11, then v2.133p10, then v2.133p9, etc.)

**Evidence:** Header now reads `...pinned @ v2.133p11...` and v2.133p11 row inserted at line 10

### Artifact 3: docs/project-changelog.md
**Updated:** Lines 5-139 (new v2.133p11 section before v2.133p10)
- Comprehensive v2.133p11 changelog entry
- Sections: Scope, Key Changes (3 numbered points with code pattern), Test Coverage (T1/T2/T3 breakdown), Code Review, Files Changed (4 modified + 3 new), Impact Analysis
- Includes motivation (TOCTOU race), implementation detail, test results, deferred backlog items

**Evidence:** New v2.133p11 section spans 140+ lines before v2.133p10

### Artifact 4: backlogs/backlog.md
**Updated:** Line 2 (version header) + Lines 79-81 (OUTBOX-1 checkbox + OUTBOX-1-FU row + OUTBOX-1-DISP row)
- Version header: v2.133p9 → v2.133p11
- OUTBOX-1 checkbox: `[ ]` → `[x]` (DONE v2.133p11)
- OUTBOX-1 description updated: comprehensive summary of shipped solution
- OUTBOX-1-FU (new row): Deferred integration test (P3, ~0.25d)
- OUTBOX-1-DISP (new row): Separate dispatcher claim race issue (UPDLOCK weakness, P1-1 deferred)

**Evidence:** Line 79 now reads `- [x] **OUTBOX-1: Consumer race collapse via unique filtered index** — SHIPPED v2.133p11...`

### Artifact 5: memory/project-state.md
**Updated:** Frontmatter description (lines 2) + "Last Updated" section (lines 8-9, new + bumped to line 9 with "Previous" demoted)
- Frontmatter description: v2.133p9 → v2.133p11 with comprehensive summary
- Last Updated section: NEW entry for v2.133p11 (evening session) with full context
- Previous section: v2.133p10 entry demoted below v2.133p11

**Evidence (Frontmatter):** Now reads `**v2.133p11 SHIPPED on \`main\`** (2026-04-29 evening, SQL Server) — OUTBOX-1 race collapse...`

**Evidence (Body):** Line 9 now reads `2026-04-29 (evening, TBD-FINALIZE) — **v2.133p11 SHIPPED on \`main\`** — OUTBOX-1 race collapse.` (full detailed section follows)

### Artifact 6: memory/MEMORY.md
**Updated:** Memory index table, project-state.md row description (line 11)
- Updated row to reference v2.133p11 (was v2.133p10)
- Summary updated with OUTBOX-1 context + prior version chain

**Evidence:** Line 11 now reads `| [project-state.md](project-state.md) | project | **v2.133p11 SHIPPED on \`main\`** (2026-04-29, SQL Server) — OUTBOX-1 race collapse...`

### Artifact 7: docs/journals/journal-260429-1137-outbox1-unique-active-instance-index.md
**Created:** New session journal file (this file)
- Comprehensive technical entry documenting finalize execution
- Sections: Summary, Implementation Context, 7-Artifact Sync details, Key Decisions, Deferred Items, Lessons
- Follows existing journal format (AqtCoreFW codebase convention)

**Evidence:** File exists at `C:\AqtCoreFW\docs\journals\journal-260429-1137-outbox1-unique-active-instance-index.md` (1 session)

---

## Key Decisions Locked During Finalize

**Q1: Index Filter Scope**
- **Decision:** WHERE CompletedAt IS NULL (active instances only)
- **Rationale:** Only active instances can participate in race (completed instances immutable). Large historical table scan avoided via filtered index.
- **Trade-off:** Slight complexity (filter clause) vs performance (avoids 100+ million completed rows on mature tenant)

**Q2: Consumer Catch Pattern**
- **Decision:** Reflection-based `IsDbUpdateUniqueConstraintViolation` helper (SqlException Number detection)
- **Rationale:** Type-safe, composable, future-proof for additional SQL Server error codes
- **Alternative considered:** `DbUpdateException.GetDatabaseError()` — less portable across DB providers
- **Trade-off:** Complexity (reflection) vs clarity + testability

**Q3: OCE Prepending Order**
- **Decision:** OCE filter BEFORE unique-violation filter
- **Rationale:** Defensive against future filter broadening per v2.133p10 AUD-2 precedent. Cancellation always takes priority over application-level recovery.
- **Context:** v2.133p10 established pattern: prepare for filter expansion by front-loading explicit exit conditions

**Q4: Deferred Integration Test**
- **Decision:** Defer UniqueActiveInstanceIndexTests to P3 backlog entry OUTBOX-1-FU
- **Rationale:** Reflection-against-fake unit tests + happy-path T2 regression sufficient for ship. Real-DB integration test verifying SqlException 2627/2601 end-to-end (TestContainer + intentional duplicate) not critical for v2.133p11 — technical debt tracked explicitly.
- **Risk:** False sense of security if no realistic duplicate ever triggers in prod (statistically unlikely but possible). Mitigation: OUTBOX-1-FU opened, will execute when: (a) integration test infra matures, or (b) new collision scenario discovered.

---

## Deferred Follow-ups

1. **OUTBOX-1-FU** (P3, ~0.25d): Dedicated `UniqueActiveInstanceIndexTests.cs` integration test verifying real duplicate INSERT triggers SqlException 2627/2601 end-to-end. Deferred from v2.133p11 finalize; acknowledged by user 2026-04-29.

2. **OUTBOX-1-DISP** (P1-1, ~0.5d): Dispatcher claim race — UPDLOCK weakness separate from consumer race. Pre-existing: `OutboxDispatcher` acquires UPDLOCK on SELECT in autocommit (released after SELECT), then UPDATE in separate implicit tx. Multi-replica contention → duplicate publishing. Deferred from v2.133p1; low priority if Phase 2 consumer idempotence (MassTransit `UseInMemoryOutbox` dedup) verified.

3. **RBAC-4** (P3, ~1h): Cleanup INSERT-only seeder orphan rows. `SeedPermissionsAsync` + `SeedRolePermissionsAsync` only INSERT missing, never DELETE. When perm codes are retired, orphan rows remain. Separate cleanup migration needed. Tracked in backlog, deferred per RBAC v2.133p8 finalize.

---

## Lessons Encoded

**1. Reflection-based Exception Detection**
- Pattern: `IsDbUpdateUniqueConstraintViolation(DbUpdateException ex)` helper enables portable error detection without static-type coupling to `SqlException`
- Applicable to: Any module catching DB exceptions in dependency-light layers (Application, Domain)
- Codebase precedent: None yet — OUTBOX-1 establishes pattern for future migration/constraint scenarios

**2. Defensive OCE Filtering**
- Pattern: Prepend `catch (OperationCanceledException) when (ct.IsCancellationRequested)` BEFORE recovery filters
- Established v2.133p10 AUD-2; now pattern-locked in OUTBOX-1 consumer
- Rationale: Cancellation is process-level intent, supersedes application recovery logic
- Codebase precedent: AuditService.cs:72, SubmitFormCommandHandler.cs:177, SubmitPublicFormCommandHandler.cs:172 (v2.133p10); now CreateWorkflowInstanceOnCaseCreatedConsumer.cs (v2.133p11)

**3. Filtered Indexes for Race Collapse**
- Pattern: Unique filtered index on active rows only, leverages domain knowledge (completed instances immutable)
- Codebase first use — future candidates: [any race on "active" subset of large table]
- Trade-off: Filter complexity vs query performance (avoids scanning completed rows)
- Lesson: Domain modeling enables schema optimization (filter clause = DDD ubiquitous language artifact)

**4. Deferred Integration Test Discipline**
- Decision: Accept technical debt (UniqueActiveInstanceIndexTests deferred) rather than extend finalize window
- Tracking: Explicit backlog entry (OUTBOX-1-FU) with effort estimate + trigger conditions
- Codebase pattern: Aligns with Ai module deferred wire-ups (AI-W1..W8), Rules module deferred consumers (R-W1..W4)
- Lesson: YAGNI-compliance requires explicit deferred-list + trigger documentation, not silent technical debt

---

## Artifacts Synced (Verification Evidence)

All 7 artifacts verified updated this session:

1. ✅ `docs/dependency-map.md` — version line + Workflow section OUTBOX-1 notes
2. ✅ `docs/project-roadmap.md` — header version pinned + v2.133p11 table row
3. ✅ `docs/project-changelog.md` — comprehensive v2.133p11 section (lines 5-139)
4. ✅ `backlogs/backlog.md` — version header + OUTBOX-1 checkbox [x] + OUTBOX-1-FU + OUTBOX-1-DISP rows
5. ✅ `memory/project-state.md` — frontmatter description + new "Last Updated" section (v2.133p11)
6. ✅ `memory/MEMORY.md` — project-state row updated to v2.133p11
7. ✅ `docs/journals/journal-260429-1137-outbox1-unique-active-instance-index.md` — session journal (this file)

**No conflicts detected.** All edits orthogonal, no overlapping lines modified across artifacts.

---

## Notes for Next Session

- **Version bump ready:** Main branch pinned v2.133p11. All 7-artifact finalize complete. Ready for git-manager to commit + push (pending user approval per orchestration protocol).
- **Deferred backlog clarity:** OUTBOX-1-FU (P3 integration test) and OUTBOX-1-DISP (P1-1 dispatcher race) split clearly in backlogs/backlog.md. Clear trigger conditions + effort estimates for future resumption.
- **Codebase patterns established:** Reflection-based exception detection + defensive OCE filtering + filtered index optimization now locked patterns. Reference OUTBOX-1 v2.133p11 when implementing similar scenarios.
- **Test coverage status:** T1 450/450 ✅, T2 23/27 (acceptable gap for deferred integration), T3 16/16 ✅ per CSQ-6 v2.133p10 mandatory gate.

---

## Session Metadata

**Agent:** docs-manager (finalize sync)  
**Work Context:** C:\AqtCoreFW  
**Reports:** C:\AqtCoreFW\plans\reports\  
**Branch:** main  
**Artifacts Modified:** 7/7 ✅  
**Duration:** TBD  
**Status:** COMPLETE (pending git-manager commit)

---
