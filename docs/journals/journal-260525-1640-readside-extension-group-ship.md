# T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION-GROUP Ship

**Date**: 2026-05-25 16:34-17:32 ICT  
**Severity**: Low  
**Component**: Workflow read-side (group membership eligibility)  
**Status**: Resolved  
**Version**: v2.137p113 commit `5e405eb9`

## What Happened

Shipped the ByGroup read-side arm of `T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION`, mirroring the ByRole producer+reader pattern shipped same morning (v2.137p110). Added `IIdentityModuleClient.GetUserGroupCodesAsync(userId, tenantId, ct)` — a Dapper lookup for direct group membership — and wired group codes into `IsPoolEligibleAsync` and `GetInboxAsync` with new `PrincipalType.Group` disjunction. Single slice ~1h, pattern-mirror discount applied. **8th ship of the day.**

## The Brutal Truth

This was genuinely clean. The pattern-mirror precedent meant zero design decisions, zero architectural surprises. The real win was catching a backlog row lie at slice-start: the row claimed we could reuse `ResolveActiveUsersInGroupsAsync` (the Phase 02 producer) for the read-side. Empirical grep caught it — that method inverts direction (groupCodes → userIds). We needed the opposite (user → groupCodes). That 5-minute reality check saved ~30 minutes of implementing the wrong shape.

The mid-slice advisor catch stung a little: I'd planned 5 test facts (Identity layer), but the ByRole twin shipped with 4 WF.Integration anti-tautology facts. I missed the symmetry. Adding 4 mirror facts cost ~20 minutes but locked the same fail-CLOSED rigor. Advisor flag was right — pattern-mirror means test-mirror too.

## Technical Details

**New code footprint:**
- `IIdentityModuleClient.GetUserGroupCodesAsync(Guid userId, Guid tenantId, CancellationToken)` — direct-membership Dapper query, `IReadOnlySet<string>` return
- `IWorkflowTaskRepository.IsPoolEligibleAsync` adds `IReadOnlySet<string>? groupCodes` parameter; updated predicate OR-chain with `PrincipalType.Group` arm
- `GetInboxAsync` wired `groupCodes` passthrough from caller
- `ClaimTaskCommandHandler` + `GetInboxQueryHandler` fetch groupCodes via Identity client before routing

**Test deltas:**
```
WF.App:              572 → 574 (+2)
WF.Infra:            84 → 84 (—)
Identity.Infra:      150 → 154 (+4 unit)
Identity.Integration: 207 → 211 PASS / 1 pre-existing FAIL
WF.Integration:      52 PASS + 4 SKIP (unchanged)
Architecture:        105 (unchanged)
Build:               0E / 0W
```

Baseline run identified pre-existing `SignatureConfigApiTests.Get_SystemAdmin_AfterUpsert_Returns200WithConfig` failure at clean HEAD `d96ffe42` — NOT introduced by this work, but quantified HEAD drift.

## What We Tried

**Initial test plan:** 2 WF.App (propagation) + 4 Identity.Infra (unit) + 5 Identity.Integration (DB roundtrip).  
**Mid-cycle:** Advisor flagged missing WF.Integration anti-tautology facts (4 necessary-vs-sufficient principals per ByRole twin). Added `WorkflowTaskGroupClaimIntegrationTests` mirroring `WorkflowTaskRoleClaimIntegrationTests`.

## Root Cause Analysis

No real failures — just an incomplete first-pass test plan. Advisor catch was pure value-add (pattern-mirror discipline). The backlog row direction-misclaim was the only gotcha, but audit-at-slice-start (INVARIANT 260522-A) caught it cold.

## Lessons Learned

1. **Audit-at-slice-start still works.** Backlog row claimed reusable method was wrong direction (inverted). Empirical grep on the actual method body took 5 minutes and prevented 30 minutes of misimplementation. 260522-A honored.

2. **Pattern-mirror test-mirror.** When shipping a sister feature off a twin precedent, count test facts the same way. ByRole = 4 WF.Integration anti-tautology; ByGroup = 4 WF.Integration anti-tautology. Advisor catch flagged the gap mid-slice.

3. **Cross-module tenant-parameter pattern holds.** New `IIdentityModuleClient` method carries `Guid tenantId` like siblings (260525-E honored). Audit grep confirmed zero regressions.

4. **Pattern-mirror discount stays tight.** 4-hour ByRole precedent → 1-hour ByGroup = ~4× faster than naive estimate. No design surface, no contingency burn.

## Next Steps

1. **File standalone backlog row** for pre-existing `SignatureConfigApiTests.Get_SystemAdmin_AfterUpsert_Returns200WithConfig` — discovered during full baseline, unrelated to this work, quantifies HEAD drift for next session.

2. **Optional follow-up:** `WF-CANDIDATEINDEX-CANONICALIZATION-HARDENING` (low-priority, twin-inherited 3 Low items: SQL UPPER vs ToUpperInvariant collation drift, 4-fact cross-arm disjunction gap, 3 sequential identity round-trips per Claim).

3. **Unblock Phase 04 ByOrgUnit:** Same producer+reader pattern ready to mirror once product ratifies temporal-assignment questions.

---

**Cycle breakdown:** Scout 10m + Plan 5m + Impl 15m + Test 20m + Build+baseline 10m + Code-review+advisor 10m + Docs+commit+journal 10m = **~80m actual vs 3-5h plan estimate.** Pattern-mirror discount honored. 260522-O reinforced.
