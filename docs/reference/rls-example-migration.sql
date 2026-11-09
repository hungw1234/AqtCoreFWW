-- =============================================================================
-- RLS Example Migration Pattern — AqtCoreFW
-- =============================================================================
-- PURPOSE: Demonstrates how to apply SQL Server Row-Level Security to module
--          tables. Copy this pattern into your EF migration's Up()/Down() via:
--
--   migrationBuilder.Sql(RlsMigrationHelper.GenerateRlsPolicy("cases", "Cases"));
--
-- PREREQUISITES:
--   1. TenantSessionContextInterceptor registered in DbContextOptions
--   2. tenant_id column is NVARCHAR(36) on every tenant-scoped table
--   3. App DB user must NOT be db_owner (RLS is bypassed for db_owner)
--   4. Separate db_owner login used only for migrations/admin jobs
-- =============================================================================


-- ── CASES MODULE ─────────────────────────────────────────────────────────────

-- 1. Predicate function for cases.Cases
CREATE OR ALTER FUNCTION [cases].[fn_cases_Cases_TenantFilter](@tenant_id NVARCHAR(36))
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
(
    SELECT 1 AS fn_result
    WHERE
        IS_MEMBER('db_owner') = 1
        OR
        @tenant_id = CAST(SESSION_CONTEXT(N'TenantId') AS NVARCHAR(36))
);
GO

-- 2. Security policy: filter (SELECT) + block (INSERT)
CREATE SECURITY POLICY [cases].[pol_cases_Cases_Tenant]
ADD FILTER PREDICATE [cases].[fn_cases_Cases_TenantFilter](tenant_id)
    ON [cases].[Cases],
ADD BLOCK PREDICATE [cases].[fn_cases_Cases_TenantFilter](tenant_id)
    ON [cases].[Cases] AFTER INSERT
WITH (STATE = ON, SCHEMABINDING = ON);
GO


-- ── FILES MODULE ──────────────────────────────────────────────────────────────

CREATE OR ALTER FUNCTION [files].[fn_files_FileRecords_TenantFilter](@tenant_id NVARCHAR(36))
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
(
    SELECT 1 AS fn_result
    WHERE
        IS_MEMBER('db_owner') = 1
        OR
        @tenant_id = CAST(SESSION_CONTEXT(N'TenantId') AS NVARCHAR(36))
);
GO

CREATE SECURITY POLICY [files].[pol_files_FileRecords_Tenant]
ADD FILTER PREDICATE [files].[fn_files_FileRecords_TenantFilter](tenant_id)
    ON [files].[FileRecords],
ADD BLOCK PREDICATE [files].[fn_files_FileRecords_TenantFilter](tenant_id)
    ON [files].[FileRecords] AFTER INSERT
WITH (STATE = ON, SCHEMABINDING = ON);
GO


-- ── AUDIT MODULE ──────────────────────────────────────────────────────────────
-- NOTE: Audit entries are append-only. Only FILTER predicate needed (no INSERT block
--       since audit writer uses db_owner login that bypasses RLS anyway).

CREATE OR ALTER FUNCTION [audit].[fn_audit_AuditEntries_TenantFilter](@tenant_id NVARCHAR(36))
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
(
    SELECT 1 AS fn_result
    WHERE
        IS_MEMBER('db_owner') = 1
        OR
        @tenant_id = CAST(SESSION_CONTEXT(N'TenantId') AS NVARCHAR(36))
);
GO

CREATE SECURITY POLICY [audit].[pol_audit_AuditEntries_Tenant]
ADD FILTER PREDICATE [audit].[fn_audit_AuditEntries_TenantFilter](tenant_id)
    ON [audit].[AuditEntries]
WITH (STATE = ON, SCHEMABINDING = ON);
GO


-- =============================================================================
-- ROLLBACK (Down migration)
-- =============================================================================

-- Cases
DROP SECURITY POLICY IF EXISTS [cases].[pol_cases_Cases_Tenant];
DROP FUNCTION  IF EXISTS [cases].[fn_cases_Cases_TenantFilter];

-- Files
DROP SECURITY POLICY IF EXISTS [files].[pol_files_FileRecords_Tenant];
DROP FUNCTION  IF EXISTS [files].[fn_files_FileRecords_TenantFilter];

-- Audit
DROP SECURITY POLICY IF EXISTS [audit].[pol_audit_AuditEntries_Tenant];
DROP FUNCTION  IF EXISTS [audit].[fn_audit_AuditEntries_TenantFilter];
GO
