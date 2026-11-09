using AqtCoreFW.Infrastructure.Persistence;
using FluentAssertions;

namespace AqtCoreFW.SharedKernel.Tests.Domain;

/// <summary>
/// Unit tests for RlsMigrationHelper SQL generation.
/// Verifies that generated DDL contains required RLS constructs and
/// that identifier validation rejects unsafe inputs.
/// </summary>
public sealed class RlsMigrationHelperTests
{
    // ── GenerateRlsPolicy ─────────────────────────────────────────────────────

    [Fact]
    public void GenerateRlsPolicy_contains_function_name()
    {
        var sql = RlsMigrationHelper.GenerateRlsPolicy("cases", "Cases");

        sql.Should().Contain("fn_cases_Cases_TenantFilter");
    }

    [Fact]
    public void GenerateRlsPolicy_contains_security_policy_name()
    {
        var sql = RlsMigrationHelper.GenerateRlsPolicy("cases", "Cases");

        sql.Should().Contain("pol_cases_Cases_Tenant");
    }

    [Fact]
    public void GenerateRlsPolicy_contains_session_context_call()
    {
        var sql = RlsMigrationHelper.GenerateRlsPolicy("files", "FileRecords");

        sql.Should().Contain("SESSION_CONTEXT");
        sql.Should().Contain("TenantId");
    }

    [Fact]
    public void GenerateRlsPolicy_contains_filter_and_block_predicates()
    {
        var sql = RlsMigrationHelper.GenerateRlsPolicy("cases", "Cases");

        sql.Should().Contain("FILTER PREDICATE");
        sql.Should().Contain("BLOCK PREDICATE");
        sql.Should().Contain("AFTER INSERT");
    }

    [Fact]
    public void GenerateRlsPolicy_contains_db_owner_bypass()
    {
        // db_owner must bypass RLS so migration tooling can run cross-tenant DDL
        var sql = RlsMigrationHelper.GenerateRlsPolicy("audit", "AuditEntries");

        sql.Should().Contain("db_owner");
        sql.Should().Contain("IS_MEMBER");
    }

    [Fact]
    public void GenerateRlsPolicy_contains_correct_schema_in_sql()
    {
        var sql = RlsMigrationHelper.GenerateRlsPolicy("notifications", "Notifications");

        sql.Should().Contain("[notifications].[fn_notifications_Notifications_TenantFilter]");
        sql.Should().Contain("[notifications].[pol_notifications_Notifications_Tenant]");
        sql.Should().Contain("[notifications].[Notifications]");
    }

    // ── DropRlsPolicy ─────────────────────────────────────────────────────────

    [Fact]
    public void DropRlsPolicy_contains_drop_security_policy()
    {
        var sql = RlsMigrationHelper.DropRlsPolicy("cases", "Cases");

        sql.Should().Contain("DROP SECURITY POLICY");
        sql.Should().Contain("pol_cases_Cases_Tenant");
    }

    [Fact]
    public void DropRlsPolicy_contains_drop_function()
    {
        var sql = RlsMigrationHelper.DropRlsPolicy("cases", "Cases");

        sql.Should().Contain("DROP FUNCTION");
        sql.Should().Contain("fn_cases_Cases_TenantFilter");
    }

    [Fact]
    public void DropRlsPolicy_uses_if_exists()
    {
        var sql = RlsMigrationHelper.DropRlsPolicy("cases", "Cases");

        sql.Should().Contain("IF EXISTS");
    }

    // ── GenerateRlsPolicies (batch) ───────────────────────────────────────────

    [Fact]
    public void GenerateRlsPolicies_generates_all_tables()
    {
        var sql = RlsMigrationHelper.GenerateRlsPolicies("cases", "Cases", "CaseComments", "CaseTasks");

        sql.Should().Contain("fn_cases_Cases_TenantFilter");
        sql.Should().Contain("fn_cases_CaseComments_TenantFilter");
        sql.Should().Contain("fn_cases_CaseTasks_TenantFilter");
    }

    // ── Identifier validation ─────────────────────────────────────────────────

    [Theory]
    [InlineData("cases; DROP TABLE users--")]
    [InlineData("cases' OR '1'='1")]
    [InlineData("cases/*comment*/")]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateRlsPolicy_throws_on_invalid_schema(string badSchema)
    {
        var act = () => RlsMigrationHelper.GenerateRlsPolicy(badSchema, "Cases");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Cases; DROP TABLE users--")]
    [InlineData("Cases' OR '1'='1")]
    [InlineData("")]
    public void GenerateRlsPolicy_throws_on_invalid_table(string badTable)
    {
        var act = () => RlsMigrationHelper.GenerateRlsPolicy("cases", badTable);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerateRlsPolicy_allows_underscores_and_alphanumeric()
    {
        // Should not throw for valid identifiers
        var act = () => RlsMigrationHelper.GenerateRlsPolicy("my_schema", "My_Table123");

        act.Should().NotThrow();
    }
}
