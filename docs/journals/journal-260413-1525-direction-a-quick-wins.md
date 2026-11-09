# Direction A Quick Wins — A1/A2/A3 Shipped, A4 Deferred

**Date**: 2026-04-13 14:25–15:25
**Severity**: Low
**Component**: FE — Signature, Views, Workflow Dashboard
**Status**: Resolved (A1–A3 merged; A4 deferred to T5-15)

## What Happened

Executed Direction A quick wins in a single session: A1 signature responsive canvas fix, A2 views panel drag-reorder UX, A3 workflow dashboard list virtualization via `@tanstack/react-virtual`. Introduced the first virtualization pattern in this codebase. 20 new unit tests, tsc clean, 45/45 targeted pass.

## The Brutal Truth

Scout missed a second `fromDataURL` call site in the signature component — planner caught it during plan drafting. This is the recurring scout-depth problem: breadth pass across files, but shallow read depth means secondary call sites slip through. Cost us a plan revision cycle, not a bug in prod, but only because the planner was thorough enough to audit the diff manually.

A3 scope was re-targeted mid-session. Scout reported no real virtualization pain in form submissions (too few rows, no perf complaint); planner pivoted to workflow dashboard where item counts actually warranted it. Correct call, but it means the original brief was written on stale assumptions. The scout should flag "no measurable pain" explicitly rather than leaving it for the planner to discover.

Code review initial score was 8.2/10 — that sounds fine until you see the detail: one Critical (drag-and-drop not disabled during form submission), two Majors (missing aria labels, no empty-state guard in virtual list), two Minors. All fixed in follow-up pass. Red-team depth review paid off. If we had shipped at 8.2 the Critical would have been a UX defect in production.

Parallel execution of A1+A2 held clean — file ownership was defined upfront and neither agent crossed into the other's files.

## Technical Details

- New dep: `@tanstack/react-virtual ^3.13.23` — added to `package.json`, lock updated
- 7 files touched: 3 FE components, 3 test files, 2 locale files
- Critical finding: `isDragging` state not gating form submission handler — fixed with guard flag
- 8 pre-existing test failures documented but intentionally not touched (separate remediation backlog)

## Root Cause Analysis

Scout breadth-over-depth is a structural habit problem. It finds files but skips secondary references. A2 drag disable gap was a spec omission — the original user story said nothing about submission interlock, and nobody asked.

## Lessons Learned

- Require scouts to explicitly state "no secondary call sites found" — absence of claim is not absence of bug
- Drag UX stories must include "what is disabled while dragging" as a mandatory acceptance criterion
- Virtualization is now a proven pattern; document the `useVirtualizer` wrapper location for future consumers

## Next Steps

- A4 (bulk action keyboard shortcuts) — deferred to T5-15, backlog updated
- Pre-existing 8 test failures — assign to remediation spike, not this stream
- Document `@tanstack/react-virtual` usage pattern in `docs/code-standards.md`
