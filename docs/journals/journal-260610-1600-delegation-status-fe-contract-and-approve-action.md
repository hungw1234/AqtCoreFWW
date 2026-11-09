# Delegation Status FE Contract + Approve Action

**Date**: 2026-06-10 16:00
**Severity**: High
**Component**: Delegation (BE + FE)
**Status**: Resolved

## What Happened

C3 privilege-escalation fix (`bbbcdcd8`) added `PendingApproval` status to delegations, but FE had no contract for it. `DelegationDto` exposed only `isRevoked` + time window; FE's `deriveIsActive` rendered pending as green "active" with no approval pathway. Wire and UI both blind to the intermediate lifecycle state.

## The Brutal Truth

Silent contract violation. BE shipped a state the FE couldn't see or act on. Worse, the FE actively **mislabeled** pending delegations as active because the status field was absent. Users see green checkmark on a delegation that isn't actually effective yet—no way to approve. This is embarrassing because C3 was a security fix; the fix itself introduced a UX hole.

## Technical Details

**BE changes:**
- `ListDelegationsQuery.cs` DelegationDto: added `string Status` field.
- `ListDelegationsQueryHandler.cs`: SQL selects `d.Status`; wire as enum name (e.g., `"Active"`, `"PendingApproval"`); **activeOnly filter changed** from `IsRevoked=0 AND in-window` to `Status=Active(2) AND in-window`. Pending delegations no longer show in the active list—correct, because they're not yet effective per `IsEffective` domain logic.

**FE changes:**
- `delegation-api.ts`: `DelegationStatus` union type, DTO `status` field, `useApproveDelegation()` hook (POST /delegations/{id}/approve).
- `delegation-list-page.tsx`: badge color from status (Active green, PendingApproval gold "Chờ duyệt", Expired gray, Revoked red); Approve button gated `delegatorId===myUserId || hasRole('SystemAdmin')` (mirrors BE authority, BE re-enforces 403).
- i18n: 6 new keys (vi.json, en.json).

**Test coverage gaps found and fixed:**
- BE integ tests compiled but failed on first run: `DbUpdateException` (seeded delegations with unmapped user IDs → FK violation). Added `BuildUser()` seeding before delegation creation. 3/3 integ now green.
- FE had 13 regression test stubs (compiled, never exercised); ran full vitest suite 20/20 to exercise real `useApproveDelegation` hook + role-gate logic.

## What We Tried

1. **Option A (not taken)**: show all status values in active list, gate Approve button on 403. Rejected—activeOnly means "effective delegations"; pending is not effective yet (API doesn't use it for permission checks), so filtering at the wire boundary is cleaner.

2. **Option B (taken)**: expose Status as enum-name string; BE filter to Status=Active only; FE badge + Approve gate. Requires DTO version bump (minor).

## Root Cause Analysis

1. **BE+FE contract mismatch**: C3 added a new delegation state but didn't update the DTO. FE had no field to observe the state. No integration test ran the real wire contract.
2. **Derive from incomplete data**: FE's `deriveIsActive` logic pre-dated C3; adding a new state without updating its callers is the classic "extension without extension" bug. The FE wasn't wrong—it was working from incomplete data.
3. **Test-only no-run mistake**: 3 BE integration tests compiled but were never executed before commit. Seeding bug invisible until `dotnet test` runs.

## Lessons Learned

1. **Expose every lifecycle state at the wire boundary**. Whenever a BE adds a status/state, enumerate all DTO callers and FE consumers. One missing field = one silent bug. Trace the chain: domain state → DTO field → FE display/action.

2. **Use enum NAME strings at wire boundaries, not integers**. `Status.ToString()` + FE `type DelegationStatus = 'Active' | 'PendingApproval' | ...` is config-independent and unambiguous. An integration test asserting the wire value lives in `['Active', 'PendingApproval', ...]` catches future enum reordering.

3. **Compile ≠ tested**. The 3 integration tests compiled cleanly but failed on first run (FK seeding). Always run `dotnet test` or `npm test` before pushing. Compile is a type check, not a behavior check.

4. **Verify role-gate shape before gating on it**. FE gates on `hasRole('SystemAdmin')`. This works because `/me/permissions` returns role **names** (per `MeController` design), not codes. If the API returned role IDs, the gate silently breaks. Check the shape; don't assume.

## Next Steps

- **Done**: commit `bbbcdcd8` (feat + docs) pushed origin/bitbucket ahead=0.
- **Optional hygiene**: Orval-generated client in `web/src/api/generated/delegation/` is stale (unused; FE hand-wrote the API). Regen or mark read-only.
- **Cosmetic**: ApproveDelegationCommandHandler docstring says "Admin/SystemAdmin"; code is "delegator OR SystemAdmin" (pre-existing, not fixed in this slice).
- **No-op at scale**: No status index on UserDelegations (admin table <100 rows in practice).

**File**: `C:\AqtCoreFW\docs\journals\journal-260610-1600-delegation-status-fe-contract-and-approve-action.md`
