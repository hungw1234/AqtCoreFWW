namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// Partial class — nullable / string-typed TenantId variants.
///
/// Use these instead of <see cref="GenerateRlsPolicyStatements"/> when the table's TenantId is:
/// <list type="bullet">
///   <item><see cref="GenerateAuditAdminBypassRlsPolicyStatements"/> — <c>uniqueidentifier?</c>
///     (NULL = system/cross-tenant event); requires an <c>audit_admin_bypass</c> session-context
///     flag so SystemAdmin readers can still see NULL-tenant rows.</item>
///   <item><see cref="GenerateSystemParamRlsPolicyStatements"/> — <c>nvarchar(100)</c>
///     (NULL = global/system parameter visible to all tenants); uses a symmetric single-function
///     policy: FILTER + BLOCK-AFTER-INSERT + BLOCK-AFTER-UPDATE reuse the same TVF.</item>
/// </list>
///
/// Both sets of iterators are GO-free — every yielded string is one self-contained batch
/// suitable for a single <c>migrationBuilder.Sql()</c> call.
/// </summary>
public static partial class RlsMigrationHelper
{
    // ── Audit tables — nullable uniqueidentifier TenantId ─────────────────────
    //
    // Audit events can be system-wide (TenantId = NULL) or tenant-scoped (TenantId = guid).
    // A strict-equality predicate would hide system events from tenant readers and from admins
    // (when admin TenantId is also null). The asymmetric design below solves both problems:
    //
    // FILTER function  : exposes NULL-tenant rows ONLY when the caller sets
    //                    SESSION_CONTEXT(N'audit_admin_bypass') = 'true'.
    //                    Tenant-scoped callers never set this flag → cannot see other tenants.
    // BLOCK function   : lets the server-side audit pipeline INSERT NULL-tenant rows
    //                    without setting any special context (writers are internal only).
    //                    Tenant-tagged INSERTs still require the row TenantId to match session.

    /// <summary>
    /// Yields GO-free SQL statements for setting up an asymmetric RLS policy on each audit table.
    /// Three batches per table: FILTER function, BLOCK function, CREATE SECURITY POLICY.
    /// </summary>
    /// <param name="schemaName">DB schema (e.g. "audit").</param>
    /// <param name="tableNames">Table names without schema prefix.</param>
    public static IEnumerable<string> GenerateAuditAdminBypassRlsPolicyStatements(
        string schemaName, params string[] tableNames)
    {
        foreach (var tableName in tableNames)
        {
            ValidateIdentifier(schemaName);
            ValidateIdentifier(tableName);

            var filterFnName  = $"fn_{schemaName}_{tableName}_TenantFilter";
            var blockFnName   = $"fn_{schemaName}_{tableName}_TenantInsertGuard";
            var policyName    = $"pol_{schemaName}_{tableName}_Tenant";

            // Batch 1: FILTER predicate function (must be the only statement in its batch)
            // Exposes NULL-tenant rows only when audit_admin_bypass='true' in session context.
            // Non-admin tenant callers never set audit_admin_bypass → system events remain hidden.
            yield return $"""
                CREATE OR ALTER FUNCTION [{schemaName}].[{filterFnName}](@TenantId uniqueidentifier)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN
                (
                    SELECT 1 AS fn_result
                    WHERE
                        -- Allow db_owner / sa (migration tooling, admin batch jobs)
                        IS_MEMBER('db_owner') = 1
                        OR
                        -- Match session TenantId set by TenantSessionContextInterceptor
                        @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
                        OR
                        -- Allow SystemAdmin handlers that set audit_admin_bypass='true' before SELECT
                        -- CAUTION: any caller that sets this flag can read all NULL-tenant audit rows.
                        -- @TenantId IS NULL means the row's bound column value is NULL (system event).
                        (@TenantId IS NULL AND TRY_CAST(SESSION_CONTEXT(N'audit_admin_bypass') AS bit) = 1)
                )
                """;

            // Batch 2: BLOCK predicate function (asymmetric — different from FILTER fn)
            // Allows NULL-tenant INSERTs unconditionally: audit pipeline writers are server-side
            // and never set a tenant session context; blocking them would silently drop audit events.
            // Tenant-tagged rows still require the insert row TenantId to match session context.
            yield return $"""
                CREATE OR ALTER FUNCTION [{schemaName}].[{blockFnName}](@TenantId uniqueidentifier)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN
                (
                    SELECT 1 AS fn_result
                    WHERE
                        -- Allow db_owner / sa (migration tooling, admin batch jobs)
                        IS_MEMBER('db_owner') = 1
                        OR
                        -- Match session TenantId for tenant-tagged events
                        @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
                        OR
                        -- Allow NULL-tenant rows — audit pipeline writers are server-side and
                        -- never set a session tenant context; we cannot block them here.
                        @TenantId IS NULL
                )
                """;

            // Batch 3: security policy combining both predicates
            yield return $"""
                CREATE SECURITY POLICY [{schemaName}].[{policyName}]
                ADD FILTER PREDICATE [{schemaName}].[{filterFnName}](TenantId)
                    ON [{schemaName}].[{tableName}],
                ADD BLOCK PREDICATE [{schemaName}].[{blockFnName}](TenantId)
                    ON [{schemaName}].[{tableName}] AFTER INSERT
                WITH (STATE = ON, SCHEMABINDING = ON)
                """;
        }
    }

