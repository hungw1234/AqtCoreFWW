# RBAC v2.133p7: Cache Invalidation Follow-Ups

**Date**: 2026-04-29 08:28  
**Severity**: High  
**Component**: Identity module — permission cache, role inheritance  
**Status**: Resolved  

## What Happened

Code-review feedback on merged v2.133p6 RBAC PR (#65) uncovered two cache bugs in the new API-driven permission system. User chose to fix both immediately (RBAC-1 + RBAC-2, ~3.5h total). Both shipped to main as commit follow-up pending.

## The Brutal Truth

Redis cache hit was a lie. Every call to get effective permissions deserved silent fallback to DB because the cached object couldn't be deserialized. That's a 20-min stale window for edited roles affecting delegation inheritance — multi-tenant environment exposed users to stale permission checks that looked correct until you traced the cache layer.

Worse: we knew it would happen. The code-reviewer red-flag on path-3 (delegation) missing in the initial cache invalidation handler means we shipped with silent gaps in both directions — can't deserialize AND can't invalidate. Felt sloppy.

## Technical Details

**RBAC-2 Deserialization Failure**  
- `EffectivePermissionSummary.PermissionCodes : IReadOnlySet<string>` — abstract collection type
- System.Text.Json cannot construct IReadOnlySet (no default ctor) → JsonSerializationException on every Redis cache hit
- Fallback to DB silent — no logging, no traces
- Fix: changed to `HashSet<string>` (implements IReadOnlySet, JSON-constructible)
- Cascade: 4 test files touched (SodConflictCheckerFixture, SodConflictCheckerTests, SyncRolePermissionsAuditWriteTests, TestEffectivePermissionService)
- Breaking? No — InProcessIdentityModuleClient still accepts HashSet as IReadOnlySet upcast

**RBAC-1 Stale Cache Window**  
- When `SyncRolePermissionsCommandHandler` edits role-permission mapping, only user claims cache (`user-roles:*`) was wiped
- New permission-codes cache (`perm:{userId}`) untouched → stale for 10 + 10 min (Redis TTL + React Query staleTime)
- Extended `IBulkRoleChangeService` with `InvalidatePermissionCacheForUsersAsync(IReadOnlyList<Guid>)` — chunked Redis DELETE 500/batch, mirrors existing role-cache pattern
- Handler queries full user inheritance union:
  1. Direct: `UserRoles.RoleId = X`
  2. Group: `GroupRoleAssignments.RoleId = X` joined to `UserGroupMembership`
  3. Delegation: `UserDelegations.DelegateId` where delegator ∈ (1 ∪ 2) AND active
- Path 3 was missing in first commit — code-reviewer caught via EffectivePermissionService.BuildFromDbAsync (lines 110–123) comment claiming "direct + group inheritance" but implementation also unions delegator roles
- Decision: fixed in-PR rather than defer to RBAC-3, because same handler, ~10 LOC, and ship-with-comment-mismatch is worse than over-invalidation cost (one extra DB query, admin-edit low-frequency)

## What We Tried

1. Fixed IReadOnlySet by inspection — no alternate approach needed
2. Traced cache invalidation paths against EffectivePermissionService.BuildFromDbAsync to find gap
3. Considered: precise filter (only invalidate users where DelegatedRoleIds overlaps RoleX) vs blanket invalidation — chose blanket (simpler, acceptable cost)

## Root Cause Analysis

**RBAC-2**: Assumed System.Text.Json handles IReadOnlySet (it doesn't). No test coverage for Redis round-trip with the full cached DTO. Would have failed on first integration test if cache was exercised.

**RBAC-1**: Initial spec in backlog ("UserRoles + GroupRoleAssignments") didn't call out delegation path. Code comment in EffectivePermissionService hinted at it, but wasn't factored into handler spec. Delegation inheritance is new in v2.133p6 — pattern not yet stable in team mental model.

## Lessons Learned

1. **JSON caching ≠ in-memory**: Always test serialization round-trip with the actual JSON library when caching complex objects. IReadOnlySet feels like it should work; it doesn't.

2. **Match comment to code**: When cache invalidation handler has comment "invalidate user-roles", but EffectivePermissionService builds from DB using path-1 ∪ path-2 ∪ path-3, the comment is a lie. Review must verify handler vs service contracts, not just handler logic.

3. **Delegation inheritance is surface-level complexity**: Looks like "oh, just add another join". Actually affects 3 parts simultaneously: claim enrichment, permission lookup, AND cache invalidation. Don't treat as local change.

## Next Steps

- **Possible follow-up (RBAC-3, P3)**: Add integration test asserting `InvalidatePermissionCacheForUsersAsync` called with exact user union — would have caught delegation gap automatically
- **Metric gap (P3)**: Redis hit/miss rate not yet exposed — would surface RBAC-2 class (cache failures) in production dashboards
- **Open backlog (P3)**: GroupRoleInfo missing RoleName field (from v2.133p6 review)
- **Seeder cleanup (P3)**: Orphan rows when permission code retired; dead PRESERVED_KEY_PREFIXES entries

## Build Verification

- `dotnet build AqtCoreFW.slnx -c Debug`: 0E/0W
- Identity.Infrastructure.Tests: 66/66 PASS (3 new tests + 5 cascade-touched)
- Identity.Application.Tests: 214/214 PASS
- New test file: BulkRoleChangeServicePermCacheTests.cs — empty-short-circuit, KeyDelete-with-perm-prefix, RedisException-no-throw

---

**Status**: DONE  
**Summary**: Fixed Redis deserialization bug (RBAC-2) + stale cache window for delegation users (RBAC-1). Cache invalidation handler now unions all 3 inheritance paths. All tests PASS, 0E/0W.  
**Concerns**: None — redlined by code-reviewer, shipped.
