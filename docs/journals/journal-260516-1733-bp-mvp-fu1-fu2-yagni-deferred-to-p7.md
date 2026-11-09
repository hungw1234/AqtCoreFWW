# Journal — 2026-05-16 17:33 ICT — FU1/FU2 deferred to P7 (YAGNI)

## Session goal
Post-`aqt-next` scout: user picked Option B (cook P4-2B-FU1 + P4-2B-FU2 today as BE prerequisites, P7+P8 fresh window tomorrow). Pre-cook scout sub-step revealed both FU consumer-absent → revised to EOD wrap with annotation.

## What worked
- `/aqt-next` Step 7 trade-off table surfaced fatigue + scope mismatch up front. User overrode with B, fine.
- Pre-cook scout caught YAGNI violation before any code written. Confirmed via 3 concrete probes:
  - `IBusinessProcessQueryClient` does not exist (Glob empty).
  - `DeactivateWorkflowDefinitionCommandHandler` does not exist (find empty); only aggregate `WorkflowDefinition.Deactivate()` at `WorkflowDefinition.cs:169`.
  - Red-team R5 lock (`plan-red-team-findings.md:120-122`) explicitly **rejected** BE pre-check at Forms — said "only soft-delete supported + surface in admin UI" → consumer is FE.
- HARD-GATE saved cook attempt on stub phases (`phase-07-admin-ui-bp-designer.md` 123 lines, `phase-08-user-ui-bp-runtime.md` 129 lines — vs P0-P6 cooked phases 300-800 lines).

## What didn't / lessons

### L1 — "blocked-on-X" backlog label ambiguous
Carry-over rows used "blocked-on-R5/R6 ... when guard built". Two readings:
1. (My initial framing in aqt-next) BE prerequisite for X consumer to exist.
2. (Actual intent) Wait until X consumer exists, then add method as part of same slice.

Reading 2 is the YAGNI-correct one and matches the P0-P6 vertical-slice pattern (cross-module client added in same phase as consumer, never standalone).

**Codify:** when backlog row says "X cross-module method when Y guard built", grep for Y's handler/command BEFORE recommending. Empty grep = method is P-coupled, not a standalone task. Avoids the "build now, wire later" trap.

### L2 — Scout-before-recommend, not after
`aqt-next` Step 7 surfaced FU1/FU2 as B-tier candidates without consumer grep. Reconsider sequencing: trade-off table for ANY cross-module-method candidate should include "consumer grep" as a Step-3 pre-filter, not after user picks. Cost ~30s saves wrong recommendation.

### L3 — Fatigue gut-check held, evidence-check ratified
Initial recommendation (Option A `/aqt-stop`) was based on fatigue heuristic alone — 8 ships, 16:50 ICT, memory L1 "fresh window". User reasonably overrode (memory feedback noted skepticism on heuristic-only push-back). Scout then produced **concrete** evidence (zero consumers). Concrete evidence trumps heuristic on equal weight in user-facing reasoning.

**Codify:** when fatigue-band override happens, immediately run cheap concrete-evidence probes (grep, ls) before opening scope; don't push-back on heuristic twice.

## Decisions made
- **D1** FU1/FU2 deferred to P7 vertical slice. Not standalone. Annotated `backlogs/backlog.md:374-375`.
- **D2** Session ends with no commits. Working tree was clean throughout; backlog annotation is the only mutation (uncommitted at close — bundle next session with P7 kickoff).
- **D3** No journal-only commit. Annotation + journal stay uncommitted; small mtime delta acceptable.

## Test status
N/A — read-only session.

## Next session — priorities
1. Fresh window: `/aqt-next` scout → ratify P7 readiness (BE BP MVP complete confirmed, stubs need planner expansion).
2. `/ck:plan` expand `phase-07-admin-ui-bp-designer.md` to cook-ready: API endpoint paths exact, Orval config delta, FE scout (existing Forms admin patterns to mirror), DTO contracts BE-matched, FU1 inline as step-8 wire (`IsFormTemplateReferencedAsync` cross-module client added when DeleteTemplateDialog edit happens).
3. After P7 expanded: same for P8 (`phase-08-user-ui-bp-runtime.md`), runs picker + tab-rendered forms + prefill wiring (consumes P6 `IFormPrefillContextClient` already shipped).
4. Optional parallel: `/ck:plan` expand can spawn parallel `researcher` for FE hooks pattern + Orval current state.
5. Commit at next session start: this annotation + journal as docs-only commit before P7 plan expand begins (avoids mixing with P7 implementation churn).

## Unresolved questions
- FU2 may genuinely be POST-MVP. Should `DeactivateWorkflowDefinitionCommandHandler` exist at all in MVP scope, or just rely on aggregate-method-via-admin-soft-delete-UI? Decide at P8 planner expand.
- P7 stub mentions Playwright e2e — is the Playwright harness already set up in `apps/web`? Verify in P7 planner scout (saves a research detour mid-cook).
