# MTS-3-FU3 — Smoke Test Failure Root Cause: SoftDelete Interceptor Blocks Cascade FK

**Date**: 2026-05-13 14:45–15:17 ICT
**Severity**: High (production bug + test blocker)
**Component**: Infrastructure — `E2ETestDataSeeder.Menus.cs`, `SoftDeleteInterceptor`, `MenuSeederOrphanPurgeSmokeTests`
**Status**: Resolved

## What Happened

`MenuSeederOrphanPurgeSmokeTests.SeedMenus_PurgesOrphansInSeededFamily_KeepsAdminCreatedMenus` had been failing locally since MTS-3-FU1 made the smoke suite runnable. Test expectations: after calling `PurgeOrphanMenusAsync` on seeded menu families, all orphan `MenuRolePermission` rows should vanish. Reality: 1–2 orphan permission rows (with `PermissionCode="TEST.ORPHAN.VIEW"`) persisted, causing `Assert.False(AnyAsync(…))` to fail.

**Backlog hypotheses:**
- (a) Schema drift — FK `FK_MenuRolePermissions_AppMenus_MenuId` created without ON DELETE CASCADE on live SQL Server
- (b) Shared-DB state pollution — `SmokeTestCollection` reuses a single DB and orphan rows accumulate across test runs

**Both refuted by empirical triage.** Real cause = (c) `SoftDeleteInterceptor` converts `EntityState.Deleted → Modified` for all `ISoftDeletable` rows. `AppMenu : Entity : ISoftDeletable`, so `db.AppMenus.RemoveRange(layer) + SaveChangesAsync()` emitted `UPDATE [identity].[AppMenus] SET [IsDeleted]=1 …` instead of a SQL DELETE. DB-engine's `ON DELETE CASCADE` never fired → orphan `MenuRolePermission` rows survived.

**This is a production bug**, not a test artifact. Over many boots where seeded menus are removed from `BuildMenuList()`, orphan permission rows accumulate monotonically.

## The Brutal Truth

The backlog enumerated two likely causes, and we trusted them as a constraint set instead of hypothesis candidates. Empirical drill (running the test with a debugger, reading the migration FK definition, tracing the interceptor logic) proved both wrong in 15 minutes. The guilt: we wasted 2–3 sessions crediting "CI-only" and deferring triage.

Deeper guilt: `MenuSeederPurgeLogicTests` has 10/10 tests passing in isolation because they test only the pure ordering logic (`OrderOrphanMenusLeafFirst`), never the EF + interceptor stack. The smoke test was the first real verification path. This is a pattern: **unit tests that mock the infrastructure (DbContext, interceptors) create a false sense of coverage for code that critically depends on the real interceptor stack.** The coverage gap only surfaces in integration/smoke tests.

Also: without MTS-3-FU1 unblocking the smoke suite locally, this production bug would have remained hidden in `main`. The compounding insight: **test-infrastructure fixes (unblocking runners) sometimes discover production bugs that were failing silently in CI all along.**

## Technical Details

**The cascade FK problem:**
1. Migration `20260506031659_InitialCreateFoundationV1.cs:506-513` correctly declares:
   ```
   onDelete: ReferentialAction.Cascade
   ```
   No schema drift; FK definition is correct.

2. `PurgeOrphanMenusAsync` calls:
   ```csharp
   db.AppMenus.RemoveRange(layer);
   await db.SaveChangesAsync(ct);  // ← SoftDeleteInterceptor fires here
   ```

3. `SoftDeleteInterceptor.SavingChanges(…)` intercepts the SaveChanges event and converts:
   ```
   EntityState.Deleted → EntityState.Modified
   INSERT [identity].[AppMenus] SET [IsDeleted]=1
   ```
   instead of:
   ```
   DELETE FROM [identity].[AppMenus] WHERE Id=…
   ```

4. DB-engine's ON DELETE CASCADE trigger on `FK_MenuRolePermissions_AppMenus_MenuId` expects a DELETE statement; UPDATE does not trigger it.

