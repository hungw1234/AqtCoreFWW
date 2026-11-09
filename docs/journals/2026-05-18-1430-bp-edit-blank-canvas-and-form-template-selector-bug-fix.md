# BP Edit Page: Blank Canvas and Form Template Selector — Diagnosis Revised 3× Before Correct Fix

**Date**: 2026-05-18 14:30
**Severity**: High
**Component**: Business Process edit form (BP module), Workflow visualization + Form Template binding
**Status**: Resolved

## What Happened

Two bugs rendered the BP edit page (form-maps view) unusable:

1. **Workflow visualization canvas entirely blank** — 3 nodes rendered invisible despite being measured and positioned correctly
2. **Form Template selector showed placeholder** — dropdown never populated with available templates, form state rows lacked the template reference

Both fixed, compiled clean, live-verified on real code path with hard reload.

## The Brutal Truth

BUG 1 was **diagnose-wrong-fix-wrong** before ground truth emerged. The debugging sequence was: initial hypothesis (BE returns null) → disproved by live API fetch; second hypothesis (numeric node IDs break react-flow) → disproved by Visual Designer rendering identical data perfectly; **third hypothesis** (one-shot `fitView` prop no-ops inside antd Card) → seemed airtight after static analysis, but the **first fix built on it was inert**. Only then — via React DevTools fiber-walk into the zustand store during live verification — did the true mechanism surface: `node.measured` was empty, `nodesInitialized` false, `fitViewQueued` true, because react-flow v12's ResizeObserver never fires the initial measurement when the component mounts inside a `display:none` ancestor. That was the moment of honest "we were confident and wrong."

BUG 2 was **correct diagnosis on first try** — no redemption arc there, just good forensics. But the contrast matters: static reasoning + advisor review ≠ verified truth.

## Technical Details

**BUG 1 — Workflow Visualization Canvas**

Initial state (broken):
```
rfRect: 460x420  (container IS sized)
nodeCount: 3  (data-id "40","38","39")
ALL nodes: visibility: hidden
viewportTransform: translate(0,0) scale(1)  ← fitView never ran
```

Root cause (disproved diagnosis):
- Hypothesis #1: `GetBusinessProcessByIdQueryHandler` returns null `workflowDefinitionPublicId` due to tenant mismatch → **WRONG**. Live API fetch returns valid Guid, tenants match in DB.
- Hypothesis #2: Numeric node IDs (`{id: 40, ...}`) break `@xyflow/react` → **WRONG**. Visual Designer renders the same WF#13 with identical numeric-id data, same `buildNodes` helper, same bare `fitView` prop. Nodes are `visibility:visible`, viewport `scale(1.45385)`.
- Hypothesis #3: One-shot `fitView` prop no-ops because antd Card/Col layout isn't final at react-flow's initial tick → **DIAGNOSIS CORRECT, FIX WRONG**.

True root cause (discovered during fix verification):
React-flow zustand store showed `node.measured: {}` (empty) for all nodes, `nodesInitialized: false`, `fitViewQueued: true`. react-flow v12 per-node ResizeObserver never delivers initial measurement when the canvas mounts inside a `display:none` ancestor (antd Card/Col conditional reveal in form-maps editor). Without measurements, `nodesInitialized` never flips → queued fitView never drains → nodes stay `visibility:hidden`.

**First attempted fix** (the wrong one):
```tsx
const nodesInitialized = useNodesInitialized();
useEffect(() => { if (nodesInitialized) void fitView({ padding: 0.2 }); }, [nodesInitialized, fitView]);
```
This compiled clean, looked plausible, and would have been correct in a normal render context. **Live verification showed the canvas still blank.** The effect never ran — the gate (`nodesInitialized`) never became true.

**Applied fix** (`bp-workflow-flow-readonly.tsx`):
```tsx
const { fitView } = useReactFlow();
const updateNodeInternals = useUpdateNodeInternals();
useEffect(() => {
  if (nodes.length === 0) return;
  const raf = requestAnimationFrame(() => {
    updateNodeInternals(nodes.map((n) => n.id)); // sync getBoundingClientRect — bypasses dead RO
    void fitView({ padding: 0.2 });
  });
  return () => cancelAnimationFrame(raf);
}, [nodes, updateNodeInternals, fitView]);
```

`useUpdateNodeInternals` synchronously invokes `getBoundingClientRect` on each node, populating `measured` and flushing the queued fitView. Live verification: viewport `translate(39px,186.96px) scale(0.59)`, 3/3 nodes `visibility:visible`.

**BUG 2 — Form Template Selector**

