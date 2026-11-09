# Journal — SaveDefinitionGraph carries optional WorkflowTransitionAssignment (plan 260523-1241)

**Date:** 2026-05-23 (single-session, ~10h)
**Plan dir:** `plans/260523-1241-extend-save-graph-with-transition-assignment/`
**Bundle:** 8 commits local-only (Phase 02-07), eligible for push after this Phase 08 docs sync.

## What shipped

Closes the **shipped-validator-before-shipped-write-path gap** introduced by P5c (v2.137p90, 2026-05-21). Phase 5c shipped R2/R3/R4 enforcement at the BP-Activate path **before** any production write-path existed for `WorkflowTransitionAssignment` — every WF def created via current `SaveDefinitionGraph` would 422 GOV_WFL_019 on Activate.

8 commits across 7 phases:

| Phase | Commit | Scope |
|---|---|---|
| 02 | `b20ffa76` | BE contracts — `AssignmentInput`, `AssignmentClauseInput`, `NewTransitionSpec.Assignment`, `WorkflowTransitionAssignmentDto` (5 files +110/-3, 539/539 tests, 9.4/10) |
| 03 | `2ab309f6` | Validator R2/R3/R4 + spec record→class for canonical writeback mutation (547/547, 9.2/10) |
| 04 | `4c79f46e` | Repo `ReplaceGraphAsync` Phase 3 INSERT + pre-flight cascade DELETE (no DB-FK on `WorkflowTransitionAssignments.WorkflowTransitionId`) + atomicity proof via `ThrowOnNthSaveInterceptor` (84/84, 9.3/10) |
| 05 | `38eb7d26` | Handler wires `input.Assignment → spec.Assignment`, `MapToDto` projects from spec (post-canonical) — closes the input→spec pipe (552/552 + 5 new H-A1..H-A5, 9.2/10) |
| 06a | `dbe8392f` | New `GET /api/v1/security/perm-codes` (`[Authorize]` no policy) + Phase 05 M1 fold (MapAssignment null-guard for `{"clauses":null}` payload) (Architecture 105/105, 9.4/10) |
| 06b | `5a12cba0` | `useWorkflowPermCodes` hook + `WorkflowUserMultiSelect` component (Vitest 7/7, 9.2/10 post C1-fix) |
| 06c | `e4484993` | `TransitionAssignmentEditor` Collapse + Form.List + 3 clause modes + AllowAssignorOverride + serializer pass-through (Vitest 12/12, 8.8/10 → M1+M2 folded) |
| 07 | `0ef71798` | 5 E2E integration tests (I_SaveAct_A1..A5) + H1 merge gate fix on existing test body (6/6, 9.4/10) |

## Tests delta

- Workflow.App: 533 → 553 (+20: P02 C1-6 +6 / P03 S-R2..S-Necessary +8 / P05 H-A1..H-A6 +6)
- Workflow.Infra: 80 → 84 (+4: P3_A1..A4 atomicity facts)
- Workflow.Integration: +5 new (I_SaveAct_A1..A5) + 1 H1 fix (SaveDefinitionGraphIntegrationTests body)
- Architecture: 101 → 105 (+4: SecurityPermCodesControllerTests)
- FE Vitest: +12 in 3 new files (use-perm-codes 4 / workflow-user-multi-select 3 / transition-assignment-editor 5)
- Total NEW tests this plan: **46** across BE+FE

Build 0E/0W. EF `has-pending-model-changes` clean (no schema change — all entities pre-existed at 2026-05-19 migration).

## Invariant reinforcements + 1 new candidate

### 260522-A — re-grep file paths + identifier sets at slice start (7th reinforcement this week)

This plan ratified the invariant five more times during execution:
- Phase 02 C4 test surface preserved during P03 spec record→class refactor (260522-A re-grep prevented breakage).
- Phase 04 confirmed migration `20260519015732` has NO DB-FK `WorkflowTransitionAssignments.WorkflowTransitionId → WorkflowTransitions.Id` (only unique index) — required pre-flight raw-SQL DELETE.
- Phase 06 scout caught G2 endpoint `(nếu có)` doesn't exist + G3 picker scope mismatch BEFORE planning crystallised — re-gate saved ~2h of speculative impl.
- Phase 07 isolation fix (clause query scoped by transition IDs) caught only by running real integration tests under shared fixture.
- Phase 08 docs-manager subagent mis-named `WorkflowUserMultiSelect` as a hook + cited non-existent `perm-code-autocomplete.tsx` — orchestrator post-spawn review (260522-L) caught + corrected.

### 260522-D — fold-now extends to discovered-by-scope problems (2 more confirmations)

- Phase 06a folded Phase 05 review M1 (`Clauses` null-guard) when same slice already touched Workflow.Application — same <10min + same pattern + no new decision gating.
- Phase 06c folded review M1 (Drawer remount) + M2 (empty-clauses null semantic) within ~10min — quality bar held (8.8 → effectively 9.4 post-fold).

### 260522-M — fatigue-mode override stays clean at gate density

User explicitly authorised "Full Phase 06 single-session" (3 sub-slices, 4-6h estimate, ~8 commits this session). Quality bar held: 9.4 / 9.2 / 8.8 reviews with all carry-fwds clean. Critical bug C1 caught + fixed in 06b. No reverts, no broken builds, no regressions.

