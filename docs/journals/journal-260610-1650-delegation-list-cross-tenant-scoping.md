# Journal — Delegation List Cross-Tenant Scoping (2026-06-10 ~16:50)

**Commits:** `b0af06f1` (fix) · `128674f8` (docs) — PUSHED origin+bitbucket, ahead=0.

## Session goal
`/cook DELEG-LIST-OWNERSHIP-SCOPE` — close the harvest half of the revoke broken-access-control vector filed in the prior slice.

## What worked
- **Scout resolved the filed open-question definitively.** `UserDelegation` has **no TenantId column** and there is **no RLS policy** for `[identity].UserDelegations` (only the EF Designer snapshot references the entity; the migration `.cs` Up() has no SECURITY POLICY). So the `sp_set_session_context N'TenantId'` that `DapperReadDbConnection` sets on every read is a **no-op for this table** → `GET /delegations` returned ALL delegations across ALL tenants + soft-deleted to any authenticated caller. The FE `useDelegations` hook sends no owner filter, so the `/delegations` page actually exhibited the leak.
- **Fix (Model C, query-level, no migration):** `ListDelegationsQuery` gains `ActorId` + `IsPrivileged`; controller derives privileged from `User.IsInRole(SystemAdmin|Admin)`; handler forces non-privileged callers to `(DelegatorId=@me OR DelegateId=@me)` and tenant-binds privileged callers via `EXISTS([identity].AspNetUsers WHERE Id IN (DelegatorId,DelegateId) AND (TenantId=@T OR TenantId IS NULL))`; `d.IsDeleted=0` now unconditional.
- Build slnx Release `/warnaserror` 0W/0E · integration 6/6 (real SQL via Testcontainers, 42s) · code-review APPROVE 0-blocking.

## What didn't / lessons
- **Severity was under-estimated when filed** ("relies on unverified RLS, in-tenant"). The empirical truth was worse: cross-tenant disclosure (A01), because the tenant-isolation mechanism the rest of the app relies on simply does not apply to this table.
- **Reusable: a "tenant scope" docstring is not proof of tenant scoping.** Verify (1) the entity/table has a TenantId column, (2) an actual RLS SECURITY POLICY exists in the migration `.cs` Up() — not just the Designer snapshot, (3) remember Dapper bypasses EF global query filters, so only DB-level RLS scopes a raw-SQL read.
- **Reusable test technique:** the integration fixture's DI tenant context is null (no HttpContext), so I tested the privileged tenant-bind handler-direct with a stub `ITenantContext(tenantA)` and asserted the bind holds even when an out-of-tenant delegator is *explicitly* requested (adversarial framing, flake-proof against shared-DB pagination). Non-privileged ownership tested via mediator (deterministic).
- Seed soft-deleted rows via `entity.Delete()` before SaveChanges (inserts a row with IsDeleted=1 that Dapper would return without the new guard).

## Decisions made
- Model C (ownership for non-admin, tenant-bound for admin), query-level scoping only (controller stays `[Authorize]`, no new permission attribute, no FE change), tenant-bind on BOTH parties (user choice) — a cross-tenant delegation is intentionally visible to both tenants' admins.
- No schema/migration change (no TenantId column added).

## Test status
Build 0W/0E (warn-as-error) · ListDelegationsQueryTests 6/6 integration (3 existing + 3 new) · code-review APPROVE, 0 blocking (non-blocking: Guid.Empty bad-claim matches 0 rows = fail-safe; pre-existing RevokedAt null).

## Next session
- Backlog optimization is triggered (635 lines, 12 closed sections) — deserves a dedicated careful pass.
- CI still parked (push ≠ CI-verified). Re-enable remains a separate multi-slice effort (`CI-DISABLED-SINCE-APRIL`).
- Flagship feature (`T5-01` KPI Alerting) still unpicked — feature-dev phase.
