# Demo Rules Seeder (P7a) — Mixed Save Shape & Idempotency-Probe Trade-off

**Date**: 2026-06-02 15:30
**Severity**: Low
**Component**: Rules.Infrastructure/Seeding
**Status**: Resolved

## What Happened

Shipped P7a = `DemoRulesSeeder` + `DemoRulesSeederOptions`, second of the niche seeders in P7 reseed plan. Module-resident, mirrors P7c Integration + prior slices P4/P5. Seeds 1 Draft RuleSet + 2 expression Rules + 1 DecisionTable per tenant (BTC + BNV) — both rule-authoring modes the module supports. Left Draft (activation is a domain event, never happens in seeding). Gated `Rules:DemoRulesSeed` OFF. Build clean 0E/0W, code-review APPROVE 0 Crit/High (cleaner than P7c's APPROVE-WITH-NITS). Tests 3/3 real-SQL + startup smoke + full Rules.Integration 9/9 no regression. Commits `4840ecaa` + `cbfdf8d4`, PUSHED origin + bitbucket, ahead=0, v2.137p141.

## The Brutal Truth

P7 structure is now clear: each niche module (Rules/Reporting/Integration) is a SELF-CONTAINED demo-data island. No cross-module FK, no shared schema — just the module owns its seeder. This is architecturally clean but operationally leaves THREE separate idempotency probes with subtly different constraints (no unique index on RuleSet; unique on FormTemplate; unique on IntegrationJob). The honest read: we're building tiny data factories, not a cohesive demo-data story. Each seeder is a one-off shape, and there's no leverage to abstract this further without YAGNI widening. Feels fragmented, but it's honest.

The review was faster than P7c because I applied the transaction-wrap lesson PROACTIVELY (had learned it in P6/P7c, didn't wait for reviewer to flag it again). That's the right pattern for lessons: propagate them forward, don't make the same reviewer find the same issue twice.

## Technical Details

### Mixed Save Shape (Empirically Verified)

Phase 1: RuleSet + 2 Rules in ONE SaveChanges.
- Rules added via mapped nav `RuleSet.Rules` (HasMany in RuleSetConfiguration.cs:27).
- EF nav-fixup: Rule.Create called with ruleSetId=0, EF fixes up Rule.RuleSetId from assigned RuleSet.Id on the collection (same pattern P5 FormTemplate used).
- Verified in test: T1 asserts both rules have correct RuleSetId after save.

Phase 2: DecisionTable in separate SaveChanges.
- DecisionTable has loose RuleSetId column, NO FK constraint (no nav on RuleSet, no HasOne back-reference).
- Requires assigned RuleSet.Id explicitly: `decisionTable = new DecisionTable { RuleSetId = ruleSet.Id, ... }`.
- Different from Rules because DecisionTable is NOT in a mapped nav collection.

Both phases wrapped in BeginTransactionAsync/CommitAsync (lesson from P6 DemoBusinessProcessSeeder where partial failure left root with 0 bindings). No production code path uses this transaction shape — it's seeder-only safety.

### Idempotency-Probe Residual

RuleSet has NO unique (TenantId, Name) index at database level. Idempotency probes by (TenantId, Name) as a convention key, not enforced by DB.

**Accepted:** Seeders run serially at single startup (no concurrent double-seed). Dev-only, gated OFF, no migration (YAGNI). Commented at the probe: `// Convention-based; no unique constraint. Safe: single-instance startup only.`

**Residual risk:** Multi-instance concurrent seed (seeder runs twice in parallel). Not a dev scenario. If we ever move to hot-reload or multi-instance startup, this becomes a blocker (need to add unique index). Documented, not escalated — same as prior seeders' transaction-rollback path untested.

### Drift-Guard Anchor (Continuing P7c Discipline)

Added Rules.Infrastructure assembly anchor to SeederMarkerContractTests. The marker guard now scans:
- Identity.Infrastructure
- Organization.Infrastructure
- Workflow.Infrastructure
- Forms.Infrastructure
- BusinessProcess.Infrastructure
- IntegrationEngine.Infrastructure (P7c)
- Rules.Infrastructure (NEW)

Note: Rules.Infrastructure also registers RulesEngineCacheSubscriber (Redis IHostedService). It ends "Subscriber" not "Seeder" → the guard predicate skips it. Verified: filter `name.EndsWith("Seeder")` excludes it. The guard is honest.

## What We Tried

1. Single SaveChanges for all three entities (RuleSet, Rules, DecisionTable).
   - Failed: DecisionTable.RuleSetId was 0 → violated FK intent (not actually FK, but semantically wrong).
   - Lesson: Don't assume; check the entity config. DecisionTable is NOT in a nav.

2. Skip transaction wrapping (assumes happy path).
   - Avoided: P6 taught us partial failure leaves orphans. Two-phase with transaction costs ~0 overhead; benefit is crash-safety. Applied proactively.

## Root Cause Analysis

Why is idempotency harder here than P4/P5?

- P4 Workflow: WorkflowDefinition has unique (TenantId, DefinitionKey). Probe by DefinitionKey, DB-enforced.
- P5 Forms: FormTemplate has unique (TenantId, Code). Probe by Code, DB-enforced.
- P7a Rules: RuleSet has NO natural unique on name. Index is (TenantId, Status) — a composite that includes status (not unique by name). Convention-based probe is a **design gap**, not a code bug.

Why didn't we add a unique index? Because RuleSet.Activate() changes Status from Draft → Active → Live. A unique (TenantId, Name) can't simultaneously enforce draft-uniqueness and allow multiple activated versions of the same logical rule (multi-version is a real scenario for rules engines). The module's design trades name-uniqueness for version flexibility. Seeder doesn't activate, so the problem is dormant. But it's honest to name it: if the module ever implements version lifecycle, the probe needs rework.

## Lessons Learned

1. **Verify the nav config before relying on EF fixup**: Single-save vs two-phase isn't obvious from entity shape. RuleSet.Rules IS a mapped nav (HasMany) → single-save works. DecisionTable.RuleSetId IS a column only → two-phase needed. The config file (RuleSetConfiguration.cs, DecisionTableConfiguration.cs) is the oracle, not intuition.

2. **Apply prior slice lessons proactively**: P6 taught transaction-wrap; P7c reinforced it with a review finding. Applying it upfront in P7a kept the review clean (0 Crit/High, vs P7c's APPROVE-WITH-NITS). Lessons should flow forward, not be re-discovered.

3. **Honest residuals are better than risky fixes**: Idempotency-probe residual (convention-based, not DB-enforced) is acceptable ONLY because seeder is dev-only, gated, serial startup. Documenting why it's safe is more valuable than over-engineering a unique index that conflicts with the module's version design.

4. **Drift-guard discipline compounds**: Each seeder adds its module's assembly anchor. Continuing this discipline (P7c started it, P7a continued it) keeps the marker guard preventive, not reactive. Small consistent rule beats sporadic audits.

## Next Steps

- **P7b Reporting = LAST item**: ReportDefinition.TenantId = string slug (not Guid) — verify how handlers read it before seeding. QueryCatalogEntry.Code is globally unique → seed ONE global, not per-tenant loop. Widget/query SQL is raw Dapper unvalidated → use hardcoded SELECT. DashboardLayout.OwnerId = Guid.Empty. ReportDefinition unique (Name, TenantId).
- **WF-DEMO-SEED-SMOKE extended**: Reset-all marker convergence now covers P7a (5/5 seeders).
- **SEED-TXN-WRAP-P4-P5 still open**: P4/P5 use raw multi-phase; P6/P7c/P7a wrap in txn. Consider backfill in P4/P5 if we ever hotfix transaction-rollback.

---

**Unresolved questions:** None — all trade-offs documented with risk acceptance.