    /// <summary>
    /// Yields GO-free DROP statements for removing asymmetric audit RLS from each table.
    /// Order: DROP SECURITY POLICY first (references both functions), then DROP each function.
    /// </summary>
    /// <param name="schemaName">DB schema.</param>
    /// <param name="tableNames">Table names without schema prefix.</param>
    public static IEnumerable<string> DropAuditAdminBypassRlsPolicyStatements(
        string schemaName, params string[] tableNames)
    {
        foreach (var tableName in tableNames)
        {
            ValidateIdentifier(schemaName);
            ValidateIdentifier(tableName);

            var filterFnName = $"fn_{schemaName}_{tableName}_TenantFilter";
            var blockFnName  = $"fn_{schemaName}_{tableName}_TenantInsertGuard";
            var policyName   = $"pol_{schemaName}_{tableName}_Tenant";

            // Drop policy first — it references both functions; dropping a function while the
            // policy exists would fail with "object is referenced by a security policy" error.
            yield return $"DROP SECURITY POLICY IF EXISTS [{schemaName}].[{policyName}]";

            yield return $"DROP FUNCTION IF EXISTS [{schemaName}].[{blockFnName}]";

            yield return $"DROP FUNCTION IF EXISTS [{schemaName}].[{filterFnName}]";
        }
    }

    // ── SystemParameters — string-typed (nvarchar(100)) TenantId ──────────────
    //
    // SystemParameters rows can be:
    //   TenantId = NULL       → global/system parameter visible to ALL tenants (read)
    //   TenantId = <string>   → tenant-specific override, visible only to that tenant
    //
    // Symmetric design: one TVF serves FILTER, BLOCK-AFTER-INSERT, and BLOCK-AFTER-UPDATE.
    // NULL-tenant INSERTs/UPDATEs are allowed so the SystemParamSeeder (server-side, no tenant
    // session set) can write global rows without needing a db_owner connection.
    // SESSION_CONTEXT returns sql_variant; implicit cast to nvarchar(100) is safe here.

    /// <summary>
    /// Yields GO-free SQL statements for setting up symmetric RLS on a SystemParameters table.
    /// Two batches: CREATE OR ALTER FUNCTION (nvarchar(100) param), then CREATE SECURITY POLICY
    /// with FILTER + BLOCK-AFTER-INSERT + BLOCK-AFTER-UPDATE (all reusing the same function).
    /// </summary>
    /// <param name="schemaName">DB schema (e.g. "config").</param>
    /// <param name="tableName">Table name without schema prefix (e.g. "SystemParameters").</param>
    public static IEnumerable<string> GenerateSystemParamRlsPolicyStatements(
        string schemaName, string tableName)
    {
        ValidateIdentifier(schemaName);
        ValidateIdentifier(tableName);

        var fnName     = $"fn_{schemaName}_{tableName}_TenantFilter";
        var policyName = $"pol_{schemaName}_{tableName}_Tenant";

        // Batch 1: predicate function with nvarchar(100) param (matches SystemParameters column type)
        // SESSION_CONTEXT returns sql_variant; CAST to nvarchar(100) for comparison.
        // NULL-tenant rows: visible to all tenants (global parameters are shared infrastructure).
        yield return $"""
            CREATE OR ALTER FUNCTION [{schemaName}].[{fnName}](@TenantId nvarchar(100))
            RETURNS TABLE
            WITH SCHEMABINDING
            AS
            RETURN
            (
                SELECT 1 AS fn_result
                WHERE
                    -- Allow db_owner / sa (migration tooling, SystemParamSeeder startup)
                    IS_MEMBER('db_owner') = 1
                    OR
                    -- Match session TenantId (nvarchar comparison; SESSION_CONTEXT implicit-casts ok)
                    @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS nvarchar(100))
                    OR
                    -- Global parameters (TenantId IS NULL) are visible to all tenants
                    @TenantId IS NULL
            )
            """;

        // Batch 2: security policy — same function reused for FILTER + both BLOCK predicates.
        // BLOCK-AFTER-INSERT + BLOCK-AFTER-UPDATE: NULL-tenant rows are allowed (seeder writes
        // global parameters at startup without a session tenant context set).
        yield return $"""
            CREATE SECURITY POLICY [{schemaName}].[{policyName}]
            ADD FILTER PREDICATE [{schemaName}].[{fnName}](TenantId)
                ON [{schemaName}].[{tableName}],
            ADD BLOCK PREDICATE [{schemaName}].[{fnName}](TenantId)
                ON [{schemaName}].[{tableName}] AFTER INSERT,
            ADD BLOCK PREDICATE [{schemaName}].[{fnName}](TenantId)
                ON [{schemaName}].[{tableName}] AFTER UPDATE
            WITH (STATE = ON, SCHEMABINDING = ON)
            """;
    }

    /// <summary>
    /// Yields GO-free DROP statements for removing symmetric SystemParams RLS.
    /// Order: DROP SECURITY POLICY first, then DROP FUNCTION.
    /// </summary>
    /// <param name="schemaName">DB schema.</param>
    /// <param name="tableName">Table name without schema prefix.</param>
    public static IEnumerable<string> DropSystemParamRlsPolicyStatements(
        string schemaName, string tableName)
    {
        ValidateIdentifier(schemaName);
        ValidateIdentifier(tableName);

        var fnName     = $"fn_{schemaName}_{tableName}_TenantFilter";
        var policyName = $"pol_{schemaName}_{tableName}_Tenant";

        // Drop policy first — it references the function; order matters.
        yield return $"DROP SECURITY POLICY IF EXISTS [{schemaName}].[{policyName}]";

        yield return $"DROP FUNCTION IF EXISTS [{schemaName}].[{fnName}]";
    }
}
