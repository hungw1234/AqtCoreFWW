using AqtCoreFW.Infrastructure.Persistence;
using FluentAssertions;

namespace AqtCoreFW.SharedKernel.Tests.Domain;

/// <summary>
/// Unit tests for the nullable / string-typed TenantId RLS variants in RlsMigrationHelper.
///
/// Key security invariants pinned here:
///   - "audit_admin_bypass" exact spelling — a typo silently breaks admin reads of system audit events.
///   - BLOCK fn must NOT require audit_admin_bypass (server-side writers never set it).
///   - SystemParams fn uses nvarchar(100) (matches config.SystemParameters.TenantId column type).
///   - No GO separator in any yielded string (Database.Migrate() does not split on GO).
///   - Policy is dropped BEFORE its referenced functions in Drop iterators.
/// </summary>
public sealed class RlsMigrationHelperNullableTests
{
    // ── GenerateAuditAdminBypassRlsPolicyStatements ───────────────────────────

    [Fact]
    public void AuditBypass_yields_three_statements_per_table()
    {
        var statements = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .ToList();

        statements.Should().HaveCount(3);
    }

    [Fact]
    public void AuditBypass_yields_six_statements_for_two_tables()
    {
        var statements = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries", "SecurityEventLogs")
            .ToList();

        statements.Should().HaveCount(6);
    }

    [Fact]
    public void AuditBypass_filter_fn_contains_audit_admin_bypass_exact_spelling()
    {
        // EXACT spelling "audit_admin_bypass" is a security invariant — typo = silent broken admin reads
        var filterFn = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .First();

        filterFn.Should().Contain("audit_admin_bypass",
            because: "typo in session context key name silently breaks SystemAdmin audit reads");
    }

    [Fact]
    public void AuditBypass_filter_fn_contains_try_cast_audit_admin_bypass_as_bit()
    {
        var filterFn = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .First();

        filterFn.Should().Contain("TRY_CAST(SESSION_CONTEXT(N'audit_admin_bypass') AS bit)");
    }

    [Fact]
    public void AuditBypass_filter_fn_has_correct_name()
    {
        var filterFn = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .First();

        filterFn.Should().Contain("fn_audit_AuditLogEntries_TenantFilter");
    }

    [Fact]
    public void AuditBypass_filter_fn_has_uniqueidentifier_param()
    {
        var filterFn = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .First();

        filterFn.Should().Contain("@TenantId uniqueidentifier");
    }

    [Fact]
    public void AuditBypass_block_fn_does_not_contain_audit_admin_bypass()
    {
        // BLOCK fn is used by server-side writers who never set audit_admin_bypass.
        // If the BLOCK fn required audit_admin_bypass, audit pipeline INSERTs would be silently rejected.
        var blockFn = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .Skip(1).First();

        blockFn.Should().NotContain("audit_admin_bypass",
            because: "server-side audit writers never set this flag; requiring it would silently block INSERTs");
    }

    [Fact]
    public void AuditBypass_block_fn_contains_tenant_id_is_null()
    {
        // NULL-tenant INSERTs must be allowed — audit pipeline writes system/cross-tenant events
        var blockFn = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .Skip(1).First();

        blockFn.Should().Contain("@TenantId IS NULL",
            because: "audit pipeline inserts NULL-tenant system events and must not be blocked");
    }

    [Fact]
    public void AuditBypass_block_fn_has_correct_name()
    {
        var blockFn = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .Skip(1).First();

        blockFn.Should().Contain("fn_audit_AuditLogEntries_TenantInsertGuard");
    }

    [Fact]
    public void AuditBypass_policy_references_both_functions()
    {
        var policy = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .Skip(2).First();

        policy.Should().Contain("fn_audit_AuditLogEntries_TenantFilter");
        policy.Should().Contain("fn_audit_AuditLogEntries_TenantInsertGuard");
    }

    [Fact]
    public void AuditBypass_policy_has_correct_name()
    {
        var policy = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .Skip(2).First();

        policy.Should().Contain("pol_audit_AuditLogEntries_Tenant");
    }

