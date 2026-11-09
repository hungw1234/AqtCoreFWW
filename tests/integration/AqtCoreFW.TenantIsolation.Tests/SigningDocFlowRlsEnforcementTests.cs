using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// Verifies the symmetric RLS policy on the Tier-3 doc-flow tables
/// (<c>identity.SigningDocFlowRecords</c> + <c>identity.SigningDocFlowNonces</c>, added by
/// <c>20260615184000_AddSigningDocFlowRlsPolicies</c>) is enforced for a non-db_owner SQL login.
///
/// All assertions connect via <see cref="RlsPolicyPresenceFixture.ConstrainedConnectionString"/>
/// (<c>rls_test_app</c> — NOT db_owner). Seeding uses the <c>sa</c> connection (RLS-bypassing).
///
/// PREDICATE SHAPE (standard uniqueidentifier TenantId):
///   IS_MEMBER('db_owner')=1
///   OR @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
///
/// db_owner masks RLS — so these MUST run under the constrained login or they pass falsely.
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class SigningDocFlowRlsEnforcementTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-5165-A00000000001");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-5165-B00000000002");

    private readonly RlsPolicyPresenceFixture _fixture;

    public SigningDocFlowRlsEnforcementTests(RlsPolicyPresenceFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Records_TenantA_Session_CannotSee_TenantB_Rows()
    {
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        await SeedRecordAsync(TenantA, docA);
        await SeedRecordAsync(TenantB, docB);

        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        await SetSessionTenantAsync(conn, TenantA);

        var visible = await SelectRecordDocIdsAsync(conn, new[] { docA, docB });
        visible.Should().Contain(docA, "TenantA session must see its own signing record");
        visible.Should().NotContain(docB, "TenantA session must NOT see TenantB's signing record (FILTER predicate)");
    }

    [Fact]
    public async Task Records_CannotInsert_ForeignTenantRow_WhenSessionIsTenantA()
    {
        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        await SetSessionTenantAsync(conn, TenantA);

        // BLOCK AFTER INSERT: row TenantId(B) != SESSION_CONTEXT TenantId(A), not db_owner.
        var act = async () => await InsertRecordAsync(conn, TenantB, Guid.NewGuid());

        await act.Should()
            .ThrowAsync<SqlException>()
            .Where(ex => ex.Number == 33504,
                "SQL error 33504 = BLOCK predicate violation — foreign-tenant INSERT must be blocked");
    }

    [Fact]
    public async Task Nonces_TenantA_Session_CannotSee_TenantB_Rows()
    {
        // Records first (FK target), then nonces under each tenant.
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        var recA = await SeedRecordAsync(TenantA, docA);
        var recB = await SeedRecordAsync(TenantB, docB);
        var nonceA = Guid.NewGuid();
        var nonceB = Guid.NewGuid();
        await SeedNonceAsync(nonceA, TenantA, recA);
        await SeedNonceAsync(nonceB, TenantB, recB);

        await using var conn = new SqlConnection(_fixture.ConstrainedConnectionString);
        await conn.OpenAsync();
        await SetSessionTenantAsync(conn, TenantA);

        var visible = await SelectNonceIdsAsync(conn, new[] { nonceA, nonceB });
        visible.Should().Contain(nonceA, "TenantA session must see its own nonce");
        visible.Should().NotContain(nonceB, "TenantA session must NOT see TenantB's nonce (FILTER predicate)");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<long> SeedRecordAsync(Guid tenantId, Guid docId)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString); // sa — RLS bypassed
        await conn.OpenAsync();
        return await InsertRecordAsync(conn, tenantId, docId);
    }

    private static async Task<long> InsertRecordAsync(SqlConnection conn, Guid tenantId, Guid docId)
    {
        const string sql = """
            INSERT INTO [identity].[SigningDocFlowRecords]
                (TenantId, DocId, SignType, Status, OriginalStorageKey, JwtNonce,
                 InitiatedBy, InitiatedAt, CreatedBy, CreatedAt, IsDeleted, ClassificationLevel)
            OUTPUT INSERTED.Id
            VALUES
                (@TenantId, @DocId, 1, 0, @Key, @Nonce,
                 @Actor, GETUTCDATE(), @Actor, GETUTCDATE(), 0, 0)
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@DocId", docId);
        cmd.Parameters.AddWithValue("@Key", $"{tenantId}/{docId}/original.pdf");
        cmd.Parameters.AddWithValue("@Nonce", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@Actor", Guid.Parse("00000000-0000-0000-0000-0000000000aa"));

        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(id);
    }

    private async Task SeedNonceAsync(Guid nonce, Guid tenantId, long recordId)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString); // sa — RLS bypassed
        await conn.OpenAsync();

        const string sql = """
            INSERT INTO [identity].[SigningDocFlowNonces]
                (Nonce, TenantId, DocFlowRecordId, UseCount, MaxUses, ExpiresAt)
            VALUES
                (@Nonce, @TenantId, @RecordId, 0, 2, DATEADD(MINUTE, 10, GETUTCDATE()))
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Nonce", nonce);
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@RecordId", recordId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<Guid>> SelectRecordDocIdsAsync(SqlConnection conn, IEnumerable<Guid> docIds)
    {
        var idList = string.Join(",", docIds.Select(g => $"'{g}'"));
        var sql = $"SELECT DocId FROM [identity].[SigningDocFlowRecords] WHERE DocId IN ({idList})";
        return await ReadGuidsAsync(conn, sql);
    }

    private static async Task<HashSet<Guid>> SelectNonceIdsAsync(SqlConnection conn, IEnumerable<Guid> nonces)
    {
        var idList = string.Join(",", nonces.Select(g => $"'{g}'"));
        var sql = $"SELECT Nonce FROM [identity].[SigningDocFlowNonces] WHERE Nonce IN ({idList})";
        return await ReadGuidsAsync(conn, sql);
    }

    private static async Task<HashSet<Guid>> ReadGuidsAsync(SqlConnection conn, string sql)
    {
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
            $"EXEC sp_set_session_context @key=N'TenantId', @value=N'{tenantId}', @read_only=0;", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
