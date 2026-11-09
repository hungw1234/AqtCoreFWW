# P7b Demo Reporting Seeder — Split-Personality Module & Tenant-Scope Asymmetry

**Date**: 2026-06-02 (post-ship)
**Severity**: Low
**Component**: Reporting.Infrastructure/Seeding
**Status**: Resolved

## What Happened

Shipped P7b = `DemoReportingSeeder` + `DemoReportingSeederOptions`, third and FINAL niche seeder in P7 reseed plan. Closes Phase 7 — all three niche seeders (7c Integration + 7a Rules + 7b Reporting) now shipped. Module-resident, self-contained, mirrors P7c/P7a/P4/P5. Per tenant (BTC + BNV): 1 ReportDefinition + 1 ReportWidget + 1 DashboardLayout (grid JSON references assigned widget.Id) + 1 GLOBAL QueryCatalogEntry (seeded once outside per-tenant loop). Left in Draft state (activation is a manual operator choice, not a seeding concern). Gated `Reporting:DemoReportingSeed` OFF. Build clean 0E/0W (first build had 8 CS8123 tuple-name warnings in tests → fixed to 0W). Code-review APPROVE 0 Crit/High (cleaner than P7c's APPROVE-WITH-NITS). Tests 3/3 real-SQL + startup smoke + full Reporting.Integration 7/7 no regression. Commits `4c7911ba` feat + `d4bbf592` docs, PUSHED origin + bitbucket, ahead=0, v2.137p142.

## The Brutal Truth

This module is **split-personality**: three different tenant-scope shapes in ONE seeder.

- ReportDefinition.TenantId = `string?` slug (NOT Guid ITenantScoped) — query handler casts tenantContext to string, seed via `profile.TenantId.ToString()`, DB unique (Name, TenantId) WHERE TenantId IS NOT NULL.
- ReportWidget + DashboardLayout = Guid ITenantScoped — EF global filter, bypassed via SystemSeederTenantContext.
- QueryCatalogEntry = NO TenantId field — globally unique Code, seeded once outside the per-tenant loop.

The honest read: Reporting module wasn't designed as a multi-tenant entity model; it bolted tenancy onto ReportDefinition after the fact (string slug, not Guid, not ITenantScoped). This asymmetry is the defining quirk. It meant reading THREE different configs before understanding the save shape — and the seeder had to adapt to three different isolation boundaries.

Empirical audit at slice-start (INVARIANT 260522-A) caught a memory-decay claim that the old ReportDefinitionSeeder was a "TOMBSTONE PR#71". File STILL EXISTS but is now verified 100% comment-only (7 lines, no class, never wired in DI). Memory was right; empirical read confirmed it rather than assumed it.

## Technical Details

### Split-Personality Tenant Scope (The Core Quirk)

**ReportDefinition** (string TenantId):
- Entity config: unique (Name, TenantId) WHERE [TenantId] IS NOT NULL
- Query path: ReportDefinitionRepository.cs:18 compares `tenantContext.TenantId?.ToString()`
- Seeded as: `definition.TenantId = profile.TenantId.ToString()` (byte → string conversion)
- Tenant user sees this row: ✓ (name-keyed query works)
- Isolation: per-tenant name uniqueness; soft-delete filter omitted (no [IsDeleted] column)

**ReportWidget + DashboardLayout** (Guid ITenantScoped):
- Entity config: HasQueryFilter `x => x.TenantId == tenantId` (EF runtime filter)
- Seeded via: SystemSeederTenantContext bypass (IgnoreQueryFilters + explicit set)
- Isolation: EF interceptor skips sp_set_session_context for db_owner startup path (same as P4/P5)
- Both wrapped in phase 1 SaveChanges (FK nav exists Widget→Dashboard implicit via grid JSON)

**QueryCatalogEntry** (NO TenantId, global):
- Unique Code, GLOBAL entity
- Seeded OUTSIDE per-tenant loop: `await seeder.SeedGlobalQueryCatalogAsync(…)`
- Idempotent on Code
- Used by all tenants; one shared canonical catalog

### Two-Phase Txn-Wrapped Save (P6/P7c Lesson Applied Proactively)

Phase 1: ReportDefinition + ReportWidget
- Widget linked to report by id-in-JSON, NOT an FK navigation
- Save both, widget.Id only assigned after phase 1 SaveChanges
- Widget unique key: (Name, TenantId)
- Verified in test: T1 asserts widget.Id populated after phase 1

Phase 2: DashboardLayout
- Grid JSON references widget.Id by value: `"{\"widgetId\": {widget.Id}, …}"`
- DashboardLayout unique key: (Name, OwnerId=Guid.Empty) — **OwnerId IS Guid.Empty for all demo dashboards**
- Naming MUST be tenant-distinct to avoid collision: `"Demo Dashboard — {profile.Name}"`
- Second SaveChanges inserts the layout
- If phase 2 fails (e.g., invalid JSON), entire transaction rolls back; Report + Widget never committed

Wrapped in BeginTransactionAsync/CommitAsync. No production code uses this exact shape — seeder-specific safety.

### Self-Contained Literal SQL (No Table Dependency)

Report template query, widget query, catalog query are each:
```sql
SELECT CAST(0 AS bigint) AS [QueryResult] 
WHERE @TenantId IS NOT NULL
```

- No FROM/JOIN clause (Cases module deleted PR#71 — no backing table)
- All three queries carry @TenantId parameter
- Code-review verified all executors bind @TenantId server-side (SqlClient parameter binding, no string interpolation)
- CrossSchemaSqlScanner can't trip (no FROM/JOIN, single schema, no stored proc)
- Seeded data RENDERS (not just inserts) — verified by running demo dashboard query path

### Idempotency-Probe Mechanics

Per-tenant block:
- Anchor probe: ReportDefinition (Name, TenantId slug)
- DB unique index load-bearing
- Convention: `definitions.FirstOrDefault(x => x.Name == seedName && x.TenantId == tenantIdString)`

Global block (outside loop):
- Anchor probe: QueryCatalogEntry Code
- DB unique index load-bearing
- Convention: `entries.FirstOrDefault(x => x.Code == "demo-query")`

ReportWidget/DashboardLayout idempotency:
- Widgets + Layouts each have their own unique keys
- Probe by Name + TenantId (Widget) and Name + OwnerId (Layout)
- Re-run on same tenant is safe: finds existing widget → reuses widget.Id in phase 2 layout JSON

No partial-state hole (transaction atomicity enforced at DB).

### Drift-Guard Anchor (Continuing P7c/P7a Discipline)

Added Reporting.Infrastructure assembly anchor to SeederMarkerContractTests. Guard now scans:
- Identity.Infrastructure
- Organization.Infrastructure
- Workflow.Infrastructure
- Forms.Infrastructure
- BusinessProcess.Infrastructure
- IntegrationEngine.Infrastructure (P7c)
- Rules.Infrastructure (P7a)
- Reporting.Infrastructure (NEW)

SeederMarkerContractTests 2/2 pass (confirms all 8 modules' seeders are now guarded).

## What We Tried

1. Single SaveChanges for all three entities (Report + Widget + Layout).
   - Failed: Widget.Id was still 0 when phase 2 tried to reference it in layout JSON.
   - Lesson: JSON-embedded references need the ID assigned first; can't be in same SaveChanges.

2. Skip transaction wrapping (assumes happy path).
   - Avoided: Applied lesson from P6/P7c. Two-phase txn costs zero overhead; benefit is rollback safety. Proactively wrapped.

## Root Cause Analysis

Why is audit-at-slice-start important here?

Memory claimed ReportDefinitionSeeder was a TOMBSTONE (PR#71) but file STILL EXISTS. False alarm? No — empirical read verified: file exists, contains 7 lines of comment, zero code, zero DI wiring. The memory was correct; the empirical check confirmed rather than contradicted. This is how audit works: read the thing, don't trust the snapshot.

Why are the three tenant-scope shapes different?

- ReportDefinition pre-dates module's ITenantScoped adoption (string slug = legacy)
- Widget + Layout added later, used new pattern (Guid ITenantScoped)
- QueryCatalogEntry is intentionally global (shared across tenants)

No design flaw — organic evolution. But the seeder has to understand all three. Reading the three config files (ReportDefinitionConfiguration, ReportWidgetConfiguration, DashboardLayoutConfiguration, QueryCatalogEntryConfiguration) was load-bearing.

## Lessons Learned

1. **Empirical audit defends against memory decay both directions**: A "tombstone" claim that looked contradicted by a file-exists probe turned out correct once read verbatim. Probe THEN read. Don't trust the snapshot; don't panic at the first contradiction.

2. **Config is the oracle for save shape**: Entity-shape intuition says "all three should be saved together" but the EF configs tell the real story. Widget.Id is NOT in a navigation → can't use nav-fixup → needs two phases. Read the Fluent API, not the entity properties.

3. **Apply prior-slice lessons proactively**: P6 taught transaction-wrap; P7c reinforced it; P7a applied it upfront; P7b did the same. Result: 3 consecutive 0-Crit/High slices. Lessons should propagate forward, not be re-discovered per-seeder.

4. **Tenant-scope asymmetry is data-model debt**: String-slug TenantId on ReportDefinition is a design debt (not ITenantScoped like the rest). Don't hide it; document it. Future refactors (e.g., Guid TenantId everywhere) will need to touch this seeder and the query handler together. Naming the debt is better than pretending it doesn't exist.

5. **Advisor value = pinpoint the one unproven assumption**: All three SQL executors (report, widget, catalog) bind @TenantId. Code-review traced each executor → confirmed all bind server-side. Without that tracing, we'd ship a seeder that inserts data that doesn't render.

## Accepted Gaps (Intentional)

- **Transaction rollback untested**: Atomicity correct by construction; fault-injection refactoring overkill for demo code.
- **Multi-instance concurrent seed not tested**: Seeders run serially at single startup (no concurrent double-seed). Dev-only, gated OFF. If we ever hot-reload or multi-instance boot, this becomes a blocker (need to lock the anchor probe).
- **Dashboard.OwnerId = Guid.Empty for all demos**: This is intentional (demo dashboards are system-owned, not user-owned). Means all demo dashboards must have tenant-distinct names to avoid (Name, Guid.Empty) collision across tenants.

## Carry-Forward

**Phase 7 CLOSED** — all niche seeders (7a Rules + 7b Reporting + 7c Integration) shipped v2.137p142.

Outstanding follow-ups (lower priority):
- **WF-DEMO-SEED-SMOKE extended**: Now covers P4 + P5 + P6 + P7c + P7a + P7b (manual reset-all smoke test, 6-marker convergence)
- **SEED-TXN-WRAP-P4-P5**: P4 and P5 seeders use raw multi-phase (no txn wrap). P6/P7c/P7a/P7b all wrap. Consider backfill if we hotfix transaction-rollback.
- **ReportDefinition.TenantId string-slug debt**: Future Guid migration will need coordinated changes in seeder + query handler + config.

---

**Unresolved questions:** None — all quirks documented with trade-off rationale.
