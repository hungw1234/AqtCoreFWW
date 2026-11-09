# Phase-04a Seeder Regressions (RT-3/4/5) Shipped

**Date**: 2026-05-05 19:42
**Severity**: High
**Component**: Identity + Forms seeders; Phase-04a T1/T2 entity type refactor
**Status**: Resolved
**Branch**: `fix/phase-04a-rt-seeder-regressions` (off main `04cd0346`)
**PR**: #75 — opened, not yet merged
**Commit**: `a01ee6df`

## What Happened

Three P0 seeder regressions emerged post-Phase-04a entity refactor when the system attempted to backfill tenants and seed initial test data. The API host crashed on startup because seeders were reading/writing entity IDs incorrectly against the new long-PK + Guid-PublicId schema.

- **PHASE4A-RT-3**: BtcIdentitySeeder SELECT statement picked internal `Id` (now bigint) instead of `PublicId` (Guid) when crossing module boundaries to OrgUnits.
- **PHASE4A-RT-4**: TenantBackfillSeeder WHERE clause and INSERT statement used `Id` (Guid) instead of `PublicId` (Guid) as the AK target.
- **PHASE4A-RT-5**: FormFieldConfiguration retained `ValueGeneratedNever()` override, breaking EF's multi-add tracking for child entities in transactions.

**Shipping impact**: False-green suite passed because integration tests never invoked seeders — fixture strips `IHostedService`. Code review APPROVE 9/10. All gates green post-fix (Architecture 26/26, Integration 110/110, Forms 7/7, Identity 54/54).

## The Brutal Truth

This is the third false-green pattern in three consecutive releases. The integration test suite is a lie — it doesn't exercise seeders at all because `WebAppFixture.ConfigureTestServices` removes `IHostedService` registrations. We ship false-green regularly and only catch it when the developer manually spins up the API host.

The fix itself is straightforward, but the pattern that enabled the bug — treating static analysis as sufficient — keeps repeating. We know this is a systemic gap (documented in Phase-04e and Phase-04b follow-up memories), yet we don't fix the fixture.

## Technical Details

### PHASE4A-RT-3 — Cross-Module FK Direction

**File**: `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Seeding/BtcIdentitySeeder.Users.cs`

**Error**: 
```
System.InvalidCastException: Unable to cast object of type 'System.Int64' to type 'System.Guid'
```

**Root Cause**: Post-Phase-04a refactor, `OrgUnit` became T2 (long PK + Guid PublicId). `ApplicationUser.PrimaryOrgUnitId: Guid` should target `OrgUnits.PublicId` (AK), not `OrgUnits.Id` (PK). Seeder's Dapper query selected `SELECT Id` (now bigint), breaking the FK contract.

**Fix**: Changed SQL query to `SELECT PublicId AS Id` (materializes Guid as expected). Also converted `OrgUnitRow` from positional record to class form (defensive against future projection shape changes).

### PHASE4A-RT-4 — T2 Entity AK Usage

**File**: `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Seeding/TenantBackfillSeeder.cs`

**Error**:
```
Microsoft.Data.SqlClient.SqlException: Operand type clash: bigint is incompatible with uniqueidentifier (line 76)
```

**Root Cause**: `Tenants` became T2 (long Id PK + Guid PublicId AK). Seeder's WHERE clause tried to match `WHERE t.Id = a.TenantId` where `t.Id` is now bigint but `a.TenantId` is Guid. INSERT statement also tried to populate `Id` explicitly (IDENTITY violation).

**Fix**: Changed WHERE to `WHERE t.PublicId = a.TenantId`. Dropped explicit `Id` from INSERT cols, allowing DB IDENTITY to auto-assign. Seeder remains idempotent via `NOT EXISTS (... WHERE PublicId ...)` guard.

### PHASE4A-RT-5 — EF ValueGeneratedNever Anti-Pattern

**File**: `src/modules/forms/AqtCoreFW.Forms.Infrastructure/Persistence/Configurations/FormFieldConfiguration.cs`

**Error**:
```
System.Data.SqlClient.SqlException: Violation of PRIMARY KEY constraint 'PK_FormFields'
Duplicate Id = 0
```

