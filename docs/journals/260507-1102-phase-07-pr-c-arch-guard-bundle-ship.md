# Phase-07 PR-C: Arch-DTO-Guard + Tech Debt Bundle Ship

**Date:** 2026-05-07 11:02  
**Severity:** Medium  
**Component:** Architecture, Application DTOs, Partner Module  
**Status:** Resolved  

---

## What Shipped

PR #81 squash `5a70062f` + post-merge docs sync `312ed4a8`.

**3 deferred backlog items from PR-B + 1 docs sync:**

1. **ARCH-DTO-GUARD** (`59565215`) — `Rule_T2_External_Surface_MustNot_Leak_LongFK` source-level architecture fact. Bans `long {N}Id` on Application surface (DTOs, Commands, Queries) where N = T2 entity name.
2. **UpdateContract N+1 collapse** (`5710eb2c`) — replace post-SaveChanges EF `IPartnerRepository.GetByIdAsync(long)` with Dapper SELECT JOIN projection. 3 EF roundtrips → 2.
3. **Partner repo cleanup** (`b60c6c44`) — delete orphaned `IPartnerRepository.GetByIdAsync(long)` (zero callsites post-Phase-2).
4. **Docs sync** (`790b84c9`) — update Partner row in `docs/dependency-map.md` to reflect T2 tier + post-save event + narrowed repository interface.

Code review: APPROVE 9/10 (see `plans/260507-0905-phase-07-pr-c-arch-guard-tech-debt/reports/code-reviewer-260507-1011-GH-07-pr-c-bundle-review.md`).

Tests: Architecture suite 16/17 GREEN (Rule6 residual 3 = pre-existing). Solution build clean.

---

## The Brutal Truth

Plan validation was **dangerously optimistic**. Predicted "rule passes on first run" from local context of ~10 files. Reality: **22 real T2-FK leaks across 6 modules + 49 raw regex hits**. The plan's Phase-1 baseline audit only spot-checked `IntegrationDtos.cs` (3 records) and concluded "clean state." That's the architectural equivalent of testing one user on staging and calling it production-ready.

Also discovered **CSV registry drift** — `entity-tier-mapping-data.csv` hadn't been synced since Phase-04a/AMEND-A13. PR-A updated FormTemplate source to T2; PR-B updated Partner source to T2. CSV still said T1 for both. Result: rule was toothless for the exact entities it should guard. Caught only because first rule run flagged them.

The suppression strategy (7 entities in `T2FkDriftBacklog`) feels pragmatic but **masks systemic drift**. Chose this over 10h+ of immediate fixes, betting each future retier-wave PR drops 1 entity. High confidence this will happen, but it's a future-liability bet with present-day leverage.

Integration test suite still hits `WebAppFixture` seeder gap (3rd occurrence — memory `false-green-pattern-3rd-occurrence`). 110 tests pass, but API startup crashes on real environment without explicit seeder invocation.

---

## Technical Details

### ARCH-DTO-GUARD Rule

**File:** `src/Architecture.Facts/Rules/ArchitectureRuleT2ExternalSurfaceGuard.cs`

Scans `*.Application/{DTOs,Commands,Queries}/*.cs` for pattern `long {N}Id` where N ∈ `T2FkDriftBacklog`. Reads per-line via `File.ReadAllLines` + regex.IsMatch.

**Caveat:** Cannot distinguish `long RuleId` (T2 `rules.Rule` violation) from `long RuleId` (T1 `identity.AbacRule.Id` internal — legitimate). When future cleanup drops `Rule` from suppression, may need per-file suppress comment or Roslyn AST.

### UpdateContract Handler

**File:** `src/Modules/Contract/Contract.Application/Commands/UpdateContractCommandHandler.cs:80-100`

Pattern: post-SaveChanges `IReadDbConnection` single SELECT JOIN (not EF roundtrip):

```csharp
var updated = await _readDb.QueryFirstOrDefaultAsync<ContractForPartnerProjection>(
  sql: @"
    SELECT c.Id, c.PublicId, c.PartnerId, p.PublicId AS PartnerPublicId
    FROM [contracts].Contracts c
    INNER JOIN [masterdata].[Partners] p ON c.PartnerId = p.Id
    WHERE c.Id = @ContractId",
  param: new { ContractId = aggregate.Id },
  transaction: txn
);
```

