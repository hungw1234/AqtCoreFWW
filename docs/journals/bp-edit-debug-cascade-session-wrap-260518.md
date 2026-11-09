# Session Wrap — BP Edit Debug Cascade (2026-05-18)

**Goal:** /ck-debug → /fix loops on BP edit page `019e364f…` — 3 user-reported symptoms + residual follow-ups + a UX layout request.

## What worked (verified, shipped)
- `56f191d4` **BUG1+BUG2**: viz canvas blank fix (react-flow `useUpdateNodeInternals`+rAF) + FORM TEMPLATE — BE server-side `FormTemplatePublicId` resolution (new batch `IFormTemplateQueryClient.ResolveTemplatePublicIdsAsync`, mirrors `IWorkflowModuleClient`), deleted broken FE `Number(Guid)===bigint` backfill. Live-verified.
- `f5a5a995` **CLONE-FK**: `WorkflowDefinition.Clone()` IDENTITY-before-save (`newState.Id==0`) → all cloned transitions persisted `0/0`. Fix = states-only Clone() + two-phase handler (resolve FK by stable state Name) + `WorkflowTransition.Create` FK≤0 guard + e2e regression. Live clone repro `0/0`→correct.
- Data remediation (SQL, dev): whole-DB scan → only WF#13 + Id 11 corrupt (E2E/throwaway, **0 real-tenant**); surgical UPDATE preserving state ids; throwaways soft-deleted; `GLOBAL_ZERO_FK=0`. AutoTransition remap live spot-check verified.
- `13bf2e91` **buildEdges defense-in-depth**: drops + warns FK∉states (3 call sites, suite 9/9).
- `36133d52` **viz layout**: stacked full-width (1129px vs ~460px), Row/Col removed. Live-verified.

## What didn't / lessons
- BUG1 diagnosis revised 3× (tenant-null → numeric-id → fitView one-shot → **true: RO never measures, only forced store updateNodeInternals works**). Compile-clean + advisor-reviewed ≠ verified; live store-inspection closed it.
- Clone-FK: 2026-05-08 two-phase fix covered Create/ReplaceGraph but **missed Clone()** — fixing a bug-class must audit ALL pattern sites.
- **Architecture wall:** layout change re-exposed react-flow reveal fragility. 4 approaches failed incl. user-sanctioned re-architecture, **disproven by evidence** (container sized, still `measured:{}`). HARD-GATE: stopped, surfaced user-choice-vs-evidence conflict, user chose decouple.

## Decisions
- BUG2: BE resolve server-side (vs FE bigint in picker DTO — would leak T2 internal id).
- WF#13 data: surgical SQL (preserve state ids → BP form-maps safe) vs re-save graph (would orphan form-maps).
- Reveal defect: decouple from layout; revert experimental file to clean baseline; track as spike.

## Test status
- FE typecheck clean (all flows); vitest helper suite 9/9 (2 new buildEdges regression); e2e clone regression added (Playwright spec-parse clean, not run). BE: Forms.Infra + BP.App + Workflow.App build 0 error. No automated reveal/handler test (harness gap — honest, flagged).

## Next session (carry-over)
1. **SPIKE: react-flow read-only canvas reveal** — pick approach (a) self-terminating poll of forced store `updateNodeInternals({nodeElement,force:true})` until `nodesInitialized`; (b) replace mount/lib/SVG; (c) key-remount on layout-stable. Evidence: `plans/reports/fix-260518-1610-bp-viz-stack-layout.md`. Canvas blank in stacked layout until done.
2. Verify designer (`react-flow-canvas.tsx`) actually renders today (bare fitView) — premise unverified.
3. Optional: handler integration test for clone FK + AutoTransition remap when a workflow integration harness exists.