**Root Cause**: `FormFieldConfiguration` declared `.Property(e => e.Id).ValueGeneratedNever()`, instructing EF that the application will always provide an Id. But `FormField.Create()` never sets Id; it relies on DB IDENTITY. When `AddFormFieldCommandHandler` called `repository.AddAsync(formTemplate)` inside a transaction scope, EF tracked multiple `FormField` entities with Id=0. The `ValueGeneratedNever()` flag told EF "this is an explicit key," so it rejected the duplicate instead of deferring to the DB.

**Fix**: Removed `ValueGeneratedNever()` override. EF now treats Id=0 as "not yet assigned" and allows multiple Added entities. Migration artifact updated (`Annotation("SqlServer:Identity", "1, 1")`), Designer and Snapshot synced.

## What We Tried

1. **Static analysis (integration tests)** — INSUFFICIENT. Fixture strips seeders, so errors never surface.
2. **Code review** — Caught the cross-module FK direction mismatch and EF config issue, but reviewer cannot execute code.
3. **Manual API host startup (pass-1 + pass-2)** — WORKED. Startup clean after fixes applied.

## Root Cause Analysis

Three distinct classes of error:

1. **Schema contract drift** — Phase-04a T1/T2 refactor introduced long PKs and Guid PKs. Dapper raw SQL wasn't updated to reflect cross-module FK targets. Root cause: schema-aware migrations (Designer/Snapshot) don't live-validate against Dapper query shapes — that's manual work.

2. **EF domain assumptions** — `ValueGeneratedNever()` was a cargo-cult misunderstanding. The comment claimed it "marks new child entities as Added (not Modified) during navigation collection detection," but that's not what it does. No caller ever provided an Id. This is purely a misread of EF semantics.

3. **Fixture-level gap** — `WebAppFixture.ConfigureTestServices` disables `IHostedService` to avoid background jobs in unit tests. Good for isolation, bad for startup validation. Seeders are `IHostedService` and only run at host boot, so the integration suite never exercises them.

## Lessons Learned

1. **False-green is a meta-problem**: The test suite reports all-green because it doesn't run the code that breaks. We've now documented this 3 times (Phase-04e, Phase-04b follow-up, now Phase-04a RT). Until we wire up a "startup validation" gate (e.g., a fixture that DOES invoke seeders), we'll keep shipping regressions that only surface during manual dev testing.

2. **Schema versioning in raw SQL**: When Phase-04a refactored entities, no central registry told us "update your cross-module FK targets." Dapper forces developers to track schema contracts manually. Consider: Grep the codebase post-refactor for all `SELECT ... FROM [schema].[Table]` patterns and audit FK targets against the new schema. This should be in the Phase-04a checklist but wasn't.

3. **`ValueGeneratedNever()` is a footgun**: It's almost never correct. Only use it if you genuinely want the application to provide the PK on every insert (e.g., a lookup table with business-assigned codes). EF default `.ValueGeneratedOnAdd()` is right 95% of the time. Comment discipline: if you override it, explain WHY (e.g., "lookup table" or "natural key"), not WHAT it does.

4. **Migration edit-in-place is dev-only-safe**: This PR edited `20260505030102_InitialCreateFoundationV1.cs` post-apply (Phase-06/6C pattern). That's OK here because AqtCoreFW is a template repo with no prod env. Downstream consumers wipe and reseed. But this is a non-obvious move — document it in the ADR or memory.

## Next Steps

1. **Merge PR #75** once code-review approves (currently 9/10 — 2 pre-existing P3 findings noted, not blocking).
2. **Fix fixture for Phase-04a final checklist**: Wire up a "seeder validation" integration test that explicitly invokes `IHostedService` startup, or move seeders out of hosted service pattern.
3. **Add to PHASE4A-SEEDER-AUDIT backlog**: 
   - Sweep all Dapper raw SQL for cross-module FK targets (especially after entity refactors).
   - Verify all `IPublicIdentified` entities have explicit `HasIndex(t => t.PublicId).IsUnique()` in EF configs (currently relies on migration annotations only).
4. **Document schema-change checklist** for future entity refactors: FK target audit, Dapper query audit, EF config review.

---

**Status**: DONE. Shipped 2026-05-05 19:42. PR #75 ready for merge (code review APPROVE). Verification gates all green. False-green pattern documented for action in Phase-04a final. Downstream Phase-07 (FE contract validation) unblocked once PR merges.
