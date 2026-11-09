namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// Static helper for generating SQL Server Row-Level Security (RLS) DDL.
///
/// Standard usage (non-nullable uniqueidentifier TenantId):
///   migrationBuilder.Sql(RlsMigrationHelper.GenerateRlsPolicy("cases", "Cases"));
///   migrationBuilder.Sql(RlsMigrationHelper.DropRlsPolicy("cases", "Cases")); // Down()
///
/// For Database.Migrate() / runtime apply (test fixtures, dev startup), use the GO-free
/// statement variants — each call to migrationBuilder.Sql() becomes its own batch:
///   foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements("identity", "CredentialPolicies"))
///       migrationBuilder.Sql(sql);
///
/// Nullable / string-typed TenantId variants (see <c>RlsMigrationHelper.NullableTenant.cs</c>):
///   <list type="bullet">
///     <item><see cref="GenerateAuditAdminBypassRlsPolicyStatements"/> — for tables with nullable
///       <c>uniqueidentifier</c> TenantId (audit tables). Uses an asymmetric two-function policy:
///       FILTER fn requires <c>audit_admin_bypass</c> session flag to expose NULL-tenant rows;
///       BLOCK fn permits NULL-tenant INSERTs (server-side audit pipeline writers).</item>
///     <item><see cref="GenerateSystemParamRlsPolicyStatements"/> — for tables with
///       <c>nvarchar(100)</c> TenantId (SystemParameters). Uses a single symmetric TVF for
///       FILTER + BLOCK-AFTER-INSERT + BLOCK-AFTER-UPDATE; NULL-tenant rows are global
///       parameters visible to all tenants.</item>
///   </list>
///
/// How it works:
///   1. fn_TenantFilter reads SESSION_CONTEXT(N'TenantId') set by TenantSessionContextInterceptor.
///   2. Security policy filters SELECT/INSERT — blocks cross-tenant reads at DB engine level.
///   3. Works alongside EF global query filters (defense in depth).
///
/// Requirements:
///   - sp_set_session_context must be called before any query (done by TenantSessionContextInterceptor).
///   - Standard column type: 'TenantId' of type uniqueidentifier (EF default Guid mapping).
///   - Admin operations needing cross-tenant access must connect via a db_owner login.
/// </summary>
public static partial class RlsMigrationHelper
{
    /// <summary>
    /// Generates the full RLS setup SQL for one table:
    ///   1. Predicate function fn_{schema}_{table}_TenantFilter
    ///   2. Security policy pol_{schema}_{table}_Tenant
    /// </summary>
    /// <param name="schemaName">DB schema (e.g. "cases", "files", "audit").</param>
    /// <param name="tableName">Table name without schema (e.g. "Cases", "FileRecords").</param>
    public static string GenerateRlsPolicy(string schemaName, string tableName)
    {
        ValidateIdentifier(schemaName);
        ValidateIdentifier(tableName);

        var fnName = $"fn_{schemaName}_{tableName}_TenantFilter";
        var policyName = $"pol_{schemaName}_{tableName}_Tenant";

        return $"""
            -- ── RLS for [{schemaName}].[{tableName}] ──────────────────────────────────────
            -- Predicate function: returns 1 when row's TenantId matches session context
            CREATE OR ALTER FUNCTION [{schemaName}].[{fnName}](@TenantId uniqueidentifier)
            RETURNS TABLE
            WITH SCHEMABINDING
            AS
            RETURN
            (
                SELECT 1 AS fn_result
                WHERE
                    -- Allow db_owner / sa (migration tooling, admin jobs)
                    IS_MEMBER('db_owner') = 1
                    OR
                    -- Match session context set by TenantSessionContextInterceptor
                    @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
            );
            GO

            -- Security policy: filter predicate (SELECT) + block predicate (INSERT)
            CREATE SECURITY POLICY [{schemaName}].[{policyName}]
            ADD FILTER PREDICATE [{schemaName}].[{fnName}](TenantId)
                ON [{schemaName}].[{tableName}],
            ADD BLOCK PREDICATE [{schemaName}].[{fnName}](TenantId)
                ON [{schemaName}].[{tableName}] AFTER INSERT
            WITH (STATE = ON, SCHEMABINDING = ON);
            GO
            """;
    }

