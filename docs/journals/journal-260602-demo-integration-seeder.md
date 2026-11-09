# P7c Demo Integration Seeder — Last Slice, Drift Guard Gap Fixed

**Date**: 2026-06-02 (post-ship)
**Severity**: Low
**Component**: Integration.Infrastructure/Seeding, SeederMarker audit
**Status**: Resolved

## What Happened

Completed P7 partial scope: "Integration first (7c)" — the ascending-quirk sequencing choice that put demo seeding in order of architectural simplicity rather than backlog sequence. Built DemoIntegrationSeeder as module-resident (self-contained, no cross-module FK unlike P6's host-resident BusinessProcess seeder). Shipped v2.137p140 (`01a7bf01` feat + `594f9ab9` docs), PUSHED origin + bitbucket, ahead=0.

## Technical Details

**DemoIntegrationSeeder** (Integration.Infrastructure/Seeding):
- 1 Active Partner + 1 Draft Contract per tenant (BoTaiChinh, BoNoiVu)
- Two-phase save (Partner → Contract) wrapped BeginTransactionAsync/CommitAsync — P6 M1 lesson applied proactively
- Idempotent by (TenantId, Code="demo-partner") via filtered unique index [IsDeleted]=0
- Marker `[DemoIntegrationSeeder] All tenants complete`
- Gated `Integration:DemoIntegrationSeed` OFF
- NO MessageLog (runtime entity, not seeded)

**Drift Guard Discovery**: SeederMarkerContractTests scanned 7 assemblies but did NOT include Workflow/Forms/Integration.Infrastructure — P4/P5/P7 module seeders were invisible while P6 host seeder was covered via E2ETestDataSeeder. Fixed by adding three module-seeder assembly anchors to guard scan. Guard now catches all markers across module + host boundary.

## Build & Verification

- Build slnx 0E/0W
- DemoIntegrationSeederTests 3/3 real-SQL:
  - T1: Active+Draft+FK+diacritic
  - T2: idempotent re-run same row
  - T3: cross-tenant isolation (same Code, distinct TenantId)
- SeederMarkerContractTests 2/2 (now scanning 3 module assemblies)
- Startup smoke 1/1 (StartAsync completes)
- Integration.Tests full suite 7/7 (no regression to ContractEndpointsTests)
- Code-review: APPROVE-WITH-NITS, 0 Crit/High

## Root Cause Analysis

Drift guard architectural flaw: marker contract scanned only "known" assemblies (7 hardcoded) at test-author time. P4/P5 added Workflow/Forms.Infrastructure seeders; P7 added Integration.Infrastructure seeder — none updated the guard. Silent coverage gap remained until empirical audit during this slice (INVARIANT 260522-A = audit ground-truth before impl).

## Lessons Learned

1. **Audit-at-slice-start pays off**: Empirical check of drift-guard coverage (read which assemblies guard actually scans) caught a latent gap introduced in P4/P5 and now visible in P7. No new test needed — just add the anchors.

2. **Proactive lesson propagation**: Applying P6 M1 txn-wrap finding (two-phase save needs explicit commit) BEFORE code-review meant 0 findings on the pattern itself. Lesson embedded → less future friction.

3. **Honest naming of untested paths**: Transaction rollback atomicity is correct by construction but has no fault-injection test (reviewer recommended NOT refactoring demo-grade code). Documented instead of implying coverage.

4. **Module seeder = self-contained**: Unlike P6 (host-resident, cross-module FK to Workflow/Forms), P7 Integration seeder is module-internal (Partner/Contract only). Architectural simplicity = fewer visibility gaps.

## Accepted Gaps (Intentional)

- **Transaction rollback untested**: Atomicity correct by construction; fault-injection refactoring overkill for demo code.
- **Soft-delete not re-seeded**: Root-keyed probe sees tombstones; idempotent skip is intended; soft-deleted Partner stays deleted until reset-all.
- **PartnerCreatedEvent no handler**: Internal event, no outbox. Doc-commented as future capability.

## Carry-Forward

**P7 Remainder** (7a Rules + 7b Reporting) — both quirky:
- **7a Rules**: RuleSet has NO unique (TenantId, Name) index → probe by Name; chain RuleSet→Rule/DecisionTable
- **7b Reporting**: ReportDefinition.TenantId = string slug (not Guid); QueryCatalogEntry.Code globally unique (ONE global, not per-tenant loop); widget/query SQL raw Dapper (use safe SELECT); DashboardLayout.OwnerId needs Guid.Empty

**Outstanding**:
- WF-DEMO-SEED-SMOKE extended for P7c (4-marker convergence on reset-all)
- SEED-TXN-WRAP-P4-P5 (propagate txn wrap to P4/P5 — both left unresolved in journal-260602-p4 & p5)

## Meta-Reflection

P7 is **LAST reseed phase but PARTIAL** — Integration seeded, Rules+Reporting pending. Drift guard fix was not originally planned but emerged from mandatory audit (260522-A). Discovery of uncovered seeders in P4/P5 indicates prior audits missed them. Guard is now correct; future seeders MUST update both anchor and behavior before claiming completeness.
