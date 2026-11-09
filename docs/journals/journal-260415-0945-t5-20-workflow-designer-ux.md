# T5-20: Workflow Designer UX — R7 Drag Persistence + R11 Draw-to-Connect

**Date**: 2026-04-15 09:45
**Severity**: Low
**Component**: `react-flow-canvas.tsx`, `workflow-designer-tab.tsx`, `workflow-graph-input-utils.ts`
**Status**: Resolved

## What Happened

Delivered two workflow designer UX follow-ups in a single FE-only session: R7 (persist node drag positions within tab session) and R11 (draw-to-connect pre-fills transition modal). Both shipped green — 730/730 tests, TSC zero errors. No drama. But it nearly wasn't that clean.

## The Brutal Truth

The backlog said "dagre override." The codebase uses a plain grid. Those are not the same thing. If the advisor red-team hadn't caught that scope drift before a single line was written, we'd have spent an afternoon wiring up dagre to a layout system it was never part of. We got lucky on timing. The real lesson is that backlog entries decay faster than you think, and you cannot trust them against a live codebase without cross-checking.

LOC ceilings also nearly bit us. The canvas file ended at 198/200 and the designer-tab at 193/200. One more feature touch on either file without extraction first and we'd be refactoring mid-implementation — the worst possible time to restructure. This was flagged pre-coding and "extract helpers first" became mandatory step 0, not an afterthought.

## Technical Details

- **Scope correction**: `WorkflowStateDto.cs` — 15 LOC record, zero `positionX/Y` fields — confirmed FE-only persistence is viable. No BE change needed.
- **R7 impl**: `useRef<Map<string, XYPosition>>` (not `useState`) to avoid spurious re-renders on every drag commit. `onPositionCommit` optional callback prop added to canvas; cleared on undo/auto-layout.
- **R11 impl**: `onCreateTransition` optional callback on canvas; modal pre-filled with `source`/`target` IDs. Temp edge ID via `crypto.randomUUID()` — server replaces on persist.
- **`vi.hoisted()` pattern**: required to avoid temporal dead zone in `vi.mock()` factory when mocking `crypto`. Standard Vitest pattern; painful if you don't know it.
- **Non-null assertions `source!`/`target!`**: safe because React Flow's `onConnect` only fires when both handles are connected — not a lazy shortcut, a documented invariant.
- **6 new tests** (3 R7, 3 R11), all green. `setEdges` mock must invoke the updater function to exercise the `addEdge` fallback path — easy to get wrong and silently miss coverage.

## What We Tried

- Considered `useState<Map>` for position storage — rejected, triggers re-render on every drag end event which defeats the purpose.
- Considered localStorage for persistence — rejected explicitly (YAGNI). Tab-session via ref is sufficient for the stated requirement.
- Considered parallelising the two phases — rejected because both touch the same two files; sequencing was the only safe option.

## Root Cause Analysis

No failure here, but the near-miss was backlog rot plus LOC drift. The dagre mention in the original ticket was a leftover from an earlier design discussion that never materialized. The LOC ceiling issue was accumulation — each small feature adds 10-20 lines and nobody extracts until it's too late. Red-teaming the plan before coding is what prevented both from becoming mid-session crises.

## Lessons Learned

- **Red-team before coding, not after**. Advisor review caught two real bugs (dagre scope drift, LOC math) that would have surfaced mid-implementation. The 20-minute cost saved 2+ hours of rework.
- **`useRef` for write-heavy derived state**. If state is written frequently but only read on explicit user action, `useRef` beats `useState`. Fewer renders, same correctness.
- **File extraction is a hard gate, not a suggestion**. When LOC math puts you over ceiling mid-phase, the refactor cost doubles. Make extraction step 0.
- **`crypto.randomUUID()` for temp IDs is idiomatic React Flow**. Server assigns real ID on persist; keep the pattern consistent across the codebase.
- **Optional callback props keep tests honest**. The fallback (`addEdge` path) gets tested by omitting the callback — good coverage of the default behavior without mocking ceremony.

## Next Steps

- **FU1** (filed in backlog): Replace `as unknown as XYPosition` cast in `onNodeDragStop` with a proper type guard — medium severity, no runtime risk, cosmetic correctness.
- **FU2** (filed in backlog): Both canvas (198 LOC) and designer-tab (193 LOC) are at ceiling. Next feature touch on either file requires extraction first — owned by whoever picks up the next designer ticket.
- Branch `feat/t5-20-workflow-designer-ux` at `d77e7f00` is merge-ready; pending user push + PR.
