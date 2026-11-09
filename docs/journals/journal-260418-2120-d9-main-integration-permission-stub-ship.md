# D9: Main Integration Permission Stub — Ship & Adjacent Failures Mapped

**Date:** 2026-04-18 21:20  
**Severity:** Medium  
**Component:** AqtCoreFW.Tests.Integration (main project) + D9-FU (6 adjacent fails)  
**Status:** Resolved (D9), Documented (D9-FU follows-up)  

## What Happened

Shipped D9 stub for `IEffectivePermissionService` in the main integration project (`src/tests/AqtCoreFW.Tests.Integration`), distinct from the 4 module-level projects that PR #38 already addressed. Branch `feat/d9-main-integration-permission-stub` (2 commits, PR #40) achieved 14/14 targeted tests green. During full-run validation, discovered 6 adjacent failures (role auth + system params endpoints), documented as D9-FU follow-up. All D9 scope closed.

## The Brutal Truth

The frustrating part: my first approach (DB query against real `RolePermissions` table) was code-reviewed clean and architecturally sound, but **ran empirically against the runtime environment's hidden constraint: E2ETestDataSeeder gates on `env.IsDevelopment()`**. The test environment uses `"Testing"` mode, so seeder early-returns, permissions table stayed empty, and every permission check returned 403 anyway. 

Burning 1 hour on a "correct" approach that failed silently, then discovering via code-read that seeder gating was the blocker, *then* pivoting to `PermCodes.All` (which just needed the source-code constant, zero DB state) felt like punching through a wall of assumptions. The advisor can't know deployment/test environment gating from architecture alone — you have to fail empirically to find it.

Even more frustrating: the full test run surfaced 6 unrelated failures. Spent another 30 minutes categorizing them as orthogonal (role-based auth, 401 handlers, tenant resolution) before confirming they weren't D9 regressions. Could have saved time with a scoped run from the start (`--filter "Workflow|Cases"`), but the discovery of full scope required running unfiltered.

## Technical Details

**Stub implementation:**
- New `TestEffectivePermissionService.cs` (~60 LOC, internal class in test project)
- Constructor accepts `IEnumerable<string> permissionCodes`
- Returns `IsPermittedAsync(code) → HashSet.Contains(code)` for all 122 codes from `PermCodes.All`
- Registration in `ApiFactory.ConfigureTestServices()`: 8-line addition, uses factory closure to pass 122 codes once at boot

**Failed approach (lesson):**
```csharp
// Attempted: query live RolePermissions for Admin+SystemAdmin roles
var perms = await db.RolePermissions
  .Where(rp => new[] { "Admin", "SystemAdmin" }.Contains(rp.Role.Code))
  .Select(rp => rp.Permission.Code)
  .ToListAsync();
// Result: empty set, because E2ETestDataSeeder.Seed() never runs (env != Development)
```

**Root cause flow:**
1. `ApiFactory.UseEnvironment("Testing")` → environment = "Testing"
2. `E2ETestDataSeeder.Seed()` checks `if (!env.IsDevelopment())` → early return
3. Roles, Permissions, RolePermissions tables all empty
4. All 403 responses persist, D9 targets still fail

**Final approach:**
```csharp
// Use compile-time constant, zero runtime DB dependency
var stubService = new TestEffectivePermissionService(PermCodes.All);
// PermCodes.All = static HashSet<string> with 122 codes, read from source
```

**Test results:**
- Targeted run (`--filter "Workflow|Cases"`): **14/14 pass, 1m1s** ✓ All D9 scope
- Full run unfiltered: **96/102 pass, 6m1s**
  - **D9 targets:** 5/5 green ✓
  - **Adjacent failures (D9-FU):**
    - `SystemParamsApiTests.GetSystemParams_AsAdmin_Returns200` → 401 (auth handler)
    - `SystemParamsApiTests.GetSystemParams_Returns_NonEmptyList` → 401
    - `UsersApiTests.ListUsers_AsAdmin_Returns200` → 403 (role-based [Authorize(Roles="Admin")])
    - `IdentityAdminApiTests.GetPendingAccessReviews_AsAdmin_Returns200` → 403 (similar)
    - `OrgUnitsApiTests.GetOrgTree_AsViewer_Returns200` → 500 (stale "open" comment in seeder)
    - `CasesApiTests.CreateCase_MissingTenantId_Returns422` → 422 but wrong error detail (tenant resolution orthogonal)

All 6 are orthogonal to D9 (IEffectivePermissionService scope). Documented in backlog as D9-FU.

