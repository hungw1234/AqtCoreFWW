# T5-17 WAI-ARIA 1.2 Grid Keyboard Navigation — Instance Monitoring Dashboard

**Date**: 2026-04-14 13:44
**Severity**: Medium
**Component**: web — `instance-monitoring-dashboard.tsx`, new `use-workflow-grid-keyboard-nav.ts`, `workflow-grid-row.tsx`, `workflow-grid-constants.ts`
**Status**: Resolved

## What Happened

Added full WAI-ARIA 1.2 grid keyboard navigation to the virtual table in the workflow
instance monitoring dashboard. Prior state was embarrassingly partial: `tabIndex` on
checkboxes only, `role="list"/"listitem"` — not grid semantics at all, no roving
`tabIndex`, no arrow-key routing. Four phases, 0.5d estimate, 0.5d actual. TDD held
the line.

Delivered: hook `use-workflow-grid-keyboard-nav.ts` (80 LOC, 11 key bindings, 35 tests),
row renderer extracted to `workflow-grid-row.tsx`, constants consolidated into
`workflow-grid-constants.ts`, dashboard surface 258→200 LOC, 2 i18n keys, 97/97
workflow tests green.

## The Brutal Truth

The code review round stung more than expected for a "clean" implementation. Reviewer
flagged M-01 immediately: `GRID_COLS` and `ROW_HEIGHT` were duplicated across the new
hook and the dashboard with a self-admitting comment (`// keep in sync with hook`). We
wrote that comment ourselves, shipped it, then had a reviewer read it back to us. That
comment is a confession, not documentation. We knew the duplication was wrong and did
it anyway because extracting a constants file felt like over-engineering one file — it
wasn't, and thirty minutes of rework proved it.

M-02 was more architecturally embarrassing: intermediate `<div>` wrappers between
`role="grid"` and `role="row"` broke WAI-ARIA §6.2.4 grid ownership chain. We spent
time writing ARIA-compliant cell semantics and then introduced a DOM layer that
invalidated them. Classic last-mile mistake — fix the semantics, miss the container.
`role="presentation"` on the wrappers resolved it, but it should not have shipped
that way.

## Technical Details

- **Commits**: `a24768e4` (feature, 4 phases squashed), `bd001846` (docs/plan/backlog)
- **Key binding coverage**: ArrowUp, ArrowDown, ArrowLeft, ArrowRight, Home, End,
  Ctrl+Home, Ctrl+End, Space (row select toggle), Enter (row action), Tab (exit grid)
- **Roving tabIndex**: focused cell `tabIndex=0`, all others `-1`; stored as
  `[rowIndex, colIndex]` in `useRef` to avoid re-render on navigation
- **Virtualization focus wiring**: `useEffect` (not `requestAnimationFrame`) to
  reapply focus after virtual row churn — React commit cycle is deterministic; rAF
  risks firing before DOM apply
- **Option A tab entry**: Tab-into-grid lands on data cell (0,0), skips sticky
  header select-all — locked with regression test `TC-FE-WF-DASH-011`
- **Code review score**: 8/10 APPROVED_WITH_MINOR (4 findings, all resolved in fix round)
- Dashboard final: 237 LOC (target was ≤250)

## What We Tried

- **P2+P3 squash decision**: Both phases edit the same dashboard file. Keeping them
  separate would have added a review gate with zero isolation benefit — squashed into
  one commit, one review pass.
- **Option B** (sticky header as separate Tab stop) considered and rejected. It feels
  more natural to experienced users but violates the ARIA "single Tab stop per grid"
  rule. Spec compliance over UX familiarity; locked with a regression test so the
  next developer does not silently revert it thinking it was an oversight.
- **`requestAnimationFrame` for focus refocus**: considered, rejected. React's
  `useEffect` fires after commit — the DOM node exists. rAF timing is non-deterministic
  relative to React's reconciler and would introduce subtle focus races on fast scroll.

## Root Cause Analysis

M-01 (constant duplication) happened because P1 (hook) and P2+P3 (dashboard) were
planned as separate files and the extraction step was never in scope — it was implicitly
deferred as "the dashboard already has these numbers, just import them." We did not
import them; we duplicated them and added a comment. The comment was the tell we
ignored.

M-02 (DOM container ownership break) happened because the virtualization adapter
wraps rows in a sizing `<div>` that we did not examine during ARIA wiring. ARIA
grid rules are strict about direct ownership — `grid > rowgroup > row > gridcell`,
no anonymous containers. We verified the role attributes but not the rendered DOM
tree structure.

## Lessons Learned

- A `// keep in sync with` comment is a code smell that requires extraction, not
  a valid maintenance strategy. If you write that comment, stop and extract the constant
  before the PR.
- After adding ARIA roles, render the component and inspect the actual DOM tree — not
  just the JSX. Virtual DOM wrappers that are invisible in JSX become real DOM nodes
  that break ARIA ownership chains.
- `useEffect` is the correct hook for post-render DOM side effects (focus management
  after virtualization churn). `requestAnimationFrame` is for animation timing, not
  React DOM synchronization.
- TDD on keyboard event handlers is high-leverage: 35 tests written before any
  implementation locked all 11 bindings as contracts, not as incidental behavior.
  Phase-04 went 15 min over estimate due to fixture DTO drift caught in review — not
  because the logic was wrong.
- Option A vs Option B is a real decision with a spec citation (`WAI-ARIA 1.2 §6.2.3`
  single Tab stop rule). Document it and lock it with a test. Future developers will
  "fix" it otherwise.

## Next Steps

| Item | Owner | When |
|---|---|---|
| T5-21: `tsc` strict check for `src/test/**` — fixture drift undetected at typecheck time | dev | Next sprint |
| T5-22: 8 pre-existing FE test failures (unrelated to T5-17, confirmed via git diff) | dev | Triage next session |
| Manual a11y audit with screen reader (NVDA/VoiceOver) on grid | QA | When BE online |

## Open Questions

1. **Plans in `.gitignore`**: `plans/` is gitignored — git-manager had to `git add -f`
   to commit plan artifacts. Current convention is ambiguous: are plans ephemeral
   (local only) or project artifacts (committed)? Recent commits include plan files,
   so the gitignore entry appears to be stale policy. Worth an explicit team decision
   before it causes a "where did the plan go" incident.

2. **Fixture DTO drift (T5-21)**: Test fixtures can drift from generated API types
   silently — `tsc` does not cover `src/test/**` under current config. The drift was
   caught in code review, not at build time. This is a process gap, not a one-off.
