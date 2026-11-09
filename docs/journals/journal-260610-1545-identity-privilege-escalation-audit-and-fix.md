# Identity Privilege Escalation — Audit & Critical Fixes

**Date**: 2026-06-10 15:45
**Severity**: Critical (3 verified, 1 follow-up High)
**Component**: AuthServer/Identity — RoleAssignment, DelegateRole, GroupRoleAssignment handlers
**Status**: Fixed, code-reviewed, unpushed (user decides timing)

## What Happened

`/ck-security` red-team audit (4 personas) on AuthServer+Identity uncovered 3 Critical privilege escalation paths + 1 High follow-up from code-review. All verified real against source + threat model. Fixed via 2 commits: `83425d02` (3C) + `b3f36496` (H-A). Unpushed; main ahead 2.

## The Brutal Truth

Three Critical security failures in production identity code. Not theoretical — **live exploitable paths from read-only-auditor grant to SystemAdmin role**. Compounded by seeder inconsistency hiding the real guard layer. This is the kind of audit that makes your blood cold: the code looks reasonable until you trace the grant table + name resolution together. The fact that role-sync AND group-assignment were both blind strengthens the case that someone wasn't thinking about this path systematically.

## Technical Details

**C1: View-Only Gate on Manage Operations (5 endpoints)**
- PolicyRules, CredentialPolicies, SodRules, Abac, Groups controllers gate POST/PUT/DELETE on `.View` authz only
- `.Manage` perms exist in `PermCodes.cs:125–167` (grantable codes) but were dead
- RolesAdmin/UsersAdmin patterns correct (guarded `.Manage`) — siblings forgot it
- Seeder grants 0 identity perms at runtime (assigned dynamically) → any auditor role config is **live write access**
- **Exploit**: read-only auditor + misconfigured tenant = policy mutation

**C2: Self-Assign SystemAdmin Role (AssignRoleCommandHandler:42 + SyncUserRolesCommandHandler)**
- `AddToRoleAsync(userId, "SystemAdmin")` unguarded; `IDENTITY.ROLE.MANAGE` is grantable to non-SystemAdmin
- `DeleteRoleCommandHandler:29` guarded `RoleType.System` correctly → assign path didn't = inconsistency
- `IsInRole("SystemAdmin")` coerced everywhere as final elevation check → bypasses all downstream authz
- **Exploit**: Admin + MANAGE perm = self-escalate to SystemAdmin

**C3: Delegation Self-Escalation (DelegateRoleCommandHandler:59)**
- Admin role sets arbitrary `delegatorId`; auto-active delegation (`requiresApproval:false`) unions delegator role set
- **Exploit**: Admin self-delegates SystemAdmin's roles, auto-active, instant elevation

**H-A: Group Role Assignment Blind (AssignRoleToGroupCommandHandler)**
- Assigned System-typed roles to groups → members inherit escalated privilege
- Same as C2 name-keyed guard missing

## What We Tried

1. **Guard on RoleType.System only** → Code-review caught: seeder creates "Admin"+"SystemAdmin" as `RoleType.Business` (line 166) → type-only guard inert at runtime. Rekey to name.
2. **Self-approval guard on DelegatorId** → Still exploitable: `ApproveDelegationCommandHandler:41` let any Admin approve → trace the approval path.

## Root Cause Analysis

1. **Authz scattered across 3 layers, none complete**: endpoint guards (C1 view-only)+ handler guards (C2 role-type, none on sync)+ domain (none). No systematic enumeration of privilege paths.
2. **Seeder inconsistency masks real guard surface**: `RoleType.System` set inconsistently → type-based guard silently inert. Name ("SystemAdmin") is the actual privilege vector; guard must follow it.
3. **Copy-paste pattern without peer review**: RolesAdmin/UsersAdmin had correct `.Manage` gates; 5 controller siblings copy-pasted wrong `.View` gate. No lint rule caught authz asymmetry.
4. **Multi-path to same escalation unexamined**: role-sync + role-assign + group-assign all touch the same table; auditor grepped one, missed others.

## Lessons Learned

1. **Verify findings against threat model, not just code**: `.View`/`.Manage` separability is key. Seeder grants nothing = runtime config is the exploit vector. Severity hinge on grantability of the perm itself.
2. **Guard on the actual privilege source, not metadata**: `IsInRole("SystemAdmin")` (name) confers power everywhere. `RoleType.System` was set wrong by seeders. Guard the name, verify seeder sets it correctly (separate ticket).
3. **Approval guards are theater if approver is privileged**: `requiresApproval:false` + self-approve = instant. If approval needed, trace handler: approver must be delegator OR SystemAdmin, AND ≠ CreatedBy AND ≠ DelegateId (4-eyes, enforced in handler).
4. **Enumerate ALL write paths to a resource**: grep junction-table `.Add` call-sites (AssignRoleToUser, AddToRoleAsync, AssignRoleToGroup) — 3 doors. One miss = incomplete fix.
5. **Test code must compile under build gate** — new unit tests used `(T)null` casts (CS8600) + deprecated `FormatterServices` (SYSLIB0050) hidden by `dotnet test` debug config. Fixed `(T?)null` + `RuntimeHelpers.GetUninitializedObject`. Warn-as-error caught it locally, CI would too (parked this phase).
6. **Always `git status` after delegated test-run** — verified tester touched test-files-only (prior session had rogue source edit from tester).

## Next Steps

**Blocked until user decision:**
- Both commits unpushed. User calls timing (CI parked, local warn-as-error + 270/270 unit + 4 integ H-A tests verify correctness).

**Follow-up (open, not blocking):**
- **L-A FE contract**: new `PendingApproval` delegation state + approval action surfacing (FE-P7 phase item).
- **Seeder hardening**: `OpenIddictDataSeeder.cs:166` set `RoleType.System` for Admin+SystemAdmin (defense-in-depth; code-guard already safe via name-check).

**Verification summary:**
- Build: 0W/0E (warn-as-error)
- Identity.Application unit: 270/270
- Identity integ (RoleAssignment tenant scope + 4 new H-A tests): 15/15
- Code-review: APPROVE_WITH_CONCERNS (3C, 0 blocking) + APPROVE (H-A)
- 11 source files (9 handler + 2 identity.core utilities) + 6 test files
- Commits: `83425d02` (3C gates + C2 name-guard) + `b3f36496` (H-A group-assign guard)

## Open Questions

1. User approval for push (CI parked; local gates passed)?
2. Seeder RoleType.System change: separate commit or roll into next bake?
3. FE PendingApproval state: in-scope for P7 or deferred?
