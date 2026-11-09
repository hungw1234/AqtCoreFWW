# Admin Can't See Signature Enrollment Approval Menu — Database Seeding Gap

**Date:** 2026-07-01 22:30  
**Severity:** High (blocking feature in P6 Slice C; user-facing)  
**Component:** Signature enrollments, Identity permissions, AppMenus  
**Status:** Resolved (Commit `49775a81` pushed origin+bitbucket)

## What Happened

User reported: sidebar menu for approving signing-cert enrollments missing despite Admin role having correct permissions. Initial hypothesis blamed the menu gate (`SYSTEM.CONFIG.MANAGE` + parent category `SECURITY.IP_RULE.VIEW`), prompting FE fix to fallback hardcoded menu entry in `admin-menu-entries.ts` + added new permission `SIGNATURE.ENROLLMENT.APPROVE`. Menu still didn't show after rebuild.

Second round: realized sidebar is driven by **database menu tree** (`identity.AppMenus`, Redis-cached as `menu-tree:{tenant}`) which OVERRIDES the FE fallback when non-empty. Real gap discovered: P6 Slice C shipped the route (`/admin/signature-enrollments`) + page component + FE fallback entry, but **never seeded the DB AppMenu row** into `E2ETestDataSeeder.Menus.cs`. Was operating on wrong layer — fixing FE fallback when the actual UI was rendering from DB.

Empirical debugging paid off: used `sqlcmd` against dev SQL Server container (`docker-sqlserver-1`, DB `AqtCoreFW`) to verify data layer. Confirmed permission row + Admin/SystemAdmin role mappings existed. Then confirmed `identity.AppMenus` table had `signature-config` but NOT `signature-enrollments`. That pinpointed root cause at data layer, not code logic.

Final fix: new `SIGNATURE.ENROLLMENT.APPROVE` permission (PermCodes + seeder), re-gated controller + both FE layers, seeded missing DB AppMenu row with proper parent link, dropped over-broad fallback parent gate, added VN module labels to permission drawer. Delta-merge seeders auto-granted permission to Admin+SystemAdmin on restart. User confirmed menu now displays.

## The Brutal Truth

Debugging frustration hit hard. Multiple failed hypotheses wasted 40 minutes:

1. **Wrong layer focus:** spent first 20min tweaking FE hardcoded fallback in `admin-menu-entries.ts` + `sidebar-menu.tsx`. Added new permission gate. Rebuilt. Menu still missing. Rage-checked console logs, Redux state (permission was there), checked `useMenuTree` hook. All correct. Then realized: **the sidebar doesn't even render the FE fallback when DB menu tree is populated**. Was fixing dead code. That's the sting — FE-first debugging on a DB-driven UI.

2. **Red-herring caching:** spent 10min chasing Redis invalidation theory (`perm:{userId}`, `menu-tree:{tenant}`). Checked cache TTL, tried manual invalidation in Postman. Caches had already expired. Wasted round-trip.

3. **Initial sqlcmd mistake:** first `sqlcmd` query hit `master` database (omitted `-d AqtCoreFW` flag) → "Invalid object name 'AppMenus'". Confused for 2 minutes until realized environment was wrong. DB is `AqtCoreFW`, not the decoy `taskflow_dev`. Careless.

4. **Multi-layer complexity is the real killer:** Sidebar rendering involves 4 places: (a) DB menu tree, (b) permission scopes, (c) FE hardcoded fallback, (d) Redux state hydration. Nobody documents the precedence. When one layer fails silently, you have to check all four. Exhausting.

## Technical Details

**Verified at data layer (sqlcmd):**
```sql
-- Permission row exists and is mapped to roles
SELECT * FROM identity.PermCodes WHERE Code = 'SIGNATURE.ENROLLMENT.APPROVE';  -- ✅ found
SELECT * FROM identity.RolePermissions 
  WHERE RoleId IN (SELECT Id FROM identity.Roles WHERE Name IN ('Admin', 'SystemAdmin'))
  AND PermissionId IN (SELECT Id FROM identity.PermCodes WHERE Code = 'SIGNATURE.ENROLLMENT.APPROVE');  -- ✅ found

-- AppMenu row is missing
SELECT * FROM identity.AppMenus WHERE MenuKey = 'signature-enrollments';  -- ❌ 0 rows
SELECT * FROM identity.AppMenus WHERE MenuKey = 'signature-config';  -- ✅ found
```

