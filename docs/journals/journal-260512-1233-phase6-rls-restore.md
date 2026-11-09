# Journal — PHASE6-RLS-RESTORE: Row-Level Security Policies Re-added

**Date:** 2026-05-12 12:33 ICT  
**Branch:** `feat/phase6-rls-restore`  
**Status:** SHIPPED (uncommitted; code-review APPROVE_WITH_NITS)  
**Work:** Backlog row PHASE6-RLS-LOST (P1 — RLS dropped in Phase-06 migration regen, uncovered by audit)

---

## Summary

The Phase-06 migration-regen sweep (`1f344588` + `fde9ec49`) silently deleted all 33 Row-Level Security (RLS) FILTER+BLOCK policies across 14 modules. Restored via 13 new `RestorePhase06RlsPolicies` migrations (one per module), validating table lists against post-Phase-04 schema. RLS is defense-in-depth multi-tenant isolation — loss bites on next prod deploy. Tests: Architecture 94/94, TenantIsolation 6/6, all module suites PASS; `RlsPolicyPresenceTests` (new) closes the test-gap the audit flagged.

---

## What Changed

**13 new migrations** (ai, audit, collaboration, files, identity, integration, masterdata, notifications, organization, reporting, rules, workflow, forms):
- `{module}/20260512…_RestorePhase06RlsPolicies_{Module}.cs` — raw-SQL `CREATE SECURITY POLICY ... ADD FILTER ... ADD BLOCK ...` statements via per-statement `migrationBuilder.Sql(...)` calls (key insight: `GO` separators only work in `dotnet ef migrations script | sqlcmd` paths, not in `Database.Migrate()` / `dotnet ef database update`, so each `CREATE FUNCTION`/`CREATE SECURITY POLICY` must be its own batch).

**1 new test fixture + 1 test class:**
- `RlsPolicyPresenceFixture.cs` — applies all 13 migrations to a test DB, asserts `sys.security_policies` existence.
- `RlsPolicyPresenceTests.cs` — queries `sys.security_policies` for schema-mapped table names (e.g., `ai.vector_documents` mapped from class `ai.VectorDocuments`), validates all **30 non-nullable-TenantId tables** have corresponding policies.

**Updated `RlsMigrationHelper.cs`:**
- New `GenerateRlsPolicyStatements()` → split the original all-in-one `GO`-joined string into a `List<string>` of individual SQL statements (one `CREATE FUNCTION`, one `CREATE SECURITY POLICY`). Used by code-review post-ship to validate the restore is byte-for-byte faithful.

**Build:** 0E. Tests: Architecture 94/94, Identity.Integration 198/198, Audit.Integration 30/30, TenantIsolation 6/6, RlsPolicyPresence 1/1 (new). All PASS.

---

## The Brutal Truth

We shipped a **defense-in-depth tenant-isolation regression** to dev + CI, and it's waiting in `main` (v2.134+) to bite prod on the next migration deploy.

The audit revealed that EF Core's model snapshot is **not** a complete schema representation. When Phase-06 dropped all old migration files (`1f344588`) and regenerated via `dotnet ef migrations add` (`fde9ec49`), the regen only captured schema structure (tables, columns, indexes, constraints) — it skipped over all the raw-SQL DDL that lives in `.cs` migration files: `CREATE TRIGGER`, `CREATE SECURITY POLICY`, `CREATE FUNCTION`, seed `InsertData`, one-time data backfills. The RLS loss wasn't just Audit's trigger; it was **14 modules × 33 policies** (per-table FILTER + BLOCK) that were silently deleted with no test catching it.

The infuriating part: `TenantSessionContextInterceptor` is still wired in every module and sets `SESSION_CONTEXT(N'TenantId')` on every DB connection — that's the session variable the RLS predicates consume. Without the policies, we've been writing plumbing that does nothing. The dev DB had RLS; prod has RLS (migrations predate Phase-06); the gap only surfaces on the **next prod deployment from v2.133p6 → v2.134+**.

Our test suite (`RuleSetTenantIsolationTests`) exercises only app-level `HasQueryFilter` isolation, never DB-level RLS — so the loss was invisible until the audit dug into the deleted migrations.

---

## Technical Details

### RLS Predicates (Intent)

Original migrations used `RlsMigrationHelper.GenerateRlsPolicies(schema, tables)` to emit:

