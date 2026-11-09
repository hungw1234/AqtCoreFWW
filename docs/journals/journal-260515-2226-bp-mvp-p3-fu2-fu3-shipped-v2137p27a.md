# BP MVP P3-FU2 + P3-FU3 Unit Tests Shipped

**Date**: 2026-05-15 22:26 ICT
**Severity**: Low (test-only, no schema/domain logic change)
**Component**: BusinessProcess module (tests)
**Status**: Resolved

## What Happened

P3-FU2 + P3-FU3 test harness shipped: 2 new test csproj projects, 4 new test files, 1 modified `.slnx`. 19 + 2 test facts covering domain aggregate invariants and repository write-reject patterns. All baseline suites (Architecture 97/97, Forms.Domain 130/130, Forms.Application 155/155) held. Build 0E/0W. Code review 9.4/10 post-inline fold-in to 9.5+. 3 commits on origin/main; no local divergence.

## Files Changed

**New projects:**
- `tests/unit/AqtCoreFW.BusinessProcess.Domain.Tests/` (3 files, 19 facts)
  - `BusinessProcessDefinitionFactoryTests.cs`: 10 facts (GOV_BP_010 XOR intake pairing, whitespace guards, workflowDefinitionId>0)
  - `BusinessProcessDefinitionAggregateTests.cs`: 7 facts (GOV_BP_002 cross-tenant reject, idempotent Activate/Deactivate, UpdateMetadata null-coalesce)
  - `BusinessProcessChildFormTests.cs`: 2 facts (workflowStateId>0, formTemplateVersion>=1 range guards)
- `tests/unit/AqtCoreFW.BusinessProcess.Infrastructure.Tests/` (1 file, 2 facts)
  - `BusinessProcessRepositoryWriteRejectTests.cs`: Pattern-A R13 cross-tenant reject BEFORE EF tracking (asserted via `db.Local.Should().BeEmpty()`)

**Modified:**
- `src/AqtCoreFW.slnx`: Added 2 project references (lines 154-155, alphabetically after Forms.*.Tests block)

## Tests

P3-FU2: 19/19 PASS (10 factory + 7 aggregate + 2 child entity)
P3-FU3: 2/2 PASS (repo write-reject pre-tracking)

All baseline suites revalidated:
- Architecture 97/97 PASS
- Forms.Domain 130/130 PASS
- Forms.Application 155/155 PASS

## Lessons & Decisions

1. **YAGNI-Honest InMemory Testing** — FU3 dropped "positive save control test" because `InMemory` provider doesn't honor `IsRowVersion()` concurrency tokens. Positive test would validate harness, not repo. Instead, kept 2 reject tests that fire BEFORE any DB interaction (`Local.Should().BeEmpty()` assertion). Reviewer: "one of the best YAGNI-honest test classes this week." Codified: when InMemory + non-InMemory-supported features collide, assert pre-DB-interaction timing over harness-only validation.

2. **Fold-in Pattern Holds Reviewer Score** — Reviewer nits L-1 (AddTransitionForm_MatchingTenant_Appends symmetric positive control) + L-2 (DomainEvents.ContainSingle type-assert) folded inline post-review. Cost: ~5 min. Benefit: avoids follow-up commit, maintains reviewer-bar trend (P0 9.5, P1-FU 9.3, P1 8.5→9.5+, P2-FU3 9.5, P3 9.5, P3-FU 9.4+). Effective score 9.5+.

3. **Marathon Fatigue Rule-C Override Calibration** — 12th ship, 4th fatigue override (P1×2, P2×2, P3×3, P3-FU×4). Reviewer score held >9 despite fatigue token burn. Risk compression detected; next session should evaluate harder before ship #13. Thread: [[dev-fatigue-tracking-260515-memo]].

4. **/aqt-next Cross-Check Catch** — User typed `/cook P4-FU` but recommendation table wrongly listed P4-FU (already shipped 2026-05-14). Cross-check against project memory before scope opening caught this. Avoided ~1h of redundant work. Pattern: always validate FU label against latest memory snapshot.

5. **InMemory + ModuleDbContext Compat** — `ModuleDbContext.OnConfiguring()` guards non-relational-db code paths via `IsRelationalProvider()` check. InMemory provider safely triggers no-op branch (no UseSqlServer/SplitQuery). BP repo tests confirmed this path; no production code edits needed.

## Carry-Over

- **P3-FU1** (~3-4h): Full TenantIsolation enforcement test with fixture; deferred to next session
- **P3-FU2.5** (LOW): Parametrised child range guard theory (~15 LOC)
- **P3-FU2.6** (LOW): ArchUnit rule banning direct `DbSet.Add<BusinessProcessDefinition>()` bypass
- **BP MVP P4** (REST API + 3 perms + outbox + Run lifecycle, 2.5d BE): Ready next session; no local blockers

## Cross-References

Tail of [[bp-mvp-p3-shipped-260515]]. Code-review report: `code-reviewer-260515-2204-bp-mvp-p3-fu2-fu3-tests.md`. Changelog: `docs/project-changelog.md` v2.137p27a.