**Final fix includes:**
- `PermCodes.cs` — new `SIGNATURE.ENROLLMENT.APPROVE` (group SIGNATURE, tier ADMIN)
- `E2ETestDataSeeder.Permissions.cs` — new permission definition
- `E2ETestDataSeeder.Menus.cs` — new AppMenu row (`signature-enrollments`, parent `admin-category`, gate `SIGNATURE.ENROLLMENT.APPROVE`)
- `admin-menu-entries.ts` — updated FE fallback gate + VN labels
- `SignatureEnrollmentApprovalController.cs` — gate added to endpoint
- Delta-merge seeder auto-grants to Admin + SystemAdmin on boot

**Gates:** PermCodesSeederCoverage 2/2 ✅, BE build clean 0E/0W ✅, FE tsc+eslint clean ✅, DB-verified ✅

**Commit:** `49775a81` (preceded by image-upload ship `442bd137` same session)

## What We Tried

1. **Attempt 1 — FE gate refinement:** Added strict `SIGNATURE.ENROLLMENT.APPROVE` permission gate to `admin-menu-entries.ts` fallback. Hoped menu would render. Menu still missing. Realized fallback isn't active.

2. **Attempt 2 — Cache invalidation:** Assumed Redis expired cache. Checked TTL, manually evicted `menu-tree:{tenant}` in Postman. Reloaded UI. Menu still missing. Realized caches were already stale; not the issue.

3. **Attempt 3 — Empirical database audit:** Used `sqlcmd` against dev container. Queried permissions (found), then queried AppMenus (gap found). Confirmed root cause at data layer. Verified seeder fix needed.

4. **Attempt 4 — Full re-gate:** Applied fix at three levels: (a) new permission + seeder, (b) DB AppMenu seed row, (c) FE fallback updated + parent gate dropped. Verified menu renders after restart.

## Root Cause Analysis

**Why menu didn't show:**

1. P6 Slice C implemented route + component + FE fallback entry, but **omitted the DB AppMenu row in seeder**. When admin loads sidebar, `useMenuTree` fetches from DB. If non-empty, ignores FE fallback. Result: menu missing at runtime.

2. Initial hypothesis (over-broad parent gate on `SECURITY.IP_RULE.VIEW`) was plausible but wrong layer. Permission logic was sound; the data simply wasn't there.

3. **Layering bug:** No documentation that DB menu tree OVERRIDES FE fallback. Developer assumed fallback was active, so fixed FE. But fallback is dead code in DB-tree mode. This is a foot-gun in the architecture.

## Lessons Learned

1. **DB menu tree overrides FE fallback silently.** If `AppMenus` table is populated, FE hardcoded fallback never renders. Document this precedence clearly or flatten to single source.

2. **Empirical verification via SQL beats hypothesis chasing.** Used `sqlcmd` to query actual DB state rather than guess. That single query (`SELECT * FROM identity.AppMenus WHERE MenuKey = 'signature-enrollments'`) instantly revealed the root cause. Would have saved 30 minutes by doing this first.

3. **Seeding is a deployment contract.** P6 shipped new features (route, component, permissions) but forgot the data. All three must ship together. Add seeder checklist to feature review: (a) code, (b) permissions, (c) menu rows, (d) role mappings.

4. **Multi-layer debugging requires systematic elimination.** Sidebar involves 4 independent layers. Check them in order: (1) DB data, (2) permission mapping, (3) Redux hydration, (4) FE render. Eliminated DB first and found root cause. Hypothesizing layer order is faster than random patching.

5. **Redis caching is a debugging trap.** When features are gated on cached data, it's tempting to blame cache expiry. Check cache TTL + eviction, but verify DB state first. Cache usually isn't the culprit in permission/menu bugs.

## Next Steps

**Immediate (complete):**
- New `SIGNATURE.ENROLLMENT.APPROVE` permission + seeder definitions ✅
- AppMenu row seeded in `E2ETestDataSeeder.Menus.cs` ✅
- FE fallback updated + VN labels ✅
- Controller endpoint gated ✅
- Commit `49775a81` pushed ✅

**Follow-up (low priority):**
- `docs/dependency-map.md` — note Signature module now has new top-level permission group (SIGNATURE category, not under System)
- DB Security parent category still hard-gated on `SECURITY.IP_RULE.VIEW` (only FE fallback was relaxed) — consider flattening in next admin-menu refactor

**Lessons doc:**
- Add to developer guide: "Sidebar menu precedence: DB tree > FE fallback. When adding new admin feature, seed 3 things: route, permission, AppMenu row."

---

**Status:** DONE

**Summary:** Admin enrollment-approval menu missing due to omitted AppMenu seeder row in P6 Slice C. Empirical DB debugging (sqlcmd) identified gap in 5 minutes. Fixed via new permission + AppMenu seed + FE update. Commit `49775a81` pushed. Lesson: always verify DB state before hypothesis-chasing on data-driven UI features.

**Concerns:** None — all gates passing, feature user-verified. Carry-over Security parent gate hardening deferred to next admin-menu sprint.