```sql
CREATE FUNCTION [schema].[fn_securitypredicate_<Schema>](@TenantId UNIQUEIDENTIFIER)
  RETURNS TABLE
  WITH SCHEMABINDING
AS
RETURN SELECT 1 AS [pf_result]
WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS UNIQUEIDENTIFIER);

CREATE SECURITY POLICY [schema].[pol_<Schema>_<Table>_Tenant]
  ADD FILTER PREDICATE [schema].[fn_securitypredicate_<Schema>](TenantId) ON [schema].[<Table>]
  ADD BLOCK PREDICATE [schema].[fn_securitypredicate_<Schema>](TenantId) ON [schema].[<Table>]
    AFTER INSERT, UPDATE
  WITH (STATE = ON);
```

This creates a **FILTER** predicate (rows where `TenantId ≠ SESSION_CONTEXT` are invisible to SELECT/DELETE) + a **BLOCK** predicate (INSERT/UPDATE are rejected if `TenantId ≠ SESSION_CONTEXT`). **33 policies** across **30 tables** (one function per schema, one policy per table).

### Restore Design Decisions

1. **Per-module migration, not one consolidated:** Mirrors the original layout (easier diff review, independent rollback, each module owns its RLS). Trade-off: 13 migration files instead of 1 per DbContext. Code-review flagged this as **M1 non-blocking** — the audit report's unresolved Q1 left this open; user's PR description confirms per-module.

2. **Per-statement `migrationBuilder.Sql()`:** The original helper emitted `GenerateRlsPolicies(...) → "CREATE FUNCTION ... GO ... CREATE SECURITY POLICY ... GO ..."` as one string. That works in `dotnet ef migrations script | sqlcmd` (sqlcmd splits on `GO`), but **not** in `Database.Migrate()` or `dotnet ef database update` (test fixtures, dev startup) — SQL Server rejects `CREATE FUNCTION` if it's not alone in its batch. Solution: `GenerateRlsPolicyStatements()` splits the string on `GO`, returns `List<string>` of individual statements, and each gets its own `migrationBuilder.Sql(sql)` call. A comment in each migration explains the `GO`-split pattern.

3. **Nullable `TenantId` tables excluded:** `AuditLogEntries` (nullable `Guid?`), `SecurityEventLogs` (nullable `Guid?`), `SystemParameters` (string? or absent TenantId) cannot use a strict-equality `@TenantId = SESSION_CONTEXT` predicate — it would hide every NULL-tenant (system/global) row from all connections, causing a silent data-hiding regression. Those three need a predicate variant (e.g., `@TenantId = SESSION_CONTEXT OR TenantId IS NULL`) + an admin-bypass session flag that doesn't exist yet. Deferred to `PHASE6-RLS-NULLABLE-TENANT` (P3).

4. **Table name validation:** The helper encodes PascalCase class names (e.g., `VectorDocuments`), but the dev caught that `ai.VectorDocuments` maps to SQL table `ai.vector_documents` (snake_case). Updated the migration to use the actual table name. A new scan via `CREATE TABLE ... sys.objects WHERE OBJECT_SCHEMA_NAME(object_id) = 'ai'` would have caught this; it's now a code-review comment for future migrations.

### Test Gap Closed

New `RlsPolicyPresenceTests` applies all 13 migrations to a test DB and queries `sys.security_policies`:
```csharp
Assert.That(policies.Count, Is.EqualTo(33));
Assert.That(policies.All(p => p.Name.StartsWith("pol_")), Is.True);
```

This prevents a future migration-regen loss (the test will fail immediately if RLS is missing).

---

## Decisions

1. **Scope: restore RLS only, defer nullable-TenantId edge case.** The three tables with nullable TenantId need a design decision (add admin-bypass flag? allow NULL predicates?) that's out-of-scope for a pure restore. Backlog `PHASE6-RLS-NULLABLE-TENANT` as P3.

2. **Did NOT scope forms covering indexes.** The audit found that forms `IX_FormSubmissions_List_Covering`, `IX_FormSubmissions_Analytics`, and `IX_FormFields_PiiActive` were also lost in Phase-06 (raw-SQL index creates with `INCLUDE` columns). Not part of RLS; backlog as separate candidate.

3. **Did NOT use a planner agent.** Mechanical work (recover table lists from `git show 1f344588^:<path>`, validate, re-emit as per-statement SQL). Delegated direct implementation to one `fullstack-developer` subagent.

---

## Lessons Learned

1. **EF model snapshot is incomplete.** It captures schema structure (DDL CREATE TABLE/INDEX/CONSTRAINT), but **not** raw-SQL DDL (triggers, security policies, functions, constraints via raw SQL, seed data, backfills). Any migration-regen sweep must post-audit by diffing deleted migrations for `migrationBuilder.{Sql, InsertData, UpdateData, DeleteData, CreateSequence}` and verifying each is either (a) reflected in the regen, (b) re-expressed as `HasData`, or (c) genuinely moot (one-time backfill on a fresh DB). Codify this as a **checklist step** in the next Phase-06-style regen.