Follows established `IMessageLogStatusCommand` pattern from Phase-2. Reserved schema bracket-escape (`[integration].*`) per memory `feedback-mssql-reserved-schema-escape`.

### CSV Sync

**File:** `src/Architecture.Facts/Support/entity-tier-mapping-data.csv`

FormTemplate + Partner rows updated from T1 → T2 inline as part of Phase-1 commit `59565215`.

---

## What We Tried

1. **Predict leaks from code review** → Failed. Assumed small number of violations; 22 discovered at rule runtime.
2. **Skip CSV sync** → Failed. Rule fired on entities not yet registered T2. Forced inline fix.
3. **Full remediation in PR-C** → Rejected. 22 leaks × ~30 min each = 11h effort. Suppression strategy approved instead.

---

## Root Cause Analysis

**Plan Validation Gap:** Baseline audit was manual inspection, not automated rule execution. Should have RUN `Rule_T2_External_Surface_MustNot_Leak_LongFK` against current `main` during planning phase to uncover real drift before scoping.

**CSV Drift:** No automated check that `entity-tier-mapping-data.csv` stays in sync with `*.Domain/*.cs` `IPublicIdentified` trait. Two prior tier-changes (PR-A, PR-B) updated source but skipped CSV update.

**Systemic Drift in 6 Modules:** T2 retier waves (Phase-07 PR-A/B) brought entities into T2 tier; Application surface was written before retier decisions solidified. No guard rule existed until PR-C.

---

## Lessons Learned

1. **Arch-rule plans must execute the rule itself during validation.** Predicting rule results from code review is unreliable at codebase scale. Always RUN on current main during planning, not just static analysis.

2. **Tier classification is a multi-file sync problem, not a single-file change.** CSV + source class + repository interface + DTO surface must all move together. Consider enforcing this with an architecture fact OR inline comments.

3. **Pragmatic suppression + future debt is valid when immediate fix is >10h.** But suppression list must be visible and tracked. `T2FkDriftBacklog` as a HashSet buried in rule code is discoverable but not as visible as a JSON allowlist (e.g., parallel to `architecture-test-allowlist.json`).

4. **Dapper post-write JOIN is the established idiom for read-side symmetry.** Phase-2 set precedent; Phase-07 PR-C mirrors it verbatim. IReadDbConnection scoped DI + transaction param enables atomic post-write consistency checks.

5. **Line-based regex cannot distinguish legitimate internal Ids from external T2-FK leaks.** Future Roslyn AST upgrade or per-file suppress comments will be needed when dropping `Rule` from suppression.

---

## Next Steps

1. **Spawn Phase-07 T2-Retier Waves Cleanup backlog** — 7 entities (FileRecord, OrgUnit, WorkflowDefinition, WorkflowInstance, Message, Rule, FormTemplate) × 22 leaks. Each future retier-wave PR drops 1 entity from `T2FkDriftBacklog`. Target 1-2 entities per PR.

2. **Remaining T1 Retier Wave** — 3 residual entities (ai.AiModelProfile, ai.AiPromptTemplate, identity.MfaPolicy) flagged by Rule6 as T1 in CSV but `IPublicIdentified` at source. Verify intent + reclassify or suppress.

3. **CSV Sync Guard** — Consider architecture fact or doc comment enforcing manual CSV update whenever tier classification changes.

4. **WebAppFixture Seeder Gap** — Plan fixture overhaul or explicit post-Build seeder invocation. 3rd pattern occurrence (memory `false-green-pattern-3rd-occurrence`). Backlog action for Phase-08.

---

## Unresolved Questions

- Should `T2FkDriftBacklog` evolve into JSON allowlist (parallel to `architecture-test-allowlist.json`) for discoverability, or stay inline HashSet for containment?
- Next retier-wave PR: target highest-impact entity (e.g., WorkflowDefinition — 7 violations) or easiest (e.g., OrgUnit — 2 violations)?
- Should CSV sync be enforced via architecture fact, or is manual due-diligence + code review sufficient?
