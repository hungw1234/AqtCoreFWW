# Journal — PHASE6-RLS-PROD-PRINCIPAL-VERIFY: Defense-in-Depth Operational Check

**Date:** 2026-05-12 21:00 ICT  
**Branch:** `fix/rls-prod-principal-verify`  
**Commit:** `aacd35e0`  
**Status:** SHIPPED (code-review APPROVE_WITH_NITS; PR #102 open, not merged)  
**Work:** P3 follow-up from PHASE6-RLS-NULLABLE-TENANT; immediate predecessor AUDIT-EXPORT-TENANT-FORCE (PR #101 merged to main)

---

## Summary

Shipped **operational verification layer** for RLS precondition: prod app login must not hold `db_owner`/`sysadmin`/`db_securityadmin` roles (which bypass all 36 tenant-isolation security policies at the engine level). Option B scope: deployment-guide docs + non-throwing startup self-check. New `RlsPrincipalValidator.cs` (sibling of `ProductionSecretValidator`) executes `SELECT IS_SRVROLEMEMBER('sysadmin'), IS_MEMBER(...)` on boot in Production; if defeating role detected → logs Critical with login/db/remediation hint (does NOT throw — misconfigured-but-working app surfaces in alerts, not crash-loop). Docs §4 "Application DB Principal" specifies least-privilege grant (db_datareader + db_datawriter + EXECUTE; db_ddladmin transitional only), separate migration login, and verification query. **Note:** `RUN_MIGRATIONS_ONLY` gate is documented but not implemented — both hosts still call `MigrateAsync()` on every boot → runtime login still needs db_ddladmin transitional. Build 0E, Infrastructure.Extensions.Tests 106/106 (+12 new), Architecture 94/94.

---

## What Changed

**New `RlsPrincipalValidator` class:**
- **Path:** `src/shared/AqtCoreFW.Infrastructure/Configuration/RlsPrincipalValidator.cs`
- **Public API:** `IHost.WarnIfDbOwnerInProduction()` extension method (Production-only guard via `env.IsProduction()`).
- **Behavior:**
  - Opens `SqlConnection` to `ConnectionStrings:Default`.
  - Executes role-check query: `SELECT IS_SRVROLEMEMBER('sysadmin'), IS_MEMBER('db_owner'), IS_MEMBER('db_securityadmin'), IS_MEMBER('db_ddladmin'), ORIGINAL_LOGIN(), DB_NAME()`.
  - Parses results into `PrincipalRoleFlags` bitmask.
  - If `HasRlsDefeatingRole` (sysadmin OR db_owner OR db_securityadmin) → `ILogger.LogCritical(BuildCriticalMessage(...))` naming login, database, roles held, remediation link.
  - **Does not throw.** Failed probe (SQL exception, permission denied) → `LogWarning`, swallowed.
  - Pure helper `BuildCriticalMessage(PrincipalRoleFlags)` for unit testing.
  - `db_ddladmin` alone does NOT trip `HasRlsDefeatingRole` (informational only — it can DDL-disable a policy but doesn't satisfy `IS_MEMBER('db_owner')`).

**Integration into `Program.cs` (Api + AuthServer):**
```csharp
await app.Services.GetRequiredService<IHostingEnvironment>()
  .MigrateAsync(/* ... */);
app.WarnIfDbOwnerInProduction();  // After migrations (DB/schema must exist on first deploy)
```

**Docs:**
- **`docs/deployment-guide.md` § 4 NEW:** "Application DB Principal (least privilege — required for Row-Level Security)"
  - Rationale: RLS predicates carved out `IS_MEMBER('db_owner')=1` for tooling; prod login must NOT hold defeating roles.
  - **Hard requirement:** Runtime login is `db_datareader` + `db_datawriter` + `EXECUTE` (on stored procs / UDFs).
  - Migration login (separate, elevated): `db_ddladmin` (transitional — backlog `RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS`).
  - Verification query (4-role check matching the self-check code).
  - ⚠️ **Caveat:** `RUN_MIGRATIONS_ONLY` / `--migrate-only` init-container gate is documented but **not actually implemented**. Both Api + AuthServer hosts call `MigrateAsync()` for all module contexts on every boot → runtime login currently must also hold `db_ddladmin` (transitional).
  - ⚠️ **Blind spot:** Direct `GRANT CONTROL` or `GRANT ALTER ANY SECURITY POLICY` to the user (not via a role) is **not detected** by `IS_MEMBER()` check. Deployment lead must verify via `SELECT * FROM sys.database_permissions WHERE [name] = 'CONTROL'`.
  - Remediation: migrate login, provision runtime with db_datareader/writer/EXECUTE, verify via query.

**Test suite (12 tests, new file `RlsPrincipalValidatorTests.cs`):**
1. `BuildsCriticalMessage_WithAllThreeDefeatingRoles` — message lists sysadmin/db_owner/db_securityadmin in precedence order.
2. `BuildsCriticalMessage_WithDbOwner_Only` — single role case.
3. `BuildsCriticalMessage_WithDbSecurityAdmin_Only` — can self-elevate to db_owner.
4. `BuildsCriticalMessage_WithSysadmin_Only` — server-wide role.
5. `BuildsCriticalMessage_WithDbDdlAdmin_Only` — **does NOT** set `HasRlsDefeatingRole` flag (info-only).
6. `BuildsCriticalMessage_WithMultipleDefeatersAndDdlAdmin` — mixed roles; ddl-admin correct reported but not part of defeating check.
7. `BuildsCriticalMessage_UnknownLogin` — null login → `(unknown login)` placeholder.
8. `BuildsCriticalMessage_UnknownDatabase` — null db name → `(unknown database)` placeholder.
9. `BuildsCriticalMessage_IncludesRemediationHint` — message contains "See deployment-guide.md §4".
10. `HasRlsDefeatingRole_TrueFor_Sysadmin_DbOwner_DbSecurityAdmin_Alone` — 3× assertion.
11. `HasRlsDefeatingRole_FalseFor_DbDdlAdmin_Alone` — negative case.
12. `HasRlsDefeatingRole_TrueWhenAnyDefeatersPresent` — combined flags.

IHost/live-SQL path untested (integration territory; mirrors `ProductionSecretValidator` test boundary).

---

## The Brutal Truth

RLS is a **beautiful backstop** — right up until it isn't. All 36 tenant-isolation policies in `RlsMigrationHelper` + `RlsMigrationHelper.NullableTenant` carve out `IS_MEMBER('db_owner')=1` to let migration/admin tooling cross tenants. But if the prod runtime app runs as `db_owner` / `sysadmin` / `db_securityadmin`, the engine **silently ignores all 36 policies**. EF global query filters still apply in-process (so ORM queries are safe), but any raw-SQL path (Dapper, audit exports, reporting queries) becomes wide-open.

The frustrating part: you can't enforce this in code or CI. It's a **deployment operational requirement**. If ops misconfigures it, the app boots, handles traffic, and looks fine — until someone audits logins and realizes the defense is theatre.

This is the cost of shipping RLS without a mandatory gateway check. We're shipping both: (1) loud logging so it surfaces in alerts, (2) explicit deployment docs so the next ops engineer has no excuse.

---

## Technical Details

### Role-Check Query

```sql
SELECT 
  IS_SRVROLEMEMBER('sysadmin') AS IsSysadmin,
  IS_MEMBER('db_owner') AS IsDbOwner,
  IS_MEMBER('db_securityadmin') AS IsDbSecurityAdmin,
  IS_MEMBER('db_ddladmin') AS IsDbDdlAdmin,
  ORIGINAL_LOGIN() AS OriginalLogin,
  DB_NAME() AS CurrentDatabase
```

Executed on every app boot (Production only). Takes ~5-10ms (single roundtrip, no schema scan).

### Why `db_ddladmin` Alone Is Not Defeating

- `db_owner` role satisfies `IS_MEMBER('db_owner') = 1`.
- `db_securityadmin` can `ALTER SECURITY POLICY` (bypass via UNMASK), so it indirectly satisfies db_owner semantics.
- `db_ddladmin` can DDL (CREATE/ALTER/DROP) but does NOT satisfy `IS_MEMBER('db_owner')`. RLS still applies. **However**, it CAN `ALTER SECURITY POLICY ... WITH STATE = OFF` (disable it). So it's reported in logs as "watch this transitional grant," not "RLS is broken."

---

## Decisions

1. **Non-throwing startup check.** `ProductionSecretValidator` throws because a missing secret means the app cannot function. A misconfigured DB role means the app *can* function (albeit insecurely). Log Critical and surface in alerts; let ops choose to escalate or downtime to remediate.

2. **Production-only guard.** Dev / Staging / Test environments run as `dbo` or admin — expected. Only prod must be least-privilege. Guard via `env.IsProduction()`.

3. **Placement after migrations.** The self-check runs a SELECT against the database. On first deploy, the DB/schema must exist. Placed immediately after `MigrateAsync()` ensures the connection works.

4. **`ORIGINAL_LOGIN()` + query user context.** The check runs as the app login. If that login is compromised or proxied, we report the **effective** login (not the Windows user). Accurate for prod scenarios.

---

## Lessons Learned

1. **Defense-in-depth layers can be nullified by operational preconditions outside the codebase.** RLS is brilliant code-level isolation, but it requires a prod-deployment precondition (least-privilege login). When you can't verify the precondition at build/CI time, ship: (a) **explicit deployment docs** with exact provisioning + verification commands, (b) **runtime self-check** that makes the violation loud (Critical log + alerting) without crashing the app.

2. **Temporal coupling:** The `--migrate-only` gate is documented but not implemented. The app currently *must* hold `db_ddladmin` at runtime because both the init container and the runtime container call migrations. This is a backlog item (P2 `RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS`), but document it **prominently** in the deployment guide until fixed — ops will notice a discrepancy.

3. **Log levels matter.** `LogWarning` for probe failure (transient SQL issue, permission denied). `LogCritical` for defeating role detected (operator must act). Avoid `LogError` (sounds like a bug) and `throw` (sounds like a crash). Use the right level to guide ops response.

---

## Follow-ups Spawned (P3)

- **`RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS`** — Implement the documented `RUN_MIGRATIONS_ONLY=true` / `--migrate-only` init-container gate so the runtime container skips in-app migrations. Allows runtime login to drop `db_ddladmin`.
- **`RLS-PRINCIPAL-FAILFAST`** (YAGNI, backlog) — Optional config `RlsPrincipal:FailFast=true` to upgrade Critical-log to a throw. Ops can opt-in if they want prod to crash on misconfiguration. Not shipping by default.
- **`RLS-PRINCIPAL-DIRECT-GRANT-CHECK`** (P3 enhancement) — Extend self-check to scan `sys.database_permissions` for direct `CONTROL`/`ALTER ANY SECURITY POLICY` grants (not role-mediated). `IS_MEMBER()` misses these. Low priority (rare, requires explicit grant), but documentable gap.

---

## Code Review Status

**Verdict:** APPROVE_WITH_NITS (all nits folded before commit).

**Nits addressed:**
1. Probe moved after migrations (original placement after `ValidateProductionSecrets()` would skip the check on a fresh DB).
2. `db_ddladmin` added as info-only flag so operators see the same 4-flag picture the deployment-guide verification query shows.
3. Doc caveats added: (a) `db_ddladmin` can DDL-disable a policy (transitional only); (b) direct `GRANT CONTROL` / `ALTER ANY SECURITY POLICY` not detected by `IS_MEMBER()` check.
4. Role-order assertion in tests (sysadmin → db_owner → db_securityadmin precedence).
5. `using static` / qualified-call inconsistency resolved in test file.

**Report:** `plans/reports/code-reviewer-260512-2030-rls-prod-principal-verify.md`

---

## Build Status

- **Build:** 0E (clean).
- **Infrastructure.Extensions.Tests:** 106/106 PASS (+12 new).
- **Architecture.Tests:** 94/94 PASS.
- **All module suites:** Green baseline maintained.

---

## Unresolved

- Should we add a **direct grant check** (scan `sys.database_permissions`)? Code-review suggested it as P3 (rare case). Current `IS_MEMBER()` only detects role-mediated grants.
- What's the **acceptable frequency** for running the self-check on every boot? 5-10ms is negligible, but should it be gated behind a config flag (e.g., `RlsPrincipal:EnableStartupCheck=true`)? Current design: always-on in Prod.