Root cause (correct first diagnosis):
- FE `makeStateRow` sets `formTemplatePublicId: undefined` always (lines 83, 192–193)
- Backfill attempts `formTemplateData.items.find((f) => Number(f.id) === bigintId)` where `FormTemplatePickerDto.id` is a Guid string
- `Number("019e0c3f-...")` → `NaN`; `NaN === 35` → false; lookup always `undefined`
- Picker DTO carries **no internal bigint Id field** — server returns only `{id: Guid, name, code, version}`
- Result: Select `value={undefined}` → placeholder "Form template…"

Applied fix (BE, mirrors existing workflow pattern):
- Added `Guid FormTemplatePublicId` init-only field to `BusinessProcessStateFormDto` and `BusinessProcessTransitionFormDto`
- New `IFormTemplateQueryClient.ResolveTemplatePublicIdsAsync` batch method (no N+1, defense-in-depth tenant filter)
- `GetBusinessProcessByIdQueryHandler` injects client, collects distinct template IDs, batch-resolves Guids, projects via `with`
- FE: `makeStateRow` reads `formTemplatePublicId` from source; deleted broken `Number(Guid) === bigint` lookup and 500-row `PICKER_PAGE_SIZE` workaround

Live verification: `GET /business-processes/{id}` now carries resolved `formTemplatePublicId` for each state/transition form; Select displays `E2E Public Form 1778321962191 (v1)`.

## What We Tried

1. **API response inspection** — Ruled out tenant mismatch, null FK. Data correct in-flight.
2. **Component data flow trace** — Confirmed `buildNodes` helper outputs numeric IDs; Visual Designer renders them correctly. Narrowed issue to `bp-workflow-flow-readonly.tsx`.
3. **Static react-flow/antd analysis** — Hypothesis #3 (one-shot fitView in conditional Card). Theory coherent, but fix was inert.
4. **Live DOM inspection** — Saw `visibility:hidden` on all nodes, empty viewport transform. Confirmed fitView never ran.
5. **Peering into zustand store (React-fiber walk)** — Saw `measured: {}`, `nodesInitialized: false`, `fitViewQueued: true`. **Ground truth finally accessible.**

## Root Cause Analysis

**BUG 1:**
Compile-clean + plausible mechanism + airtight reasoning ≠ verified. The `/ck:debug` report built a convincing diagnosis through elimination, but the fix was built on **what should happen** (the useNodesInitialized gate), not **what actually happens** (the ResizeObserver never fires, so nodesInitialized never flips). The error was one of incomplete mental model: we assumed the state machine would reach the awaited state; the store walk proved it wouldn't.

**BUG 2:**
Straightforward FE-BE contract violation. The FE tried to solve a resolution problem (bigint ↔ Guid) that only the BE could solve. Applied the pattern already in use for workflow definitions.

## Lessons Learned

1. **Compile-clean does not mean correct.** A fix that compiles and passes static analysis can still be inert — it just can't be proven inert without live execution. The `useNodesInitialized` effect-gate was logically sound if the gate were true; the gate never became true.

2. **Live primary-source inspection (store state, not symptoms) closes the loop.** We had symptoms (visibility:hidden, blank canvas) and a plausible mechanism (fitView no-op). Only the zustand store walk revealed that the mechanism's prerequisite (nodesInitialized flip) never occurred. The DOM tells you what happened; the store tells you why.

3. **Diagnostic iterations are OK. Shipping unverified fixes is not.** Three hypothesis revisions felt like failure, but each was a step closer to ground truth. The mistake was shipping after hypothesis #3 without live store inspection; the `/fix verify-loop` caught it.

4. **Antd's conditional rendering + react-flow's layout assumptions = friction.** Neither is broken, but the intersection is: react-flow assumes the container is visible and layout is stable at initial mount; antd Card's reveal is neither. Workaround: imperative re-measure on mount (or add min-height to Card body to stabilize first layout).

5. **Mirror existing patterns.** BUG 2 was resolved by recognizing the FK-resolution pattern already used for workflow definitions and applying it to form templates. No novel code, less surface area for mistakes.

## Next Steps

- **BUG 1 & 2 shipped live.** RE-verified this morning: canvas renders 3 nodes, form template selector populated. No rollback needed.
- **Edges not rendering (latent defect):** WF#13 has 2 transitions but 0 edges in both visualizers. Root cause unknown (numeric source/target vs node-id mismatch, missing handles, or data issue). Out of scope, recommend follow-up `/ck:debug`.
- **Test coverage gap:** No automated regression harness for these bugs. BP read-handler integration tests (if introduced) should assert `FormTemplatePublicId` populated from `ResolveTemplatePublicIdsAsync`; react-flow/jsdom layout timing is not meaningfully unit-testable.
- **BP edit page sign-off:** User can now see workflow states and select templates on form-maps edit view.

**Status**: RESOLVED

