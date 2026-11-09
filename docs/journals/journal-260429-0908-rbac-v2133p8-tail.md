# RBAC v2.133p8 Tail — Allowlist Hygiene + Group Role Visibility (RBAC-5 + RBAC-3)

**Date:** 2026-04-29 09:08
**Severity:** Medium
**Component:** Authorization (Identity), Query Cache Config (FE)
**Status:** Resolved
**Branch:** main | **Commits:** pending

## What Happened

v2.133p6 shipped 3-phase RBAC refactor (JWT revert → API-driven perms → React Query hook). v2.133p7 fixed Redis deserialization bug (HashSet type mismatch). Anh asked "công việc tiếp theo?" → RBAC-5 (P2, ~15min allowlist trim) + RBAC-3 (P3, ~1h group role visibility). Close v2.133.x RBAC follow-up tail.

## The Brutal Truth

Two edge cases existed in shipping RBAC-1,2,6 code that manual testing never caught:

1. **Allowlist bloat:** Query cache config had stale permission key roots from abandoned design (e.g., singular `'permissions'` → never matches modern `me` root). No runtime impact (keys just never hit the allowlist), but mental debt.
2. **Group-only user gets empty roles:** User assigned roles via group membership (not direct assignment) would return `roles=[]` from `/api/v1/me/permissions`, failing FE `AdminGuard` permission checks. Edge case but **live bug** in multi-org deployments where role assignment flows through group inheritance.

The second hit harder: we'd tested direct role assignment (obvious path), assumed group inheritance worked (it did for permission inheritance), but never traced the full data flow from group → role **name** emission in the API response.

## Technical Details

**RBAC-5 (Query Cache Allowlist Hygiene):**

File: `web/src/core/api/query-config.ts:11`

```
BEFORE: PRESERVED_KEY_PREFIXES = ['auth', 'permissions', 'menu', 'i18n', 'session']
```

Root cause: v2.133p6 designed to emit `permissions` claim from JWT (abandoned for API endpoint). Modern roots:
- `me` = user profile + current org (via `/api/v1/me`)
- `menus` = user's visible menus (via `/menus/my-menus`) — tenant-scoped per lines 8-10 design comment
- `auth`, `i18n`, `session` = still active

Singular `'permissions'` and `'menu'` never match any query key. Result: dead allowlist entries create cognitive friction on code review.

```
AFTER: PRESERVED_KEY_PREFIXES = ['auth', 'i18n', 'session']
```

Test: `vitest run query-config-predicate.test.ts` — 6/6 PASS (test.each auto-adapts to new array).

**RBAC-3 (Group Role Names Not Emitted):**

Root cause: `EffectivePermissionService` computes role set via two paths:

1. **Direct roles:** `User.Roles` SELECT `DirectRoles.RoleName` ✅
2. **Group roles:** `User.UserGroupMemberships` → `GroupRoles` — carried `RoleCode` (uppercase "ADMIN") but lost `RoleName` (PascalCase "Admin") in `GroupRoleInfo` record

`MeController.GetMyPermissions` merged only `DirectRoles.RoleName` into `roles` list. Group members got `roles=[]`.

Files modified:

1. **EffectivePermissionSummary.cs:42** — Extended `GroupRoleInfo` record with 5th positional param `string RoleName`
   ```csharp
   public record GroupRoleInfo(
       Guid GroupId, string GroupName, Guid RoleId, 
       string RoleCode, string RoleName);  // <- NEW
   ```

2. **EffectivePermissionService.cs:85–90** — JOIN selector populates role name from `x.Role.Name ?? string.Empty` (mirrors logic at line 74 for direct roles)
   ```csharp
   GroupRoles = groupRoleAssignments.Select(x => new GroupRoleInfo(
       x.Group.Id, x.Group.Name, x.Role.Id, x.Role.Code,
       x.Role.Name ?? string.Empty  // <- NEW
   )).ToHashSet()
   ```

3. **MeController.GetMyPermissions:67** — role emission now includes both paths, deduplicated
   ```csharp
   Roles = response.DirectRoles.RoleName
       .Concat(response.GroupRoles.RoleName)
       .Where(r => !string.IsNullOrWhiteSpace(r))
       .Distinct()
       .ToList()
   ```

**Cache self-heal:** Adding 5th positional param changes `GroupRoleInfo` constructor. Stale v2.133p7 Redis payloads (4-arg JSON) fail JSON deserialization on cache hit. Try/catch at `EffectivePermissionService.cs:155` catches the error, falls back to DB query, returns fresh payload with 5 args. Same pattern as v2.133p7 HashSet type change. No migration script needed.

## Verification

```
dotnet build AqtCoreFW.slnx -c Debug
→ 0 errors, 0 warnings

AqtCoreFW.Identity.Infrastructure.Tests
→ 66/66 ✅

vitest run query-config-predicate.test.ts
→ 6/6 ✅
```

**Files changed:** 4 total (1 FE, 3 BE)
- `web/src/core/api/query-config.ts` (trim allowlist)
- `src/modules/identity/Models/EffectivePermissionSummary.cs` (add RoleName param)
- `src/modules/identity/Services/EffectivePermissionService.cs` (populate group role names)
- `src/modules/identity/Controllers/MeController.cs` (merge and deduplicate role sources)

## What's Left

**RBAC-4 (P3, trigger-based):** Seeder orphan cleanup — fires only when first permission code is retired from use. No action this cycle.

**v2.133.x RBAC arc status:** Essentially closed. RBAC-1 (JWT revert), RBAC-2 (HashSet deser), RBAC-3 (group roles), RBAC-5 (allowlist) shipped. RBAC-4 deferred to trigger event.

## Lessons Learned

1. **Group inheritance edge cases survive manual testing.** We tested direct assignment, group assignment worked for permission bits, so we assumed role inheritance was correct. Never traced the full `Roles` array flow. **Lesson:** Code review must explicitly trace all data paths for inheritance/composition patterns.

2. **Stale design artifacts in config are debt.** `['permissions', 'menu']` allowlist entries were harmless at runtime (just unused), but accumulated friction on every code review. **Lesson:** Quarterly audit of config allowlists/constants for dead code.

3. **Adding positional record params requires cache strategy.** New param mid-release breaks existing cache payloads. Relying on self-heal (try/catch + fallback) works but obscures the migration. **Lesson:** Document cache versioning strategy for breaking changes to serialized DTOs.

## Next Steps

- No blocking issues. RBAC-1 through RBAC-3 and RBAC-5 shipped; RBAC-4 waiting on business event.
- Consider: Document group role inheritance pattern in code-standards.md (avoid future dev confusion).
- Audit: Any other cached record types that might have similar positional param gaps? Search `IEffectivePermissionService` usages.

---

**Status:** DONE

**Summary:** Fixed allowlist bloat in query cache config. Fixed group role name visibility — group members now correctly report inherited roles in `/api/v1/me/permissions` response. Both ship as v2.133p8.