    [Fact]
    public void AuditBypass_policy_has_filter_and_block_after_insert()
    {
        var policy = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .Skip(2).First();

        policy.Should().Contain("FILTER PREDICATE");
        policy.Should().Contain("BLOCK PREDICATE");
        policy.Should().Contain("AFTER INSERT");
    }

    [Fact]
    public void AuditBypass_no_statement_contains_go_separator()
    {
        // Database.Migrate() does NOT split on GO — any GO in a yielded string breaks the batch
        var statements = RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries", "SecurityEventLogs")
            .ToList();

        foreach (var sql in statements)
        {
            // Check for standalone GO on its own line (case-insensitive)
            var lines = sql.Split('\n').Select(l => l.Trim());
            lines.Should().NotContain("GO",
                because: $"GO is a sqlcmd batch separator and must not appear in migrationBuilder.Sql() strings");
            lines.Should().NotContain("go");
        }
    }

    [Fact]
    public void AuditBypass_identifier_validation_rejects_invalid_schema()
    {
        var act = () => RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit; DROP TABLE--", "AuditLogEntries")
            .ToList();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AuditBypass_identifier_validation_rejects_invalid_table()
    {
        var act = () => RlsMigrationHelper
            .GenerateAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries; DROP--")
            .ToList();

        act.Should().Throw<ArgumentException>();
    }

    // ── DropAuditAdminBypassRlsPolicyStatements ───────────────────────────────

    [Fact]
    public void DropAuditBypass_yields_three_statements_per_table()
    {
        var statements = RlsMigrationHelper
            .DropAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .ToList();

        statements.Should().HaveCount(3);
    }

    [Fact]
    public void DropAuditBypass_policy_drop_comes_before_function_drops()
    {
        // Policy references both functions; dropping a function while policy exists = SQL error.
        var statements = RlsMigrationHelper
            .DropAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .ToList();

        var policyDropIndex = statements.FindIndex(s => s.Contains("DROP SECURITY POLICY"));
        var fnDropIndex     = statements.FindIndex(s => s.Contains("DROP FUNCTION"));

        policyDropIndex.Should().BeLessThan(fnDropIndex,
            because: "security policy must be dropped before its referenced functions");
    }

    [Fact]
    public void DropAuditBypass_drops_both_functions()
    {
        var statements = RlsMigrationHelper
            .DropAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .ToList();

        statements.Should().Contain(s => s.Contains("fn_audit_AuditLogEntries_TenantFilter"));
        statements.Should().Contain(s => s.Contains("fn_audit_AuditLogEntries_TenantInsertGuard"));
    }

    [Fact]
    public void DropAuditBypass_uses_if_exists()
    {
        var statements = RlsMigrationHelper
            .DropAuditAdminBypassRlsPolicyStatements("audit", "AuditLogEntries")
            .ToList();

        statements.Should().AllSatisfy(s => s.Should().Contain("IF EXISTS"));
    }

    // ── GenerateSystemParamRlsPolicyStatements ────────────────────────────────

    [Fact]
    public void SystemParam_yields_two_statements()
    {
        var statements = RlsMigrationHelper
            .GenerateSystemParamRlsPolicyStatements("config", "SystemParameters")
            .ToList();

        statements.Should().HaveCount(2);
    }

    [Fact]
    public void SystemParam_fn_contains_nvarchar100_param()
    {
        // SystemParameters.TenantId is nvarchar(100) — must NOT use uniqueidentifier
        var fn = RlsMigrationHelper
            .GenerateSystemParamRlsPolicyStatements("config", "SystemParameters")
            .First();

        fn.Should().Contain("nvarchar(100)",
            because: "config.SystemParameters.TenantId column is nvarchar(100), not uniqueidentifier");
    }

    [Fact]
    public void SystemParam_fn_has_correct_name()
    {
        var fn = RlsMigrationHelper
            .GenerateSystemParamRlsPolicyStatements("config", "SystemParameters")
            .First();

        fn.Should().Contain("fn_config_SystemParameters_TenantFilter");
    }

    [Fact]
    public void SystemParam_fn_allows_null_tenant_rows()
    {
        // NULL-tenant = global parameter, must be visible to all tenants
        var fn = RlsMigrationHelper
            .GenerateSystemParamRlsPolicyStatements("config", "SystemParameters")
            .First();

        fn.Should().Contain("@TenantId IS NULL",
            because: "global parameters (TenantId=NULL) must be readable by all tenants");
    }

    [Fact]
    public void SystemParam_fn_does_not_use_uniqueidentifier()
    {
        var fn = RlsMigrationHelper
            .GenerateSystemParamRlsPolicyStatements("config", "SystemParameters")
            .First();

        fn.Should().NotContain("uniqueidentifier",
            because: "SystemParameters.TenantId is nvarchar(100) not uniqueidentifier");
    }

    [Fact]
    public void SystemParam_policy_has_filter_and_block_after_insert_and_after_update()
    {
        // Symmetric: same fn reused for FILTER + BLOCK INSERT + BLOCK UPDATE
        var policy = RlsMigrationHelper
            .GenerateSystemParamRlsPolicyStatements("config", "SystemParameters")
            .Skip(1).First();

        policy.Should().Contain("FILTER PREDICATE");
        policy.Should().Contain("AFTER INSERT");
        policy.Should().Contain("AFTER UPDATE");
    }

    [Fact]
    public void SystemParam_policy_has_correct_name()
    {
        var policy = RlsMigrationHelper
            .GenerateSystemParamRlsPolicyStatements("config", "SystemParameters")
            .Skip(1).First();

        policy.Should().Contain("pol_config_SystemParameters_Tenant");
    }

    [Fact]
    public void SystemParam_no_statement_contains_go_separator()
    {
        var statements = RlsMigrationHelper
            .GenerateSystemParamRlsPolicyStatements("config", "SystemParameters")
            .ToList();

        foreach (var sql in statements)
        {
            var lines = sql.Split('\n').Select(l => l.Trim());
            lines.Should().NotContain("GO",
                because: "GO is a sqlcmd batch separator and must not appear in migrationBuilder.Sql() strings");
            lines.Should().NotContain("go");
        }
    }

    [Fact]
    public void SystemParam_identifier_validation_rejects_invalid_schema()
    {
        var act = () => RlsMigrationHelper
            .GenerateSystemParamRlsPolicyStatements("config; DROP--", "SystemParameters")
            .ToList();

        act.Should().Throw<ArgumentException>();
    }

    // ── DropSystemParamRlsPolicyStatements ────────────────────────────────────

    [Fact]
    public void DropSystemParam_yields_two_statements()
    {
        var statements = RlsMigrationHelper
            .DropSystemParamRlsPolicyStatements("config", "SystemParameters")
            .ToList();

        statements.Should().HaveCount(2);
    }

    [Fact]
    public void DropSystemParam_policy_drop_comes_before_function_drop()
    {
        var statements = RlsMigrationHelper
            .DropSystemParamRlsPolicyStatements("config", "SystemParameters")
            .ToList();

        var policyDropIndex = statements.FindIndex(s => s.Contains("DROP SECURITY POLICY"));
        var fnDropIndex     = statements.FindIndex(s => s.Contains("DROP FUNCTION"));

        policyDropIndex.Should().BeLessThan(fnDropIndex,
            because: "security policy must be dropped before its referenced function");
    }

    [Fact]
    public void DropSystemParam_drops_correct_objects()
    {
        var statements = RlsMigrationHelper
            .DropSystemParamRlsPolicyStatements("config", "SystemParameters")
            .ToList();

        statements.Should().Contain(s => s.Contains("pol_config_SystemParameters_Tenant"));
        statements.Should().Contain(s => s.Contains("fn_config_SystemParameters_TenantFilter"));
    }

    [Fact]
    public void DropSystemParam_uses_if_exists()
    {
        var statements = RlsMigrationHelper
            .DropSystemParamRlsPolicyStatements("config", "SystemParameters")
            .ToList();

        statements.Should().AllSatisfy(s => s.Should().Contain("IF EXISTS"));
    }
}
