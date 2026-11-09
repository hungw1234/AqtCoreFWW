# Transition Picker UX Polish: Option-Level Disable Guard

**Date**: 2026-05-17 12:15
**Severity**: Low
**Component**: BusinessProcessFormMapsView (FE)
**Status**: Resolved

## What Happened

Added UX polish to transition picker: when multiple form-map rows reference the same workflow transition, the second+ rows now show that option as `disabled: true`. Complements P8-FU2.1's save-time validator; raises defense-in-depth from validator layer to picker UX.

## Implementation

- `transitionUsageByRowKey`: Map<number, string> keyed on row `_key`, valued on `workflowTransitionId`
- First-wins rule: owner is whoever's row populates the map entry first
- Picker filters `transitionOptions` with `disabled` flag when `option.value !== owner`
- Self-reselect safe: `owner === row._key` returns option unmutated
- A11y: Antd propagates `disabled` → `aria-disabled` on option element

## Technical Details

3 files, +37/-3 LOC:
- `business-process-form-maps-view.tsx`: memo + picker column logic
- `backlog.md`: P8-FU2.1.1 documented
- `CHANGELOG.md`: UX guard logged

FE typecheck PASS. Code-review 9.5/10 auto-approve (first clean 9.5 today without fold-in).

## Why This Now

Wallclock: 10min. Consumer obvious (admin designer flow); low-LOC polish. Reversed prior session's "defer UX feedback" hedge when signal was clear. Precedent: P8-FU1 state-form multi-binding allowed per design; transition picker should gate at option level instead.

## Lessons Learned

**UX-polish ROI at scale**: First-class defense-in-depth justified at O(1) cost when consumer is obvious and scope is atomic.

**Validator ↔ UX symmetry**: Map-first-wins mirrors save-time validator's "flag 2nd+ duplicate" rule. Consistent mental model.

**Scope → code-review friction**: P8-FU2 (9.6→fold) → P8-FU2.1 (9.4→fold) → P8-FU2.1.1 (9.5 clean). Smaller scope + precedent-mirror → lower fold-in probability. Threshold ~40-60 LOC before review nits accumulate.

**Perf: memo acceptable at scale**: O(rows × options) < 1000 ops/render. Reviewer confirmed trivial; no further optimization needed.

## Status

**Resolved.** FU chain P8-FU2 → P8-FU2.1 → P8-FU2.1.1 fully closed. BP MVP FE feature-complete.
