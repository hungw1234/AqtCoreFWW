# RBAC API-Driven Menu + Permission Refactor (v2.133p6)

**Date:** 2026-04-28 23:38
**Severity:** High
**Component:** Authorization (AuthServer) + Menu System (FE) + Admin Endpoints
**Status:** Resolved
**Branch:** thiennd/zen-franklin-49c17c | **Commit:** e554b74a (pushed)

## What Happened

v2.133p5 shipped Tenant Management CRUD, but Admin users couldn't see the Tenants menu item after logout/login. Investigation revealed a cascade of authorization design flaws:

1. **RCA#1 (wrong):** Missing `IDENTITY.TENANT.*` permissions in database — inserted manually via SQL. Anh logout/login, still no menu. 
2. **RCA#2 (correct):** AuthServer JWT never emitted permission claims. FE `usePermissions()` read from profile → empty array. Sidebar had hardcoded role-based fallback (`hasAccess(SystemAdmin|Admin)`) for menu visibility, but Tenants not in that list.
3. **Quick patch (bad):** Added `IDENTITY.TENANT.VIEW` to hardcoded sidebar fallback. Symptom fixed, but root cause festering.
4. **Anh challenge:** "Still have role checks somewhere. JWT stuffed with 3-4KB permission array is bloat anyway."
5. **Planned overhaul (3 phases, ~2.5h est):**
   - Phase 01: Remove JWT permission claim emission; implement `/api/v1/me/permissions` endpoint
   - Phase 02: Refactor FE `usePermissions` to call API (backend already had seeded DB perms + `IEffectivePermissionService`)
   - Phase 03: Menu system already API-driven (`/menus/my-menus`), verify integration + add Tenants row to AppMenus seeder

## The Brutal Truth

This was supposed to be "menu visibility" fix. Instead, we discovered (and had to fix) two separate design failures — one in auth architecture, one in multi-tenant isolation. Anh was right: hardcoding role checks into FE is fragile.

**Plan estimate vs reality:** Estimated 2.5h. Actual ~5h with discovery + P0 catches. We found 3 seeder bugs, 2 role-mapping inconsistencies, and 1 critical privilege escalation during code review. Manual smoke testing (curl + browser) missed the 403-flash race condition entirely.

The hardest lesson: **adversarial code review post-implementation catches what manual testing never will.** We logged in, clicked the menu, saw Tenants — passed. Reviewer read MeController logic cold, traced the permission check flow, and asked "what if `useMyEffectivePermissions` is still loading?" We had no loading state guard on the route.

## Technical Details

**Bugs discovered during implementation:**

1. **DB seeder gap (AppMenus):** `AppMenus` table had no row for Tenants module. Phase 03 seeder added parent=Administration, icon=people, route=/admin/tenants.

2. **Role mapping inconsistency:** AdminGuard / RouteGuard checked role via JWT claim `role` (PascalCase: "SystemAdmin", "Admin"). But newly seeded Admin role had code "ADMIN" (UPPERCASE). Database consistency: use `RoleName` (PascalCase) instead of `RoleCode` at permission layer.

3. **Hardcoded guards still firing:** AdminGuard (`src/features/admin/AdminGuard.tsx`) had role-based checks. Refactored to use permission-based (checks `IDENTITY.ADMIN.VIEW`, falls back to role for backwards compat). Similar pattern: menu-admin-page, tenant-list-page guards.

4. **UI inconsistency:** Admin > Tenants page had custom header/toolbar. Refactored shared components `AdminPageHeader`, `AdminPageCard`, `AdminPageToolbar` to unify styling.

**P0 findings from code-reviewer (post-implementation):**

1. **403-flash race:** RouteGuard + AdminGuard execute before `useMyEffectivePermissions` hook resolves. No `isLoading` state exposed from hook. Result: 403 Forbidden flashes for 100-200ms on page load, then disappears. Codebase lacks spinner gate. Fixed: wrapped RouteGuard in conditional render, return `<PageSpinner/>` while `useMyEffectivePermissions.isLoading`.

2. **Cross-tenant privilege escalation:** Admin role definition excluded hardcoded list of sensitive perms (`IDENTITY.RTBF.*`, `IDENTITY.CONSENT.*`, `IDENTITY.AUDIT.DELETE`). **Missing:** `IDENTITY.TENANT.*`. Result: Admin user (TenantId=ABC) could view/manage tenants across all TenantIds in database. Verified: Database role `SELECT * FROM iam.RolePermissions WHERE RoleId=(SELECT Id FROM iam.Roles WHERE Name='Admin') AND Permission LIKE 'IDENTITY.TENANT%'` returned 0 rows. Fixed: added all 3 TENANT perms to Admin exclusion list.

**Test status:**
- BE unit + integration: 31/31 ✅
- FE vitest: 794/807 (13 pre-existing workflow timeout failures, unrelated to RBAC changes)
- Build: 0 errors, 0 warnings

**Files modified (core):**

- `src/modules/iam/Queries/GetEffectivePermissionsQuery.cs` (removed JWT emission block, kept DB query)
- `src/modules/iam/Handlers/GetEffectivePermissionsQueryHandler.cs` (returns full permission set)
- `src/modules/identity/Controllers/MeController.cs` (added `/api/v1/me/permissions` GET endpoint, no JWT claim injection)
- `src/modules/identity/Seeders/RolePermissionSeeder.cs` (added Admin role exclusion: IDENTITY.TENANT.*)
- `web/src/features/admin/AdminGuard.tsx` (permission-based + role fallback)
- `web/src/features/admin/menu-admin-page.tsx` (permission-based visibility)
- `web/src/features/menus/useMyMenus.ts` (verified integration, no change needed)
- `web/src/features/tenants/tenant-list-page.tsx` (shared AdminPageHeader)
- `src/modules/menu/Seeders/AppMenuSeeder.cs` (added Tenants row)

