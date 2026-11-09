# Journal — Audit→Identity Cross-Schema Decoupled via IIdentityModuleClient (2026-06-11 ~17:00)

**Commit:** `b281f822` (NOT pushed — 5 ahead origin)

## Session goal
`AUDIT-IDENTITY-CROSSSCHEMA-DEBT` (T4): `GetLoginAuditQueryHandler` had a raw SQL `EXISTS` reference into `[identity].AspNetUsers` schema. Architecture test `ModuleIsolationTests.NoModule_Should_Reference_AnotherModuleSchema_InRawSql` was RED. Row was misfiled as "future tech-debt" but empirical probe (INVARIANT 260522-A) showed it was **active violation** from the morning read-fix `a9dd8a8f` (CI parked since April, no gate caught it).

## What worked
- **Option B verdict:** Extract `GetUserIdsInTenantAsync(Guid tenantId)` via new `IIdentityModuleClient` contract (NOT A: whitelist, NOT C: denormalize TenantId column).
  - New interface: `IReadOnlyList<Guid>` membership-only set (deliberately NO IsActive filter — locks invariant against future "helpful" IsActive=1 addition).
  - Impl `InProcessIdentityModuleClient`: schema-local `SELECT Id FROM [identity].AspNetUsers WHERE TenantId=@t` (identity module, not audit).
  - Handler: non-priv flow injects client → `GetUserIdsInTenantAsync` → JSON string → filters `la.UserId IN (SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON(@TenantUserIdsJson))`.
  - Privileged path unchanged (no pagination impact).
- **OPENJSON not Dapper @list parameter:** avoids SQL Server ~2100-parameter ceiling (ministry-scale tenants; chunking doesn't compose with COUNT+OFFSET pagination).
- **Behavior-identical, no migration:** Only doc-comment + SQL predicate location changed (identity module owns the lookup).

## Changes (5 files: 2 prod + 3 test)
1. `IIdentityModuleClient` → add `GetUserIdsInTenantAsync(Guid, ct)`.
2. `InProcessIdentityModuleClient` → implement membership-only query.
3. `GetLoginAuditQueryHandler` → inject client; non-priv path materializes tenant user-ids + OPENJSON filter.
4. 3 test stubs (`Workflow.Integration.Tests`) → implement new interface member (empty return, paths not exercised).
5. `LoginAttemptsTenantIsolationTests` + 3 new `IdentityModuleClientTenantUserIdsTests` (inactive user still returned = locks no-filter invariant).

## What didn't / lessons
- **LESSON 1 (reusable):** Interface signature-change blast-radius — grepped callers but missed OTHER IMPLEMENTERS. code-reviewer caught 4 `StubIdentityClient` test doubles in `Workflow.Integration.Tests` (CS0535 compile-fail). When adding interface member, grep `: IInterfaceName` class declarations (implementers), not just consumers. Fixed all 4 stubs.
- **LESSON 2 (reusable):** Verify subagent doc output against real model — docs-manager added a depmap note implying `ExternalIdentities` EXISTS is also cross-schema debt. It's NOT (`[identity].[identity].AspNetUsers` = same-schema, irrelevant). Corrected to prevent future anti-YAGNI "fix".

## Decisions made
- IIdentityModuleClient contract over whitelist (breaks module boundary explicitly, tests enforce).
- Over denormalization (migration burden, soft-delete+pagination complexity).
- No materialization caching (admin read-path, low QPS, YAGNI).

## Test status
Build 0W/0E `/warnaserror` · cross-schema architecture test RED→GREEN · 10 `LoginAttemptsTenantIsolationTests` pass UNCHANGED (no-regression proof, only doc-comment touched) · 3 new `IdentityModuleClientTenantUserIdsTests` incl. inactive case · workflow contract test smoke green.

## Code review arc
REQUEST_CHANGES (C1: missing stub implementations) → fixed + rebuilt 0W/0E → clean APPROVE.

## Backlog updates
- Filed advisory `LOGINAUDIT-TENANT-IDS-PERREQ-MATERIALIZE` (T4): future optimization to pre-fetch user-ids if request QPS rises (non-priv GET /login-audit now materializes all per request).
- Closed `AUDIT-IDENTITY-CROSSSCHEMA-DEBT`.

## Next session
- Backlog focus: `T5-01` KPI Alerting (flagship, ~20d, user deferred choice), polish suite (WF/Forms/BP).
- CI re-enable still pending (4 stale test families, user deferred; frontend job fixed via pnpm lockfile policy in earlier session).
- Push timing: user controls.
