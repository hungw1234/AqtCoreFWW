# T5-10 Workflow Version Migration — Red-Team DEEP + Structural Pivot

**Date**: 2026-04-12 21:07 → 2026-04-13 10:00
**Severity**: High (7 new entities, 9 endpoints, 4-eyes compliance)
**Component**: Workflow module (version migration, governance, audit)
**Status**: Resolved
**Commits**: `4959483e` (A+B+C), then D+E+docs (in progress)

## What Shipped

- 2 new entities: `WorkflowVersionMigration` + `WorkflowVersionMigrationItem` (child guard table)
- 9 new endpoints (create, query, approve, cancel, execute, audit, rollback, dry-run, status)
- 119 new tests (31 domain + 36 application + 19 infrastructure + 7 integration + 26 FE)
- 5 new permission codes: CREATE, APPROVE, EXECUTE, AUDIT, MANAGE
- 4-eyes governance: permission split (CREATE ≠ APPROVE) + app-level creator ≠ approver check
- MappingHash tamper detection + `sp_getapplock` advisory lock for concurrency
- Hangfire background workers with explicit TenantId bypass for global query filter
- Testcontainers.MsSql integration tests verifying DB constraints

## The Brutal Truth

Red-team DEEP review found 28 structural issues beyond embedded F01/F05/F13 — **this is why red-team DEEP is mandatory before finalize, not optional.** Plan was sound (6d estimate, 6 endpoints), but lacked governance depth, idempotency guarantees, and tamper evidence.

The worrying bit: initial phase-06 had **zero mention of:**
- Who can create vs approve migrations (regulatory gap)
- How to retry failed item migrations without creating duplicates (idempotency crisis)
- How to prove migrations weren't tampered mid-flight (audit failure)
- How background workers handle `TenantId` when HTTP context vanishes (operational bug)

These aren't edge cases. These are foundational. Lesson: **whiteboard governance + database constraints before coding starts.**

Also frustrating: Chunk E (FE) routed to `/create-version-migration` but modal parent was `/workflows` — dead state if browser back-button hit. Caught by advisor pre-delivery.

## Technical Details

### Structural Pivot: Child Guard Table

**Problem**: Track per-item migration state (pending, in-progress, succeeded, failed, rolled-back) + retry counts + error logs + creation timestamps.

**Wrong approach**: Denormalize into WorkflowVersionMigration as JSON array (no DB enforcement, no indexing, query nightmare).

**Right approach**: `WorkflowVersionMigrationItem` child entity with FK to parent, indexed by (MigrationId, WorkflowInstanceId, CreatedAt).

**What this solved in one design choice**:
- **C03 Idempotency**: If item already succeeded, ExecuteMigration returns early (duplicate create prevented by unique index)
- **H02 Retry Safety**: Failed items stay in DB; retry increments `AttemptCount`, preserves original timestamp
- **C06 Tamper Evidence**: CreatedAt + UpdatedAt + RowVersion form audit trail
- **H05 Rollback Audit**: Item records what was migrated per instance (forensics)

### 4-Eyes Governance

**Permission Model**:
```
- WorkflowVersionMigrationCreate: user can initiate migration
- WorkflowVersionMigrationApprove: user can approve mapping
- WorkflowVersionMigrationExecute: user can start migration
```

**App-level Guard** (PermissionCode.APPROVE enforced + creator ≠ approver):
```csharp
if (cmd.UserId == migration.CreatedByUserId)
    throw new InvalidOperationException("Creator cannot approve own migration");
```

Why: regulatory requirement (4-eyes). SQL alone insufficient (permissions can be mocked in tests).

### MappingHash Tamper Detection

- Compute SHA-256 of mapping JSON at `PendingApproval` state
- Lock hash immutably in DB
- Re-compute at Execute, compare before applying

Catches: JSON was swapped, field reordered, version changed mid-flight.

### Hangfire + Global Query Filter Gotcha

**Problem**: Hangfire background job runs outside HTTP context. `_context.WorkflowInstances.ToListAsync()` returns empty (global query filter for TenantId finds null context).

**Solution**:
```csharp
_context.WorkflowInstances
    .IgnoreQueryFilters()
    .Where(x => x.TenantId == tenantId)
    .ToListAsync();
```

Not pretty, but necessary. This is now memory feedback.

### EF Navigation Collection + Entity State Gotcha

In Chunk B: pre-populating `WorkflowInstanceHistory` collection on new `WorkflowVersionMigration` caused EF to mark parent as Modified instead of Added.

**Fix**: Explicitly set `EntityState.Added` after all navigation collections assigned, before `SaveChanges()`.

```csharp
_context.WorkflowVersionMigrations.Add(migration);
migration.MigrationItems.ForEach(x => x.MigrationId = migration.Id);
_context.Entry(migration).State = EntityState.Added;
```

## What We Tried

1. **Denormalized JSON array** (H02 idempotency) → rejected: no atomicity, no indexing, query pain
2. **String error log field** (C03 tamper evidence) → rejected: unstructured, no audit history
3. **Permission-only governance** (4-eyes) → rejected: tests mock permissions; app-level guard required
4. **`sp_getapplock` timeout 0** (H04 deadlock prevention) → changed to 10s with retry logic

## Root Cause Analysis

**Why DEEP review caught 28 issues:**
- Regular red-team audits regulatory surface (permissions exist, endpoints callable)
- DEEP audit verifies **structural assumptions** (idempotency, tamper-proofing, background job edge cases)
- Plan was written by planner (happy path). Red-team DEEP simulates production nightmare scenarios.

**Why Chunk A initially had `sp_getapplock @rc undeclared`:**
- Copy-pasted from SQL reference docs without checking variable scope
- Advisor caught it during Chunk A review (integration test failed on CREATE)

**Why Chunk E had dead route state:**
- FE agent reused workflow modal parent (/workflows) without verifying create-migration modal should post to different route
- Browser back-button from create-success landed on dead /workflows/create-version-migration path
- Advisor flagged during pre-delivery review

## Lessons Learned

1. **Red-team DEEP is mandatory pre-finalize, not optional.** Regular audit ≠ DEEP. Schedule both.
2. **Child entities solve multiple concerns.** Before adding fields to parent, ask: "Is this per-child-entity state?" If yes, extract child table.
3. **Governance ≠ permissions alone.** Permissions enable flow; app-level guards enforce policy. Both required.
4. **Hangfire + global query filter = explicit TenantId bypass.** This will repeat. Add to dev checklist.
5. **Advisor pre-delivery review catches structural bugs.** Integrate into every chunk delivery, not just crunch gate.
6. **Stale assumptions are dangerous.** "Library not installed" (T5-09) and "permission logic simple" (T5-10) both proven wrong. Verify before building.

## Unresolved Nits (Defer)

- Rollback workflow instances post-migration (T5-11 backlog)
- Migration audit export in CSV/JSON (T5-12 candidate)
- Background job retry strategy (Hangfire exponential backoff vs custom)
- Analytics on migration success rate (T5-14)

## Next Steps

- T5-11: Profile snapshot + notification migration (similar 4-eyes pattern)
- Backlog: Consolidate all permission consts into single `WorkflowPermissions` file
- Memory: Add "Hangfire + global query filter" to feedback + add "Red-team DEEP mandatory" to checklist
- Monitor: Migration execution times on production data (currently tested at <100 instances)