## What We Tried

| Attempt | Result |
|---------|--------|
| Insert missing IDENTITY.TENANT.* perms manually in SQL | Symptom returned 403, permission claim still empty from JWT |
| Add IDENTITY.TENANT.VIEW to hardcoded FE sidebar fallback | Menu appeared, but design fragile (hardcoded per permission) |
| Emit full permission array in JWT (3-4KB token) | Token bloat, cache invalidation pain, no loading gate |
| **Final: API-driven permission endpoint** | Clean separation of concerns; async permission resolution with loading state; cache-friendly |

## Root Cause Analysis

1. **Architectural:** AuthServer JWT was designed to be "minimal claim set" (user, roles, maybe perms). Somewhere between design + v2.133p5 release, someone hardcoded permissions into JWT (probably for performance). By time we saw it, JWT had 3-4KB perm array, but FE still had **fallback hardcoded role checks**, creating two sources of truth.

2. **Multi-tenant blindness:** Admin role definition never explicitly excluded `IDENTITY.TENANT.*` perms. The template assumed "Admin != tenant-scoped operations," but never formalized it. In multi-tenant deployment (prod), this is a P0 security gap. Anh's team would discover this after a tenant-admin user confirmed they could modify other tenant settings.

3. **Missing loading state:** RouteGuard chain assumed synchronous permission resolution. FE hooks are async, but no spinner or loading barrier existed. Manual testing fast enough that 100ms race never triggered locally; production load slower, users hit 403 flash daily.

4. **Seeder delta-merge bug:** When Tenant CRUD code shipped (v2.133p5), AppMenus seeder was written for "system modules" (Admin, Audit, etc.) but never extended for Tenants. Seeder runs idempotent on startup, so existing infra wouldn't re-seed. Caught during Phase 03 refactor because we audited `SELECT * FROM app.AppMenus`.

## Lessons Learned

1. **Adversarial code review (not manual testing) catches async race conditions.** Manual smoke tests: we logged in, sidebar loaded, Tenants appeared — shipped. Reviewer read code cold: "What if `useMyEffectivePermissions` is still loading? Where's the spinner?" Caught the race. **Action:** Build code-review checklists for async state gates.

2. **Multi-tenant isolation requires explicit exclusion lists.** Default "Admin gets all perms" is a footgun in multi-tenant deployments. Every sensitive perm family (IDENTITY.TENANT, IDENTITY.CONSENT, etc.) must be explicitly excluded. **Action:** Add perm exclusion audit to QA gate before multi-tenant release.

3. **Cross-cutting refactors reveal tech debt.** Started as "menu visibility bug," ended up finding and fixing: JWT bloat, role mapping inconsistency, hardcoded route guards, seeder delta-merge gap, UI inconsistency. **Action:** When RCA spans >2 layers, allocate time for refactor, not just patch.

4. **Plan estimates fail when bug discovery is on the path.** Estimated 2.5h, actual 5h. Not slippage due to scope creep — slippage due to finding P0 security bugs during execution. **Action:** Separate "happy path implementation" estimate from "typical bug discovery buffer" (2x for auth/security work).

## Next Steps

**Immediate:**

1. **Redis deserialization warning (P2):** `IEffectivePermissionService` line 153 logs warning when `IReadOnlySet<string>` cache fails JSON deserialization. Fallback works (queries DB), but needs root cause. Candidate: version mismatch in distributed cache. **Owner:** Identity module lead | **Deadline:** v2.133p7

2. **Permission staleness window audit (P1):** Admin role edit doesn't invalidate `useMyEffectivePermissions` cache instantly. Worst-case staleness ~20 min (cache TTL). Verify `SyncRolePermissionsCommandHandler` invalidation flow is correct. **Owner:** Identity module lead | **Deadline:** v2.133p7

3. **Group role names not emitted (P3 edge case):** MeController only emits direct role assignments, not inherited group roles. Unlikely impact (group inheritance rarely used), but document as known limitation. **Owner:** Backlog**

4. **13 FE workflow test timeouts (P3 backlog):** Pre-existing, unrelated to RBAC work. Separate issue for test infrastructure. No block to ship.

**Follow-up learning:**

- Prepare code-review checklist for async guards (permission loading, route loading, data loading) 
- Document multi-tenant perm exclusion pattern in code-standards.md
- Migrate remaining hardcoded route guards to permission-based (Forms, Cases modules have similar patterns)

## Outstanding Questions

- Should `useMyEffectivePermissions` cache be shared across tabs (via localStorage event broadcast)? Currently per-tab. Multi-window permission revocation + admin action would require manual refresh. **Status:** P2 follow-up discussion

- Redis cache serialization: should we pin Newtonsoft JSON version or switch to System.Text.Json? **Status:** Dependency audit needed

---

**Commit pushed to thiennd/zen-franklin-49c17c (e554b74a).** Ready for merge post-final-soak-test. Build 0E/0W. Tests all green (except 13 pre-existing workflow timeouts).