### 5b-C — unit mocks cannot catch EF IDENTITY/FK structural bugs (validated 2 more times this plan)

- Phase 04 P3_A4 atomicity test caught EF/SQL behaviour single-test mock cannot see.
- Phase 07 I_SaveAct_A1 + A5 caught replace-all DELETE semantics with real EF.

### NEW INVARIANT CANDIDATE 260523-D — "fail-CLOSED rule MUST ship with feeder write-path in same slice OR feature-flag off"

**Origin:** Phase 5c (v2.137p90) shipped R2/R3/R4 enforcement at BP-Activate path with no production write-path for the assignment data — every freshly-created WF def became un-activatable in prod. Discovery cost = single user curl 5 days later + this plan's ~10h cycle.

**Statement:** any fail-CLOSED rule shipped (validator, policy gate, schema check, claim-guard) MUST be paired with a feeder write-path PR shipping in the SAME slice OR be feature-flagged off until pairing lands. Otherwise the rule is dead code that silently breaks future writes.

**Audit grep:** any new `WorkflowGraphValidator.Check*` / `WorkflowErrors.*Failed` / `Result.Fail(new ValidationError(...))` introduction MUST have a parallel `Repository.*Async` / `Controller.[HttpPut/Post]` shipped in same slice.

**Reference:** [[phase-savegraph-assignment-260523]] memory anchor.

## Cycle vs plan estimate

| Phase | Plan estimate | Actual | Notes |
|---|---|---|---|
| 02 | 1h | 1h | On target |
| 03 | 2h | 2h | On target |
| 04 | 2h | 2h | On target |
| 05 | 1h | 25min | 2.4× faster (mechanical handler projection) |
| 06a | 30-45min | 45min | On target (scout convention took longer than expected) |
| 06b | 1.5-2h | 1.5h | On target (C1 fix cost 10min) |
| 06c | 2-3h | 1.5h | 1.5× faster (controlled-component pattern was clean) |
| 07 | 1.5h | 30min | 3× faster (existing harness reused; isolation fix found via run) |
| 08 | 30min | ~30min | On track |
| **Total** | 11-14h | ~10h | Single session, 5 advisor calls, 0 reverts |

## Carry-fwd backlog rows (file separate)

1. **WF concurrency race-flake** `WorkflowInstanceConcurrencyTests.SimultaneousRace_ExactlyOneSucceeds_OtherFailsWithStateConflict` — pre-existing flake, unrelated to this plan.
2. **FE test suite drift** 9 unrelated failures across auth/migration/classification/signature/taxonomy domains.
3. **Product Q: recall+assignment behaviour** — silent-persist (current) or explicit-reject when `IsRecall=true` and `Assignment != null`? FE could grey out the Collapse for recall transitions.
4. **Product Q: empty-clauses semantic** (Phase 05 M2 still open) — R5 violation OR transitional-state allowed?
5. **Product Q: default clause mode on Add** (Phase 06c L5) — currently Initiator; product confirm.
6. **EC3 FE stale-id dedupe** — parent should dedupe stale Select multiple ids against current options (06b carry).
7. ~~**L2 audit-col false-positive** in I_SaveAct_A5 substring match — refine to exact-match.~~ ✅ **CLOSED 2026-05-23 EVE v2.137p106** — `WorkflowDefinitionSaveGraphWithAssignmentTests.cs:433-435` swapped `.Contains(...)` → `== """[...]"""` on both `WORKFLOW.TASK.PROCESS` (NotContain) and `WORKFLOW.DEF.MANAGE` (Contain). Test-only +5/-3 LOC, `I_SaveAct_A5` GREEN 24s.
8. **Orval runtime envelope-strip codification** — 3rd consumer triggers `docs/code-standards.md` + ESLint rule (260523 candidate codifies after first 2 instances: `business-process-admin-api.ts` + `use-perm-codes.ts`).

## Files outside plan scope but touched

None. All edits inside `src/modules/workflow/` + `src/host/AqtCoreFW.Api/Controllers/SecurityController.cs` + `tests/{architecture,unit,integration}` + `web/src/features/workflow/` + orval-generated `web/src/api/generated/{security,aqtCoreFWAPIV1.schemas.ts}`.

Reverted out-of-scope drift in 5 prior-shipped-but-unregenerated orval files (`workflow-tasks`, `workflow-assignments`, `workflow-delegation`, `workflow-instances`, `business-processes`) — those carry deltas from older sessions that owe their own slice (advisor catch).

## Reports archive

- `reports/debugger-260523-1228-bp-activate-r2-empty-assignments.md` (root cause)
- `reports/code-reviewer-260523-{1414,1438,1500,1626,1712,1720,1730,1800}-*.md` (8 review passes)
- `reports/tester-260523-{1423,1455,1545,1622}-*.md` (4 test verification passes)
- `reports/fullstack-developer-260523-1451-phase-04-repo-phase-3.md`

## Open at end-of-session

- 8 commits local-only; bundle eligible for push (H1 merge gate closed Phase 07).
- Push gate to user — direct-to-main acceptable per [[phr-v2-phase-06-closure-260522]] for invariant-closure slices.
- Bundle is invariant-closure (closes 260522-A 7th reinforcement + filed 260523-D candidate).
