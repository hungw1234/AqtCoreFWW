using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// RLS enforcement for <c>forms.FormSubmissionWorkflowLinkPendingAttempts</c> — the tenant policy added
/// by <c>AddWorkflowLinkPendingAttemptRls</c> (the table landed after the Phase-06 RLS restore and was
/// missing a policy). Runs against a NON-db_owner constrained login (`ConstrainedConnectionString`), so
/// the FILTER/BLOCK predicates actually apply — a db_owner (sa) connection would bypass them and
/// false-green (see feedback-sa-dbowner-false-greens-rls).
///
/// The table has only a PK (no FK constraints), so rows can be inserted with arbitrary Guids without
/// seeding FormSubmissions/FormTemplates.
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class FormWorkflowLinkPendingRlsEnforcementTests
{
    private static readonly Guid TenantA = new("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
    private static readonly Guid TenantB = new("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2");

    private readonly RlsPolicyPresenceFixture _fixture;

    public FormWorkflowLinkPendingRlsEnforcementTests(RlsPolicyPresenceFixture fixture)
    {
        _fixture = fixture;
    }

    // ── FILTER: a TenantA session must not see TenantB rows ──────────────────

    [Fact]
    public async Task TenantA_Session_CannotSee_TenantB_PendingAttempts()
    {
        var subA = Guid.NewGuid();
        var subB = Guid.NewGuid();
        await SeedAsync(subA, TenantA);   // via sa (db_owner) — RLS does not apply to the seed
        await SeedAsync(subB, TenantB);

        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        await SetSessionTenantAsync(conn, TenantA);

        var visible = await SelectSubmissionIdsAsync(conn, new[] { subA, subB });

        visible.Should().Contain(subA, "TenantA session must see its own pending-attempt rows");
        visible.Should().NotContain(subB, "TenantA session must NOT see TenantB's rows (RLS FILTER)");
    }

    // ── BLOCK-AFTER-INSERT: a foreign-tenant INSERT is rejected ──────────────

    [Fact]
    public async Task Session_CannotInsert_ForeignTenant_PendingAttempt()
    {
        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        await SetSessionTenantAsync(conn, TenantA);

        // INSERT a row for TenantB while the session is pinned to TenantA → BLOCK predicate rejects it.
        var act = async () => await InsertAsync(conn, Guid.NewGuid(), TenantB);

        await act.Should()
            .ThrowAsync<SqlException>()
            .Where(ex => ex.Number == 33504, "SQL Server error 33504 = RLS BLOCK predicate violation");
    }

    // ── BLOCK-AFTER-INSERT: the matching-tenant INSERT succeeds (writer path) ─

    [Fact]
    public async Task Session_CanInsert_OwnTenant_PendingAttempt()
    {
        var sub = Guid.NewGuid();

        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        await SetSessionTenantAsync(conn, TenantA);

        // The event-handler / retry-job writers pin SESSION_CONTEXT to the row's tenant, so a matching
        // INSERT must pass the block predicate — a throw here would be a real regression for those writers.
        await InsertAsync(conn, sub, TenantA);

        var visibleToSa = await SelectSubmissionIdsSaAsync(new[] { sub });
        visibleToSa.Should().Contain(sub, "a same-tenant INSERT under the session must succeed");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task SeedAsync(Guid submissionId, Guid tenantId)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString); // sa
        await conn.OpenAsync();
        await InsertAsync(conn, submissionId, tenantId);
    }

    private static async Task InsertAsync(SqlConnection conn, Guid submissionId, Guid tenantId)
    {
        // Minimal INSERT satisfying NOT NULL columns (mirrors the entity insert shape; table has no FKs).
        const string sql = """
            INSERT INTO [forms].[FormSubmissionWorkflowLinkPendingAttempts]
                (TenantId, SubmissionId, FormTemplateId, ApprovalWorkflowDefinitionId, SubmittedBy,
                 AttemptCount, LastFailureCode, Status, CreatedAt, IsDeleted, CreatedBy, ClassificationLevel)
            VALUES
                (@TenantId, @SubmissionId, 1, @WfDef, @SubmittedBy,
                 0, 'RLS_TEST', 'Pending', SYSUTCDATETIME(), 0, @CreatedBy, 1)
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@SubmissionId", submissionId);
        cmd.Parameters.AddWithValue("@WfDef", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@SubmittedBy", Guid.Empty);
        cmd.Parameters.AddWithValue("@CreatedBy", Guid.Empty);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<HashSet<Guid>> SelectSubmissionIdsSaAsync(IEnumerable<Guid> ids)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString); // sa
        await conn.OpenAsync();
        return await SelectSubmissionIdsAsync(conn, ids);
    }

    private static async Task<HashSet<Guid>> SelectSubmissionIdsAsync(
        SqlConnection conn, IEnumerable<Guid> ids)
    {
        var idList = string.Join(",", ids.Select(g => $"'{g}'"));
        var sql = $"""
            SELECT SubmissionId FROM [forms].[FormSubmissionWorkflowLinkPendingAttempts]
            WHERE SubmissionId IN ({idList})
            """;

        var result = new HashSet<Guid>();
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetGuid(0));
        return result;
    }

    private static async Task SetSessionTenantAsync(SqlConnection conn, Guid tenantId)
    {
        await using var cmd = new SqlCommand(
            $"EXEC sp_set_session_context @key=N'TenantId', @value=N'{tenantId}', @read_only=0;",
            conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