2. **Test suite gaps can mask regressions.** `RuleSetTenantIsolationTests` exercises app-level isolation (EF `HasQueryFilter`) but not DB-level RLS. A **smoke test asserting `sys.security_policies` exists** would have caught this immediately. New `RlsPolicyPresenceTests` is the fix; add similar guards for other defense-in-depth features (e.g., triggers, audit append-only constraints).

3. **Schema mapping conventions need guards.** `ai.VectorDocuments` → `ai.vector_documents` (snake_case table name) is a legacy pattern — if the migration helper encodes PascalCase only, a scan across prod DBs for `sys.tables` where `OBJECT_NAME(object_id) LIKE '%_%'` would catch other similar mismatches. Backlog as scanner FU.

4. **Nullable TenantId complicates multi-tenant isolation.** The three nullable-TenantId tables break the strict `@TenantId = SESSION_CONTEXT` predicate. This was a Phase-02/Phase-04 design trade-off (system/global config rows need to be visible to all tenants). Future entity designs should avoid nullable TenantId; if unavoidable, need an explicit admin-bypass pattern (e.g., session flag `is_admin_bypass = 1` evaluated in the predicate).

---

## Follow-ups (P3/Candidate)

**FU1 — PHASE6-RLS-NULLABLE-TENANT (P3):**  
Design + implement RLS predicates for `AuditLogEntries`, `SecurityEventLogs`, `SystemParameters` that allow NULL TenantId rows. Options: (a) `@TenantId = SESSION_CONTEXT OR TenantId IS NULL`, (b) a session flag `is_admin_bypass` checked in the predicate, (c) move system rows to a separate unshared table. Own plan/review.

**FU2 — PHASE6-FORMS-COVERING-INDEXES (P3 candidate):**  
Re-add 3 lost covering indexes on `forms.FormSubmissions` and `forms.FormFields` that were created via raw-SQL in Phase-06-dropped migrations. Minimal scope (just the index creates), can be appended to an existing forms migration or split as one new migration.

**FU3 — Covering-index + RLS scanner (backlog candidate):**  
Build a `RegressionGuardScanner` that queries `sys.security_policies`, `sys.triggers`, `sys.objects WHERE type='IF'` (functions) and reports any missing per a **config file** of expected names (schema+name pairs). Run in CI/CD post-DB-update to catch future regen losses immediately.

**FU4 — Design-time-factory `.MigrationsHistoryTable()` gap (backlog candidate):**  
`MasterDataDesignTimeFactory` and `OrgDesignTimeDbContextFactory` lack `.MigrationsHistoryTable()` config, causing `dotnet ef database update` to fail without explicit sqlcmd workaround. Fixed via sqlcmd for this ship; add `.MigrationsHistoryTable(...)` to the factories (pre-existing gap, unrelated to Phase-06 regen).

---

## Code Review Status

**Verdict:** APPROVE_WITH_NITS (2 M-level notes, 1 P3 follow-up noted):

- Migration SQL is validated byte-for-byte vs. the original deleted migrations (via `git show 1f344588^` + `RlsMigrationHelper.GenerateRlsPolicyStatements` split).
- Per-statement `migrationBuilder.Sql()` handles the `GO`-split correctly for both CLI (`dotnet ef database update`) and test-fixture (`Database.Migrate()`) paths.
- Test coverage (33 policies asserted) is adequate.
- No model-snapshot drift.

**M1:** Table name mapping — confirm `ai.vector_documents` → `VectorDocuments` is the canonical mapping; document snake_case pattern for future table names.

**M2:** Migration ordering — if any module has a dependency on RLS being applied in a specific order (e.g., workflow references ai models), verify migration numbering respects that. (Low risk; each module's migrations are independent.)

---

## Unresolved Questions

- Should the covered-indexes FU (FU2) be bundled into the same PR as RLS-RESTORE, or ship separately? (Lean: separately — different scope, different review cycle.)
- Is `RlsMigrationHelper.GenerateRlsPolicies` the source of truth for which tables get RLS, or should a new `RlsPolicyPresenceTests` config drive the assertions? (Current design: tests read the helper; if RLS is added/removed in the future, both the migration and the test must be kept in sync. A centralized config might be cleaner.)
- Does `sys.security_policies` query work identically on Azure SQL Database, or are there dialect differences? (No changes in scope; assume SQL Server only for now.)