    /// <summary>
    /// Generates DROP statements to remove RLS from a table (use in migration Down()).
    /// </summary>
    public static string DropRlsPolicy(string schemaName, string tableName)
    {
        ValidateIdentifier(schemaName);
        ValidateIdentifier(tableName);

        var fnName = $"fn_{schemaName}_{tableName}_TenantFilter";
        var policyName = $"pol_{schemaName}_{tableName}_Tenant";

        return $"""
            -- ── Remove RLS for [{schemaName}].[{tableName}] ──────────────────────────────
            DROP SECURITY POLICY IF EXISTS [{schemaName}].[{policyName}];
            GO
            DROP FUNCTION IF EXISTS [{schemaName}].[{fnName}];
            GO
            """;
    }

    /// <summary>
    /// Generates RLS policies for multiple tables in the same schema in one migration block.
    /// </summary>
    public static string GenerateRlsPolicies(string schemaName, params string[] tableNames)
    {
        var parts = tableNames.Select(t => GenerateRlsPolicy(schemaName, t));
        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    // ── GO-free statement variants (for Database.Migrate() / runtime apply) ──
    //
    // migrationBuilder.Sql(oneString) → one SqlOperation → one batch.
    // GO is a sqlcmd/SSMS batch separator — not T-SQL. Database.Migrate() does NOT split on GO.
    // CREATE FUNCTION must be the only statement in its batch → each statement must be its
    // own migrationBuilder.Sql() call. These iterators yield one SQL string per batch.

    /// <summary>
    /// Yields GO-free SQL statements for setting up RLS on each table (two per table:
    /// CREATE OR ALTER FUNCTION, then CREATE SECURITY POLICY). Intended for use with
    /// <c>Database.Migrate()</c> — pass each yielded string to a separate
    /// <c>migrationBuilder.Sql()</c> call so each becomes its own execution batch.
    /// </summary>
    /// <param name="schemaName">DB schema (e.g. "identity", "files").</param>
    /// <param name="tableNames">Table names without schema prefix.</param>
    public static IEnumerable<string> GenerateRlsPolicyStatements(
        string schemaName, params string[] tableNames)
    {
        foreach (var tableName in tableNames)
        {
            ValidateIdentifier(schemaName);
            ValidateIdentifier(tableName);

            var fnName = $"fn_{schemaName}_{tableName}_TenantFilter";
            var policyName = $"pol_{schemaName}_{tableName}_Tenant";

            // Batch 1: predicate function (must be the only statement in its batch)
            yield return $"""
                CREATE OR ALTER FUNCTION [{schemaName}].[{fnName}](@TenantId uniqueidentifier)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN
                (
                    SELECT 1 AS fn_result
                    WHERE
                        IS_MEMBER('db_owner') = 1
                        OR
                        @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
                )
                """;

            // Batch 2: security policy (references the function created in batch 1)
            yield return $"""
                CREATE SECURITY POLICY [{schemaName}].[{policyName}]
                ADD FILTER PREDICATE [{schemaName}].[{fnName}](TenantId)
                    ON [{schemaName}].[{tableName}],
                ADD BLOCK PREDICATE [{schemaName}].[{fnName}](TenantId)
                    ON [{schemaName}].[{tableName}] AFTER INSERT
                WITH (STATE = ON, SCHEMABINDING = ON)
                """;
        }
    }

    /// <summary>
    /// Yields GO-free SQL statements for removing RLS from each table (two per table:
    /// DROP SECURITY POLICY, then DROP FUNCTION). Intended for migration Down() bodies
    /// using <c>Database.Migrate()</c> — pass each yielded string to a separate
    /// <c>migrationBuilder.Sql()</c> call.
    /// </summary>
    /// <param name="schemaName">DB schema.</param>
    /// <param name="tableNames">Table names without schema prefix.</param>
    public static IEnumerable<string> DropRlsPolicyStatements(
        string schemaName, params string[] tableNames)
    {
        foreach (var tableName in tableNames)
        {
            ValidateIdentifier(schemaName);
            ValidateIdentifier(tableName);

            var fnName = $"fn_{schemaName}_{tableName}_TenantFilter";
            var policyName = $"pol_{schemaName}_{tableName}_Tenant";

            // Drop policy first (it references the function — function can't be dropped while policy exists)
            yield return $"DROP SECURITY POLICY IF EXISTS [{schemaName}].[{policyName}]";

            yield return $"DROP FUNCTION IF EXISTS [{schemaName}].[{fnName}]";
        }
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private static void ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Identifier must not be empty.");

        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new ArgumentException(
                    $"Invalid identifier character '{c}' in '{value}'. Only letters, digits, underscores allowed.");
        }
    }
}