5. Result: `MenuRolePermission { PermissionCode="TEST.ORPHAN.VIEW" }` rows remain, query filter `!IsDeleted` hides the menu (so it's invisible in normal queries), but permission rows have no `IsDeleted` column and stay visible.

## The Fix

**1. `src/host/AqtCoreFW.Api/E2ETestDataSeeder.Menus.cs::PurgeOrphanMenusAsync`**
```csharp
var affected = await db.AppMenus
    .IgnoreQueryFilters()  // upstream query now skips soft-delete filter
    .Where(m => ids.Contains(m.Id))
    .ExecuteDeleteAsync(ct);  // real DELETE, bypasses SaveChanges interceptor
```

`ExecuteDeleteAsync` is an EF Core 9 escape hatch that bypasses all SaveChanges interceptors (SoftDelete, Auditable, Outbox) by design. It issues a direct SQL DELETE → DB CASCADE removes dependents in the same statement. `IgnoreQueryFilters()` ensures pre-fix soft-deleted accumulation also gets cleaned up.

**2. `tests/integration/AqtCoreFW.Startup.SmokeTests/MenuSeederOrphanPurgeSmokeTests.cs::RemoveTestRowsAsync`**
Same pitfall in test cleanup (would soft-delete `AdminCreatedMenuId` across runs). Swapped to `ExecuteDeleteAsync`.

**3. `docs/system-architecture.md`**
Added to "Known EF Core Concurrency Patterns": note on `ExecuteDeleteAsync` as escape hatch for hard-delete intent when `ISoftDeletable` semantics conflict.

**4. `backlogs/backlog.md`**
MTS-3-FU3 marked DONE.

## What We Tried

- **Approach A (foreign-key scope)**: Assume FK missing CASCADE; add explicit DELETE for dependents before AppMenu delete. Rejected after schema inspection proved FK correct.
- **Approach B (test state isolation)**: Assume DB pollution; add test isolation via separate connections/transactions. Rejected after Testcontainers inspection proved ephemeral DB per run.
- **Approach C (interceptor bypass, as implemented)**: Use `ExecuteDeleteAsync` to bypass soft-delete interception for seeder cleanup. Accepted. Minimal; fixes production bug; unblocks test.

## Root Cause Analysis

`ISoftDeletable` interceptor assumes all entity deletions should be logical (UPDATE IsDeleted=1). Seeders and maintenance code often need hard deletes (remove orphaned dependent rows, clean up test data). The interceptor has no visibility into intent — it converts all deletes uniformly.

Secondary: backlog hypotheses (a) and (b) were plausible but untested. When a backlog row lists "likely causes," it's a starting point for triage, not a constraint. Trusting the enum without empirical drill delayed resolution by 2–3 sessions.

## Lessons Learned

- **Infrastructure interception catches all code paths equally.** Code that calls `RemoveRange + SaveChanges` expecting a hard delete will get soft-deleted instead. `ExecuteDeleteAsync` is the only bypass; document it where soft-delete entities are created. Future `ISoftDeletable` entities with hard-delete use cases (seeders, maintenance) will hit this trap — add a comment block in the entity or interceptor pointing to the escape hatch.

- **Unit tests that mock the interceptor stack are theater.** `MenuSeederPurgeLogicTests` passing 10/10 felt like coverage. It wasn't. The ordering logic is correct, but the entire EF+Interceptor integration was untested. Lesson: integration/smoke tests are the actual verification for code that depends on the SaveChanges pipeline. Unit tests of pure logic are valuable; unit tests of "our code calls RemoveRange" are false confidence.

- **Backlog hypotheses are candidates, not constraints.** When triage lists two "likely causes," test both before defaulting to one. In 15 minutes of empirical drill (read source, inspect schema, run with debugger), both were refuted. Saves future teams from 2-session wild goose chases.

- **Test-infra unblocks production-bug discovery.** MTS-3-FU1 simply extracted Vault stubs. That unblocking enabled MTS-3-FU3 to run and expose a production bug that was failing silently on CI. Production bugs that can't be reproduced locally are invisible to code review and become latent debt. Prioritize test-runner unblocking.

## Test Evidence

- **Before**: MenuSeederOrphanPurgeSmokeTests 0/1 FAIL (`MenuRolePermission` orphans survived)
- **After**: MenuSeederOrphanPurgeSmokeTests 1/1 PASS (17s)
- **Full Smoke suite**: 4/5 → 5/5 PASS
- **MenuSeederPurgeLogicTests**: 10/10 PASS (pure logic unchanged)
- **Identity.Integration.Tests**: 198/198 PASS (no regression)
- **Build**: 0E
- **Code review**: APPROVE 9.2/10
  - M-1 (pre-commit): RemoveTestRowsAsync comment clarity (folded)
  - M-2 (deferred): docs/system-architecture.md wording nit
  - L-1/L-2 (informational): `ExecuteDeleteAsync` behavior in EF Core 9, `IgnoreQueryFilters()` scope

## Next Steps

| Item | Owner | When |
|---|---|---|
| **M-2 (deferred)** — docs/system-architecture.md ExecuteDeleteAsync paragraph grammar pass | Backlog | Next sprint |
| **Consider arch guard** — New rule: `ISoftDeletable` entity seeders must not use `Remove+SaveChanges` for hard-delete intent (only `ExecuteDeleteAsync`). Speculative, likely over-engineering at 1 occurrence. Backlog if 2nd seeder hard-delete found. | Backlog | Later |

## Commit

- **Branch**: main
- **Commit**: `e8a05860`
- **Files**:
  - MOD: `src/host/AqtCoreFW.Api/E2ETestDataSeeder.Menus.cs` (ExecuteDeleteAsync + IgnoreQueryFilters)
  - MOD: `tests/integration/AqtCoreFW.Startup.SmokeTests/MenuSeederOrphanPurgeSmokeTests.cs` (RemoveTestRowsAsync → ExecuteDeleteAsync)
  - MOD: `docs/system-architecture.md` (Known EF Core Concurrency Patterns item added)
  - MOD: `backlogs/backlog.md` (MTS-3-FU3 → DONE)
- **Lines changed**: +14 ExecuteDeleteAsync calls, +5 docs, ~39 LOC net