## What We Tried

1. **Advisor pre-check:** Proposed real `HashSet<string>` from DB query. Advisor rejected `AlwaysTrueSet` (enumeration semantics risk), approved HashSet from DB (more maintainable than hardcoded). Proceeded.

2. **First implementation:** Inject `DbContext` into stub, query `RolePermissions` for Admin+SystemAdmin roles. Wrote ~40 LOC, integrated into `ApiFactory`, ran `--filter "Workflow|Cases"`. Result: **5/5 still fail, 403 on every endpoint.**

3. **Root cause investigation:** Code-read `E2ETestDataSeeder.Seed()` → found `if (!env.IsDevelopment()) return` → "oh". Realized seeder never ran in test environment, permissions table empty, DB query approach fundamentally broken for this project structure.

4. **Pivot:** Replace DB query with `PermCodes.All` (compile-time constant, zero DB seeding). Rewrote stub (~20 LOC simpler), re-integrated, ran `--filter "Workflow|Cases"`. Result: **5/5 pass, 1m1s.** ✓

5. **Validation:** Full unfiltered run to check for regressions. Result: 96/102, 6 adjacent failures discovered. Categorized as D9-FU (role auth + system params + tenant resolution, orthogonal scope).

## Root Cause Analysis

**Primary:** Environment gating in seeder eliminated DB-based approach. The scout phase identified this project uses a *central* `ApiFactory.ConfigureTestServices()` (unlike module-level projects), so I should have also verified *what infrastructure the central factory actually sets up*. The "Testing" environment mode was in the fixture code but not explicitly called out as a seeder-blocker. Lesson: when centralizing setup, trace the full environment + seeder chain before committing to a DB-dependent stub approach.

**Secondary:** Full test run discovered 6 adjacent failures that weren't on the D9 radar. These are real bugs (401 auth handler, role-based policies, tenant resolution), but they're not caused by D9 changes and weren't in scope. However, they block future passes. Documenting as D9-FU ensures they don't get lost.

**Why the pivot worked:** `PermCodes.All` is *generated* from source at compile-time and reflects the actual codebase intent (all 122 codes that the system defines). No seeder, no DB, no environment assumption. It's simpler, faster (HashSet.Contains is O(1)), and moves the permission source-of-truth to the actual code instead of a data dependency.

## Lessons Learned

1. **Advisor pre-check saves major wrong turns, but can't predict runtime environment constraints.** Advisor can rule out patterns (AlwaysTrueSet), but hidden seeder gating requires empirical test. Worth failing fast (1 full test run = 1m) to discover it early rather than iterating on assumptions.

2. **Central factory != magic.** This project's centralized setup is cleaner than per-test-class helpers (PR #38), but you still need to verify *what* the factory actually initializes (environment, seeder gating, DB state). Don't assume "central" means "complete."

3. **Pivot when the approach fails empirically.** First DB-query attempt was architecturally defensible but factually wrong. Rather than trying to "fix" the seeder gating (which is by design), the better move was to eliminate the DB dependency. `PermCodes.All` is actually *cleaner* — one constant, zero assumptions about runtime setup.

4. **Scoped vs unfiltered test runs serve different purposes.** `--filter "Workflow|Cases"` validates the 5 D9 targets in 1m. Unfiltered run discovers adjacent failures and confirms no regressions. Worth doing both: scope first (fast feedback), unfiltered second (completeness check).

5. **6 adjacent failures = 1-2d of follow-up work.** They're orthogonal to D9 but real bugs. Documenting them in backlog as D9-FU ensures they get prioritized next, not lost in noise.

## Next Steps

**D9 closed:** 14/14 targeted tests pass, stub integrated, PR #40 ready.

**D9-FU (6 adjacent failures, ~1-2d):**
- `SystemParamsApiTests` 401 failures → check auth handler registration
- `UsersApiTests.ListUsers_AsAdmin_Returns200` → role-based [Authorize(Roles)] policy
- `IdentityAdminApiTests` → similar role policy
- `OrgUnitsApiTests.GetOrgTree_AsViewer_Returns200` → stale seeder comment
- `CasesApiTests` → tenant resolution logic

**After D9-FU:** D8 (orval v7→v8 mutator upgrade, ~1-2h) or user's priority.

---

**Status:** DONE | **Summary:** D9 shipped (14/14 pass, compile-time PermCodes.All stub); pivoted from DB query after empirical test failure; mapped 6 adjacent failures as D9-FU.
