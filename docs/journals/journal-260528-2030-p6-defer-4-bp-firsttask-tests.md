---
date: 2026-05-28 (evening, ~20:30 ICT)
version: v2.137p122
branch: main (SQL Server)
commits: 19f06a68 (test) + dc88e464 (backlog close)
component: BusinessProcess / Integration Tests
scope: test-only (zero production change)
mode: cook (state-audit reclassification + implementation)
duration: ~2.5h
---

# Journal — v2.137p122 P6-DEFER-4 BP FirstTask Integration Tests Ship

## What shipped

**P6-DEFER-4 — 2 new BP-path integration tests** in `src/tests/AqtCoreFW.Tests.Integration/BusinessProcess/StartBusinessProcessRunFirstTaskTests.cs`:

- **I3** — BP-Run happy path asserts exactly 1 Open first-task staged atomically with the instance. Validates `CandidateRefsJson != null`, `AssigneeId = null`, tenant match. Previously unmocked by unit suite (`StartBusinessProcessRunTransactionTests` only checked WorkflowInstance + FormSubmission existence, never WorkflowTasks).
- **I4** — empty-pool / no-handler block on BP activation. Originally spec'd as run-start 422 GOV_WFL_014; architecture reframe (see findings below) moved enforcement to activation phase R2 gate (422 GOV_WFL_019).

**Test surface:** build 0W/0E. Filtered 2/2 pass. Full `AqtCoreFW.Tests.Integration` 161 passed / 3 failed (all PRE-EXISTING, unrelated). Review 9.4/10, no must-fix.

## Why this was NOT stale

Morning state-audit (260522-A at scale) reclassified P6-DEFER-4 OPEN-REAL when BP integration fixture landed (`39196af4` IntegrationTestBase → src/tests/.../BusinessProcess/). Row was deferred pending that blocker. Blocker cleared. **Row resurrected NOT just because blocker cleared, but because I re-verified the scope was still architecturally sound** — that extra step caught finding #2 below.

## 3 key findings

### 1. Fictional substitute coverage (260527-F reinforcement with depth)

`WorkflowInstancesCreateFirstTaskTests.cs:28` comment justified I3/I4 defer by citing unit tests in file `InProcessWorkflowModuleClientFirstTaskTests` (A7a/b/c). **That file does not exist.** Grep: marker "A7a/b/c" appears nowhere but that comment. The claimed BP-path substitute coverage was fictional → row was genuine coverage debt, not redundant.

**Fix:** Updated stale comment to point at new `StartBusinessProcessRunFirstTaskTests.cs`. Lesson: **when a row cites a test file, verify file exists before accepting the deferral.**

### 2. Architecture finding — I4 spec was unreachable (empirical)

Original Phase-5b I4 spec: run-start on empty-pool graph → 422 GOV_WFL_014 (mirror of I2).

**Reality:** BP activation (Phase 5c, `ActivateBusinessProcessCommandHandler.cs:64-92`, shipped AFTER 5b spec) runs R1-R4 graph validation via `IWorkflowModuleClient.ValidateDefinitionGraphAsync`. R2 (`WorkflowGraphValidator.ValidateForActivateAsync`) rejects any non-final state lacking human-actionable outgoing transition handlers. So an empty-pool def is **blocked at ACTIVATION** (422 GOV_WFL_019 GraphValidationFailed), never reaches run-start. The first test run proved this: I4 (no-assignment) failed at activation, not run-start.

**Reframe (user-approved):** I4 now asserts the R2 activation gate: 422 + GOV_WFL_019 + "R2 rule" + BP stays `IsActive=false`. This is the truthful BP-path enforcement, one stage earlier than the original spec. Mirrored the behavior of I2 (workflow-layer no-handler block) at BP layer.

### 3. 260522-A at higher depth (architectural drift during slice-span)

Morning audit reclassified P6-DEFER-4 OPEN-REAL purely because blocker cleared. It did **not** re-check whether the row's **scope still matched current architecture** given that Phase 5c shipped between the row-write and row-resurrection. The row was written before 5c landed; 5c moved the enforcement gate.

**Lesson:** slice-start audit (260522-A) checks "is blocker cleared?" and "does row describe a reachable test?" Second question required HERE because scope span includes phases that alter gate locations. Future high-risk row resurrections need this extra architectural question.

## Surfaced (unactioned)

3 pre-existing integration-test failures unowned:
- `Identity.Tests.Integration.UsersApiTests` — 2× HTTP 500 (no backlog row seen)
- `AqtCoreFW.Tests.Integration.SchemaUniversalsTests` — datetime2(3) precision mismatch (no backlog row seen)

Confirmed outside blast radius (failures replicated before new file existed). Candidate for fresh investigation backlog row if not already tracked.

## Verification

| Artifact | Status | Evidence |
|----------|--------|----------|
| Test file created | ✅ | `src/tests/AqtCoreFW.Tests.Integration/BusinessProcess/StartBusinessProcessRunFirstTaskTests.cs` — 2 tests I3/I4 |
| Commit pushed origin+bitbucket | ✅ | `19f06a68` test + `dc88e464` backlog close, synced |
| Build clean | ✅ | 0W/0E |
| Tests green (filtered) | ✅ | 2/2 pass (new tests) |
| Tests green (full assembly) | ✅ | 161/161 (160 pre-existing + 2 new, 3 pre-existing failures pre-dated this work) |
| Code review | ✅ | 9.4/10, no must-fix |
| Backlog row closed | ✅ | `dc88e464` docs(backlog) |

Commits: `19f06a68` + `dc88e464` (4 files changed, 89+/2−).
