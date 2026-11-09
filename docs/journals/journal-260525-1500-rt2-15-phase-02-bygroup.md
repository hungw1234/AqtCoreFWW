# RT2-15 Phase 02 ByGroup: Predicate-Pool Producer + Cross-Tenant Fix

**Date:** 2026-05-25 12:12–15:00 ICT (≈3h cycle)
**Severity:** High (cross-tenant leak caught + fixed mid-cycle)
**Component:** Workflow.Application validator + resolver, Identity.Contracts client, FE assignment editor
**Status:** Shipped v2.137p112 commit `b7b80b4a` (push pending GitHub 500 retry)
**Tests:** WF.App 572/572, Arch 105/105, Identity.Infra 150/150, Identity.Integration 207/207 (+3 cross-tenant), WF.Integration 52/52+4 skips. Build 0E/0W.

## What Shipped

ByGroup mode (mode 5) end-to-end: Save-time validation hop to Identity (`ResolveActiveUserGroupsByCodesAsync`), predicate-pool producer wired in resolver (mirrors ByRole), FE group multiselect component, 9 new files, 39 file modifications, 2430 insertions / 67 deletions. Single commit per INVARIANT 260523-D.

Key technical payload:
- `IIdentityModuleClient.ResolveActiveUserGroupsByCodesAsync(codes, tenantId, ct)` validates group codes exist + active at Save time → 422 GraphValidationFailed with `UnknownGroupCodes` metadata if unknown.
- `IIdentityModuleClient.ResolveActiveUsersInGroupsAsync(codes, tenantId, ct)` for runtime resolution (forward-compat; predicate-pool path doesn't call it yet per 260524-F).
- Validator ByGroup case: canonicalize codes (Trim+Upper), batch-validate via Identity client, emit R3 violation if unknown.
- Resolver ByGroup case: calls predicate-pool factory, populates `CandidatePrincipalType.Group` predicates union-combined per transition.
- Seeder `E2ETestDataSeeder.UserGroups.cs`: 2 test groups + 4 memberships (active/inactive coverage).
- FE: mode 5 in editor, new `<WorkflowGroupMultiSelect>` + `use-user-groups.ts` hook.

## The Brutal Truth

Code reviewer C1 caught a **cross-tenant data leak in both new IIdentityModuleClient methods 90 minutes before commit.** Both `ResolveActiveUserGroupsByCodesAsync` + `ResolveActiveUsersInGroupsAsync` lacked `Guid tenantId` parameter. Save-validation SQL returned group codes from ANY tenant; runtime resolution pooled users across tenant boundaries. Severity: **immediate leak today** (Save-time validation false-pass), **armed for activation** (read-side clause/inbox will fire when P0 extension ships).

This was a catastrophic miss because:
1. **Sibling precedent existed.** Phase 01 landed `GetUserRoleNamesAsync(userId, tenantId, ct)` same author, same IIdentityModuleClient — tenant threading pattern established 1 PR prior.
2. **Architecture audit (orchestrator 260525-1204) didn't grep for tenant patterns.** Focused on file paths + line drifts, skipped semantic audit of parameter signatures.
3. **Plan-doc pseudocode lacked tenant.** Agent A copied signatures verbatim; plan specs were wrong; no one re-audited them.
4. **Single-tenant integration tests don't expose the leak.** All I1-I5 tests use random `TenantId = Guid.NewGuid()` per class — zero cross-tenant assertions possible.

The tester's initial report also tried to "accommodate" the Q2 hop by claiming it was "envelope limitation" why I1 ByGroup assertion couldn't be verified, and suggested downgrading assertions to "status-only" citing "Phase 05 deferral." Orchestrator probed: seeder NEVER ran because `env.IsDevelopment()` check + WebAppFixture sets env = `"Testing"` → groups didn't exist → Q2 correctly returned 422. **NOT a limitation; a real bug.** Once fixed, I1 passed cleanly.

Frustrated? Yes. The cycle had 6 agents + 2 gates running in parallel; C1 was the circuit breaker that prevented a production-ready commit containing a multi-tenant authz bypass. **That's the point of code review.** But it means Phase 02 is NOT the clean story we hoped for — it's a reminder that cross-module security boundaries need empirical proof, not pattern-matching.

## Technical Details

**C1 Cross-Tenant Leak (Root Cause Analysis):**

```csharp
// ❌ SHIPPED (wrong)
Task<IReadOnlySet<string>> ResolveActiveUserGroupsByCodesAsync(
    IReadOnlyCollection<string> codes, CancellationToken ct);
    
// Dapper SQL — no tenant predicate
SELECT DISTINCT Code FROM [identity].[UserGroups]
WHERE Code IN @Codes AND IsActive = 1

// FIXED (correct)
Task<IReadOnlySet<string>> ResolveActiveUserGroupsByCodesAsync(
    IReadOnlyCollection<string> codes, Guid tenantId, CancellationToken ct);
    
// Dapper SQL — tenant + global-group escape
SELECT DISTINCT Code FROM [identity].[UserGroups]
WHERE Code IN @Codes AND IsActive = 1 
  AND (TenantId = @TenantId OR TenantId IS NULL)
```

**Attack vector A (Save-time validation bypass — LIVE TODAY):**
- Tenant A editor saves workflow with ByGroup clause `["GROUP_FINANCE"]` (doesn't exist in Tenant A).
- Tenant B has `GROUP_FINANCE` active.
- Q2 hop blindly returns `["GROUP_FINANCE"]` from Tenant B row.
- Validator computes `unknown = []` → save succeeds.
- Tenant A workflow definition now persists a **cross-tenant code reference.**

**Attack vector B (Runtime authz bypass — LATENT, armed at P0 Phase 05):**
- Once read-side claim/inbox ships (imminent P0 per backlog), `ResolveActiveUsersInGroupsAsync` returns Tenant B's active users into Tenant A's candidate pool.
- Tenant B users gain visibility into Tenant A's tasks.

**Why tests didn't catch:** Single-tenant test classes. `SeedActiveGroupAsync` uses `TenantId = Guid.NewGuid()` — each test gets its own tenant. No cross-tenant assertion possible. A good reminder that **test isolation design can hide security leaks.**

**Fix scope (mechanical, ~1h):**
1. Add `Guid tenantId` to both method signatures.
2. Thread through validator + Activate handler (both have access to definition.TenantId or request.TenantId).
3. Update Dapper SQL: add `AND (TenantId = @TenantId OR TenantId IS NULL)` predicate.
4. Add integration test I-CT1 (seed GROUP_X in Tenant B, attempt save with ["GROUP_X"] in Tenant A → expect 422).
5. Update 4 integration test stubs + 4 unit-test stubs (new parameter in mock implementations).

**Fixed:** Code review requested C1 fix; fix agent completed in ~1h; re-tested I1-I5 + new cross-tenant locks I-CT1 + I-CT1b all GREEN.

## What We Tried

1. **Plan-driven implementation (Agents A/B/C/D in parallel):** Followed phase-02.md architecture exactly. Agents delivered without C1 discovery.
2. **Orchestrator pre-fan-out audit (260525-1204):** Checked file paths + line drifts. **Missed semantic drift in signatures.** False confidence.
3. **Tester accommodation instinct (Agent D mid-cycle):** Claimed envelope limitation on I1 assertion. Orchestrator empirical probe (3 questions) revealed **real bug, not limitation.** Tester's fallback-to-deferral was a reasonable signal ("I can't verify this") but needed orchestrator verification before acceptance.

## Root Cause Analysis

**Why the leak happened:**

1. **Plan-doc pseudocode wrong.** Phase-02 spec had bare method signatures; no sample tenant flow shown. Agent A copied them literally without checking sibling methods on the same interface (GetUserRoleNamesAsync established the pattern 1 PR prior).
2. **Orchestrator audit scoped too narrow.** 260525-1204 was "did agents follow the plan + file paths correct?" — skipped "do the signatures match established patterns?" Deep audit (grep sibling interface methods) would have caught it pre-code-review.
3. **Architecture Tests don't validate parameter patterns.** `Architecture.Tests` checks "no entity type leaks" but says nothing about "security-critical parameters must include TenantId." A new invariant candidate.
4. **Single-tenant test design enabled the leak.** Tests pass because isolation is perfect within each tenant; cross-tenant attack invisible. Design excellence in one dimension (test isolation) hides failure in another (multi-tenant verification).

**Why code review caught it:**
- C1 reviewer did the grep orchestrator skipped: "both methods missing tenantId despite sibling precedent" + "attack surface analysis (Save-validation + runtime resolution)."
- Severity escalation instinct: "this is a security boundary, not a nit."

## Lessons Learned

**INVARIANT 260525-C (NEW — PROMOTE):** When integration tests report an "accommodation" or "deferred" or "limitation" claim, orchestrator MUST empirically verify root cause before accepting. Tester's first move on blocked assertion is to soften it; we need a 3-probe max gate: (1) Can this be unit-tested separately? (2) Is the dependency real or a test-setup issue? (3) Would a cross-tenant / cross-actor test reveal the problem? If any probe yields a different answer, the root cause is likely not "limitation" but "bug."

**INVARIANT 260525-E (NEW — PROMOTE):** Cross-module client interface methods that accept user-input collections MUST be audited for TenantId patterns BEFORE code review. When adding new methods to an interface that already has security-critical parameters, grep siblings on the same interface for parameter consistency. This is a security boundary; tenant parameter omission = leak. Promotion trigger: saved us from 2 attack surfaces in this slice alone.

**REINFORCE 260524-F (Producer-without-reader gate):** This is the 2nd instance of "plan says eager-resolve / architecture says predicate-pool" (ByRole hit it Sun 2026-05-24). The architectural constraint is real: `HandlerResolution.Finalize` throws on multi-user mixed resolution. Phase 04 ByOrgUnit will hit the exact wall. **Recommendation:** Planner should pre-author Option A (predicate-pool + read-side gap) as the default path for Phases 03/04, not Option B (eager-resolve). Cost: 2 re-gates per phase; benefit: 3–5h per phase saved on discovery work.

**REINFORCE 260523-D (Fail-closed bundling):** Feeder (Save-validation hop Q2) + reader (predicate-pool producer) shipped together. No dangling fail-closed without producer. 3 doc artifacts present: Resolver xmldoc + Validator comment + Domain enum xmldoc + P0 backlog row filed. Pattern holds across ByGroup. Expect ByOrgUnit + ByUser to follow same bundling.

**OBSERVATION 260525-D (Tester "deferred" claims decay):** Tester's report listed 3 Phase 05 items ("envelope metadata," "partition pattern consistency," "E2E coverage"). Orchestrator categorization: 1 is real (envelope = Task #14 backlog row existing); 2 are redundant (already locked by U1-U4 unit tests + domain anti-tautology). Memo: 60-80% of "Phase X deferred" claims in test reports evaporate on review. Don't file every suggestion as a backlog row.

## Next Steps

**Immediate (already done):**
- C1 fix applied + I-CT1/I-CT1b cross-tenant locks GREEN.
- Single commit pushed (local; GitHub 500 transient blocking push retry).

**Carry-forward (M-priority):**
- M1: Envelope structured violation metadata serialization (Task #14 already filed).
- M3: Workflow-admin picker sees all tenant group codes → product policy review flag (low severity; cross-module authz intentional per spec).
- Orval regen for `use-user-groups.ts` when backend running (informational; hook hand-written for now).

**Phase 03 (ByRole mirror — imminent):**
- Apply same tenant-audit before Agent A ships.
- Pre-author Option A (predicate-pool default) in planner docs.
- Cross-tenant integration test scaffold (I-CT pattern).

**Phase 04+ (ByOrgUnit, ByUser):**
- INVARIANT 260525-E audit: cross-module client methods + tenant pattern grep.
- Test isolation design review: single-tenant-per-class can hide multi-tenant leaks.

**Architecture.Tests expansion (future):**
- Candidate invariant: "security-critical parameters (TenantId, ActorId) must be present on cross-module client methods that access user data."

## Open Questions

1. **Should `ResolveActiveUsersInGroupsAsync` be removed?** Currently shipped but unused (predicate-pool path doesn't call it). YAGNI says delete; forward-compat says keep + xmldoc note. **Decision:** Keep (mirrors ByRole precedent). Revisit at T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP closure if still unused.

2. **Is Activate path tenant-audit needed?** Activate reads pre-persisted Refs (already validated Save-time); runs defensive canonicalization only. Tenant context: does Activate cross tenant boundaries (admin moved), or always Activate within original tenant? Probably no risk (pre-persisted), but worth a 1-line comment for Phase 03 planner.

3. **Why did plan-doc have wrong signatures?** Specification pre-implementation phase missed the "check siblings first" step. Process gap: plan authors should grep interface for precedent before writing pseudocode signatures.

## Metrics

| Metric | Value |
|--------|-------|
| Cycle duration | 3h 0m (vs 5-7h plan estimate) |
| Agents spawned | 6 primary + 1 rerun (D tester fix probe) + 1 fix (C1) |
| Code review iterations | 1 REQUEST-CHANGES → 1 fix agent → APPROVE 9.2/10 |
| Files modified | 39 total (19 existing + 9 new + 11 test stubs) |
| LOC delta | +2430 / -67 |
| Tests added | +18 (4 U1-U4 unit + 5 resolver + 5 ByGroup integration + 2 Identity integration + 2 cross-tenant I-CT + FE-A4c) |
| Critical issues found | 1 (C1 cross-tenant leak) |
| Medium issues carry-forward | 2 (M1 envelope, M3 picker authz) |
| Build | 0E / 0W |
| Regression tests | All GREEN (Architecture 105/105, full Workflow.App 572/572 baseline preserved) |

## Files Changed (Absolute Paths)

Core: `src/shared/AqtCoreFW.Identity.Contracts/IIdentityModuleClient.cs`, `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Services/InProcessIdentityModuleClient.cs`, `src/modules/workflow/AqtCoreFW.Workflow.Application/Validation/WorkflowGraphValidator.cs`, `src/modules/workflow/AqtCoreFW.Workflow.Application/Services/WorkflowHandlerResolver.cs`.

FE: `web/src/features/workflow/components/transition-assignment-editor.tsx`, `web/src/features/workflow/components/workflow-group-multi-select.tsx` (NEW), `web/src/features/workflow/hooks/use-user-groups.ts` (NEW).

Tests: `src/tests/AqtCoreFW.Tests.Integration/Workflow/WorkflowByGroupProducerAndSaveValidationTests.cs` (NEW), `src/tests/AqtCoreFW.Tests.Integration/Identity/IdentityModuleClientUserGroupsTests.cs` (NEW), 4 integration test stub patches (ExecuteTransitionContractTests, etc.).

Seed: `src/tests/AqtCoreFW.Tests.Integration/Seeders/E2ETestDataSeeder.UserGroups.cs` (NEW).

---

**Summary:** Phase 02 shipped end-to-end ByGroup producer with predicate-pool option and critical cross-tenant leak fix caught mid-cycle. Lesson: code review + empirical orchestrator probes beat plan-following automation. Architecture boundaries (multi-tenant, cross-module) need explicit audit gates, not pattern inheritance from pseudocode.
