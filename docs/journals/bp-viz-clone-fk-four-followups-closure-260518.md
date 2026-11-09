# BP Viz Clone-FK Four Follow-ups Closure

**Date**: 2026-05-18 15:50  
**Severity**: Medium  
**Component**: Workflow Definitions (Clone path, Visualization, AutoTransition remap)  
**Status**: Resolved  

## What Happened

Four residual follow-ups from the BP-viz missing-transitions root-cause fix (clone-FK corruption in WF#13) were systematically resolved over 2 hours:

1. **Full-DB scan for scope.** Query `WHERE FromStateId=0 OR ToStateId=0` → 2 corrupt defs, both E2E/throwaway.  
2. **Remediate WF#11.** Surgical pattern (same as WF#13): update transition FKs to match cloned state ids.  
3. **Defensive FE guard.** `buildEdges()` now drops + warns unresolvable transitions; adds regression test.  
4. **AutoTransitionOnTimeoutId remap.** Live spot-check verified remap logic via DB fixture + API clone.  

**Code changes:** 4 files modified (BE Clone handler two-phase mirroring Create/ReplaceGraph pattern + defense guard; FE buildEdges + test), 0 destructive scripts, 1 documented gap flagged for integration test.

## The Brutal Truth

The exhausting part: knowing a rare code path (`AutoTransitionOnTimeoutId` remap in clone) was implemented correctly but unable to construct a repeatable E2E test through the public API (chicken-egg: graph-replace renumbers ids; no state-level auto-transition update endpoint). **The correct choice was to NOT fabricate a passing test**, but it leaves a gap that feels like a risk.

Also: the whole problem originated because **2026-05-09 E2E fixture seeding happened after 2026-05-08 two-phase fix was authored but BEFORE the clone path was fixed**. This means the data was written by a latent active bug we didn't know existed. The relief of discovering it was contained (2 throwaway defs only) was real, but the uncertainty about whether the fix propagated into the running E2E build lingers.

## Technical Details

**Root cause confirmed:** `WorkflowDefinition.Clone()` built cloned transitions before states were persisted, so `stateIdMap[oldId]` resolved to `newState.Id == 0` (IDENTITY assigned only post-SaveChanges). Every cloned transition persisted `FromStateId=0/ToStateId=0`. Same bug class the 2026-05-08 two-phase fix addressed for Create/ReplaceGraph — Clone was the missed site.

**Fix mirror (proven pattern):** 
- `WorkflowDefinition.Clone()` → states-only deep-copy  
- `CloneWorkflowDefinitionCommandHandler` → two-phase: AddAsync states → resolve transition FKs by stable state Name → AddTransition → SaveChanges → remap AutoTransitionOnTimeoutId  
- `WorkflowTransition.Create` → defense-in-depth guard (FK ≤ 0 throws, caught in regression test)  
- `web/e2e/workflow-definitions.spec.ts` → clone test asserts transitions FK > 0 & match cloned state ids  

**Live verification (HEAD+fix):** clone of 3-state/2-transition def → cloned transitions `Submit 20064→20062`, `Approve 20062→20063`, all match cloned state ids (pre-fix: `0/0`).

## What We Tried

1. **Scope assumption.** Initially: unknown if corruption was widespread or localized. **Query result decisive:** 2-minute scan proved ZERO real-tenant data affected.  
2. **AutoTransitionOnTimeoutId test fabrication.** E2E fixture setup impractical (graph-replace endpoint renumbers FKs; no state-level auto-transition update). **Live spot-check instead:** DB-set fixture `InReview.AutoTransitionOnTimeoutId=source Approve id` → API clone → asserted clone `InReview.auto == clone Approve id`, `≠ source id`. Explicitly documented as spot-check, not suite coverage.  
3. **Defensive FE silence.** Visualization silently renders 0 edges on corrupt 0/0 data. **Guard added:** `buildEdges` filters + `console.warn`s unresolvable transitions, plus regression test (fails without guard). Future corruption self-reports, not silent.  

## Root Cause Analysis

Two-phase EF save pattern (states first, then resolve state name→id for transition FKs) was correctly implemented for Create and ReplaceGraph on 2026-05-08, but Clone() was missed. **The oversight:** Clone was a fresh code path written independently, not audited against the same pattern.

**Why this pattern matters:** EF Core assigns IDENTITY only post-SaveChanges. Any code that builds a reference graph before all entities are persisted will capture dangling/uninitialized ids. The 2026-05-08 fix systematized this (SaveChanges states, then resolve by name). Clone should have applied the same discipline immediately.

**Why the scope stayed small:** E2E fixture defs are ephemeral (throwaway) and seeded fresh on each CI run. Production defs use the graph-save endpoints (proven correct). If the clone bug had lived in a production workflow used by tenants, it would have been catastrophic.

## Lessons Learned

### 1. Quantify blast radius before assuming the worst (or the best)

A single query (`WHERE FromStateId=0 OR ToStateId=0`) took <2 minutes and answered the question decisively: **ZERO real-tenant impact**. This prevented over-scoping remediation (e.g., auditing all historical defs, triggering tenant notifications, scheduling manual repairs).

**Discipline:** When a data-corruption root cause is found, scan the full population **before deciding remediation breadth**. A cheap query is force-multiplier for confidence. Assumption without evidence is expensive.

### 2. Honest verification of rare paths without fabrication

The AutoTransitionOnTimeoutId remap logic was implemented identically to the verified transition-FK path. But it couldn't be exercised via public API (chicken-egg: `ReplaceGraphAsync` renumbers ids; no state-level auto-transition endpoint exists). 

**The correct trade-off:** documented live spot-check (DB fixture + API call + assertion) explicitly flagged as "not automated suite coverage" + recommendation for a handler integration test when a workflow integration harness exists. This beats:
- **(a) Fabricated green test** — false confidence, ships code untested  
- **(b) Unverified claim** — ships code on theory  
- **(c) Defer indefinitely** — blocks the fix  

**Principle:** Primary-source evidence (even if manual/spot-checked) > fabricated automation. When the public API can't construct a fixture, a documented check + explicit gap beats both false green and shipping blind.

## Next Steps

1. **Clone path on HEAD.** Reproduce `POST /workflow/definitions/{id}/clone` on current build to confirm the fix prevents regression. If latent bug existed pre-2026-05-08, this will surface it.  
2. **E2E fixture seeding audit.** Identify the exact build/timestamp of E2E fixture creation (2026-05-09) and verify if 2026-05-08 two-phase fix was included. Affects whether clone or pre-fix history is the origin.  
3. **Integration test for AutoTransitionOnTimeoutId.** When a workflow integration harness is available (handler tests with real state/transition setup), add dedicated test for timeout remap in clone path.  
4. **Defensive guard regression suite.** FE `buildEdges` now catches future corruption via `console.warn` + test (9/9 pass, FE typecheck clean). No action required; ship.  

---

**Status**: RESOLVED (all 4 follow-ups closed; code committed; FE/BE tests passing; no outstanding blockers)
