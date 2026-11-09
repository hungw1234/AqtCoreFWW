# Docs & Process Consolidation (A + D Carry-fwd)

**Date**: 2026-05-27 10:40
**Severity**: Low
**Component**: Documentation, Process Gates
**Status**: Resolved

## What Happened

Closed two stale carry-forward follow-ups in a single 30-min docs-only slice:

- **A (code-standards wire-doc):** Added `### Structured Violation Metadata (PublicMetadata) Wire-Shape Contract` under §API Response Envelope in `docs/code-standards.md`. Documented the load-bearing inline cast trap, split-casing invariant (camelCase outer / PascalCase inner dict keys), and Swashbuckle→orval contract gap requiring hand-written FE types.
- **D (WF-TESTER-FULL-ASSEMBLY-GATE):** Generalized a Phase-04-discovered test-selection gap (filtered runs miss cross-domain RED) into a reusable process gate in tester subagent template (`~/.claude/agents/tester.md`, global config) + mirrored into `docs/code-standards.md` §Cross-Domain Test Selection for project git audit trail.

Commit: `c7de03f2` (docs only, version-line unchanged).

## The Brutal Truth

This felt small and clean *because* the decision boundaries were already established. A was a documentation chore that should have happened *during* T3-API-ENVELOPE-FE-RENDER slice-closing (Tuesday PM). D was a process catch that needed generalization rather than hardcoding to a now-closed arc. Without Invariant 260522-A (audit-at-slice-start on carry-forward rows), I would have filed D as "RT2-15-specific, arc closed, moot" and skipped it. The real value was recognizing "arc-closed ≠ lesson-closed."

## Technical Details

**A Wire-Shape Burden:**
- `ApiResponse.Fail()` projects only reserved `PublicMetadata` key (default-deny opt-in).
- Cast to `IReadOnlyDictionary<string,object?>` is load-bearing; wrong/missing cast silently serializes `null` (tsc 0, runtime fail).
- Arch rule `Rule_PublicMetadata_WithMetadata_Value_MustCast_To_IReadOnlyDictionary` enforces this solution-wide.
- Casing split: `metadata.violations[*].metadata.UnknownGroupCodes` (outer camelCase, inner PascalCase — no DictionaryKeyPolicy).
- Swashbuckle emits dict as OpenAPI untyped `{type:[null,object]}` → orval cannot recover property names → hand-written FE type at `web/src/core/api/api-error.ts` required regardless of regen (documented in code-standards to prevent future orval-regen cargo-cult).

**D Test-Selection Gate:**
- Origin: Phase 04 ByOrgUnit shipped with filtered test run (I7 stale-case missed, only discovered in open-phase regression).
- Generalized rule: Change spans ≥2 distinct test-domain dirs → run full `*.Integration` + `*.Application` assemblies, never claim green on `--filter` subset.
- Added Mapping-Strategy F in tester template + auto-escalation bullet.

## What We Tried

No failed attempts; the analysis was straightforward once backlog row audit applied (detect "arc closed but process lesson alive"). Global-vs-project placement was a deliberate user-gated tradeoff (global for universality; project mirror for git audit trail).

## Root Cause Analysis

**Why this existed as a carry-forward:**
- A was a documentation gap identified in T3-API-ENVELOPE-FE-RENDER (Tuesday PM) but deferred to "after shipping" to stay focused.
- D was a process observation (test-selection spread trap) filed mid-arc but set aside when RT2-15 closure was prioritized over process-normalization.

**Why it deserved a separate slice:**
- No code, no tests, no risk of touching the active dev stack.
- A is stable and reusable (wire-shape doc applies to any future `PublicMetadata` usage).
- D is domain-agnostic (applies to any cross-module change, not RT2-15-specific).

## Lessons Learned

1. **Audit-at-slice-start applies to process rows too.** Backlog row said "bake into tester template for RT2-15 phases" — literal scope was moot (arc closed). Real insight (cross-domain test selection) was still valuable. Did NOT skip as "arc closed"; generalized instead.

2. **No plan-artifact references in code/docs.** Did NOT write `§F: RT2-15-TESTER-FULL-ASSEMBLY-GATE` or mention finding codes. Wrote domain-agnostic language: "change spans ≥2 distinct test-domain dirs" → general-purpose gate for tester subagent to apply everywhere.

3. **Global-then-project mirrors balance universality + audit trail.** Global tester.md won't propagate if memory purges; project docs ensure the rule stays discoverable in git history. Both needed.

4. **Cook-workflow right-sizing.** For 2-doc edits + 1-global edit, skipped planner/tester/project-manager sync machinery. Used docs-manager review only + single `docs:` commit. Full feature machinery would be cargo-cult on doc-only work.

## Next Steps

- `WF-TESTER-FULL-ASSEMBLY-GATE` backlog row marked RESOLVED in-place (archive-eligible at next optimize pass, deferred to avoid concurrent file-refactor).
- Remaining carry-forwards untouched: RT2-15-PHASE-05-EQ2 (P3), T3-API-ENVELOPE-FE-RENDER carry-fwd (a/c/d), BACKLOG-MIXED-SECTION-TRIM (P4).
- If D hits production (cross-domain change post-today), tester will apply gate automatically from template + code-standards mirror.
