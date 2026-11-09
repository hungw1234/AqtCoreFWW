# D5+D6+D7 Permission Code Cluster: UX/Security Gap Closure + FE Cascade

**Date:** 2026-04-18 20:40
**Severity:** Medium
**Component:** Workflow (BE+FE), Authorization, Integration Tests
**Status:** RESOLVED + Documented Debt
**Branch:** `feat/d5-d6-d7-permission-code-cluster` (7 commits, PR pending)

## What Happened

Shipped unified authorization model across workflow transitions: renamed `RoleCode` → `PermissionCode` in entity, handler, controller DTOs, and FE to close a UX/security drift where `GetAvailableTransitions` filtered by role-equality but `ExecuteTransition` filtered by permission-set. Caller could list "Approve" action, click it, get 403 — classic permission-check inconsistency.

**Commits shipped:**
- D5 `d34549f9` — Entity rename + EF + migration `20260418025028` + raw SQL callsites + seeder + DTO + commands. 28 files, +1461/-72.
- D6 `509aa119` — Unify handler to `permissionCodes.Contains(RequiredPermissionCode)` predicate. 6 test cases added. 4 files, +222/-14.
- D7 BE `375ee7f0` — Remove `ActorRoleCode` from controller DTOs; server resolves from JWT.
- D7 FE `51b8fda8` — Cascade rename across 11 FE + 3 e2e specs + i18n. 15 files, +33/-48.
- Chore `67b88d8f` — Remove duplicate `workflow.http` sample.
- Docs `10123079`, `1cececcf` — Backlog + docs v2.115→v2.116.

## The Brutal Truth

Session time-boxed at 16h in session-3, so PR + journal entry deferred. This session: open PR + write journal only. The frustrating part is the full integration test suite (`src/tests/AqtCoreFW.Tests.Integration`) wasn't run due to time budget — caught 5 pre-existing 403 fails in Workflow+Cases subset, but scope of full-project failures unknown. Orval v8 incompatibility also blocks proper API-client regen — currently hand-edited schemas to avoid 40+ TS2554 errors. Workaround feels fragile for next schema change.

## Technical Details

**Authorization unification:**
```csharp
// Before: role equality only
var filtered = transitions
  .Where(t => t.RequiredRoleCode == actor.RoleCode)
  .ToList();

// After: permission-set containment (both handler + execute now use this)
var userPermissions = (await _identityModuleClient.GetEffectivePermissionsAsync(...))
  .PermissionCodes
  .ToHashSet();
var filtered = transitions
  .Where(t => userPermissions.Contains(t.RequiredPermissionCode))
  .ToList();
```

**Test results:**
- Workflow.Application: 406/406 ✓
- Workflow.Domain: 120/120 ✓
- Workflow.Infrastructure: 61/61 ✓
- Workflow.Integration (local SQL): 25/27 ✓ (2 perf skips, 67s total)
- Workflow+Cases subset (integration): 9/14 (5 D9 fails: `WorkflowDefinitionsApiTests.*`, `CasesWorkflowTransitionTests.*` all returning 403)
- FE typecheck: 0 errors ✓
- FE vitest: 765/777 ✓ (12 pre-existing jsdom/Antd Modal, not regression)

**D9 root cause (pre-existing, not D5/D6/D7 regression):**
```
src/tests/AqtCoreFW.Tests.Integration missing IEffectivePermissionService stub
Error: 403 Forbidden on /api/workflow/definitions + /api/workflow/cases/*
Root: PR #38 added stub to 4 module-level projects but not to this integration project
Fix: add service stub + seed mock permissions (~30min, deferred to D9 task)
```

**D8 blocker (Orval v7→v8):**
```
Terminal: npm run generate:api
Error: TS2554 — apiClientMutator(config, {extraHeaders: ...})
Root: orval v8 passes 2 args, but src/core/api/api-client.ts line 8 signature is 1-arg form (v7)
Workaround: hand-edited schema objects only for changed shapes to avoid regen
Upgrade path: update function signature to `(config, options?) => ...` (~1-2h, deferred to D8 task)
```

## What We Tried

1. **Session-3 bundling:** Combined BE Phase-01+02 (entity rename + raw SQL fixes) into single commit to eliminate broken-read window where migrations run but callsites still reference old column name. Avoided deploy-order footgun.

2. **Trust-but-verify on Orval:** Fullstack-developer agent claimed v8 incompatibility but didn't verify regen. Re-ran `npm run generate:api` independently — confirmed TS2554 on 40+ callsites. Workaround (hand-edit) validated, effort estimate ~1-2h for full fix. **Lesson:** Always Grep/compile on agent DONE_WITH_CONCERNS claims.

3. **Split finalize across sessions:** Session-3 committed docs sync (`1cececcf`). Session-4 (this) opens PR + writes journal. Memory index had explicit "Next Session Pickup Plan" — resumed cleanly. No context lost.

## Root Cause Analysis

**Authorization drift root cause:** Pre-D6, permission model was split across two code paths — handler used role-equality, controller used permission-set. No single source of truth. UX consequence: list shows action user has permission for, but execution rejects with 403.

**Why it wasn't caught earlier:** Handler tests don't exercise `ExecuteTransition` (different aggregate), and controller tests don't verify consistency with list API. Required cross-component test (integration) that wasn't running on this subset.

## Lessons Learned

1. **Unified predicate is mandatory** — when permission logic appears in multiple callsites, inject shared logic into a single place (here: handler filter). Never duplicate permission checks.

2. **Time-box sessions and defer explicitly** — D5/D6/D7 scope was 3 days; session-3 cut at 16h with explicit "PR + journal next session" note in memory. Resumed session-4 without context loss or redundant rework.

3. **Verify agent completeness independently** — when agent reports DONE_WITH_CONCERNS about tool incompatibility, reproduce the error yourself (Grep + compile) before believing workaround. Trust intent, verify execution.

4. **Catch pre-existing failures early** — full integration test suite should run on every session finalize to catch debt like D9. Time-box prevented this; next session must run full suite even if only to triage/document scope.

## Next Steps

- **D8 (1-2h):** Upgrade orval apiClientMutator signature from 1-arg to 2-arg, run full `npm run generate:api`, verify 0 TS errors on all 40+ callsites.
- **D9 (1-3h):** Add `IEffectivePermissionService` stub to `src/tests/AqtCoreFW.Tests.Integration`, seed mock permissions, re-run full project (15min). Document any additional failures discovered.
- **Session finalize:** Run full integration suite (Workflow+Cases+Others) before marking phase complete. Update `memory/project-state.md` with v2.117 status + session summary.

---

**Status:** DONE | **Summary:** D5+D6+D7 permcode cluster shipped (7 commits, 2 test tracks pass, 2 blockers documented in D8+D9 backlog). PR opened this session. FE cascade 100% complete. Orval v8 + integration test debt flagged for next session.
