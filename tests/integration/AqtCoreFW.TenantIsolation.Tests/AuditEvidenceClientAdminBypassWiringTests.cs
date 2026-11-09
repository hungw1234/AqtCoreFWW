using AqtCoreFW.Audit.Infrastructure.Services;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// Wiring proof for <see cref="InProcessAuditEvidenceClient"/>'s D7 security-health reads
/// (<c>GetIntegrityStatusAsync</c> / <c>GetRetentionPolicyStatusAsync</c>).
///
/// <see cref="AuditRlsEnforcementTests"/> already proves the RLS PRIMITIVE — the
/// <c>audit_admin_bypass</c> flag reveals NULL-tenant (system/global) audit rows under a
/// non-db_owner login. This slice proves the CLIENT actually routes its row COUNTs through the
/// bypass opener (<c>QuerySingleWithAuditAdminBypassAsync</c>). A refactor that swapped either COUNT
/// to the plain opener would silently drop NULL-tenant rows from the D7 dashboard totals
/// (QĐ 742 §3.4.a integrity, §3.2 retention) — a compliance under-count these tests turn RED on.
///
/// Login = <c>rls_test_app</c> (<see cref="RlsPolicyPresenceFixture.ConstrainedConnectionString"/>):
/// non-db_owner, granted SELECT on <c>[audit].AuditLogEntries</c> + <c>[audit].AuditRetentionPolicies</c>.
/// The reader is hand-constructed with a null-tenant <see cref="TestTenantContext"/> so the bypass
/// opener sets ONLY <c>audit_admin_bypass</c> (no TenantId) — the RLS FILTER then exposes NULL-tenant
/// rows and nothing else. Each test's non-vacuity leg reads the same COUNT on the PLAIN opener and
/// asserts 0, proving the bypass is what makes the seeded row visible (A/B non-vacuous).
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class AuditEvidenceClientAdminBypassWiringTests
{
    private readonly RlsPolicyPresenceFixture _fixture;

    public AuditEvidenceClientAdminBypassWiringTests(RlsPolicyPresenceFixture fixture)
        => _fixture = fixture;

    // ── GetIntegrityStatusAsync: total-entry COUNT must flow through the admin bypass ──
    [Fact]
    public async Task GetIntegrityStatus_CountsNullTenantRows_ProvingAdminBypassIsWired()
    {
        await SeedNullTenantAuditRowAsync(Guid.NewGuid());
        var reader = NewNullTenantAuditReader();

        // Non-vacuity: the SAME login WITHOUT the bypass sees zero rows — no db_owner, no session
        // tenant, no bypass, so the RLS FILTER hides every row. This makes the bypass assertion below
        // meaningful rather than trivially satisfied by pre-existing data.
        var plainCount = await reader.QuerySingleAsync<long>(
            "SELECT COUNT_BIG(1) FROM [audit].AuditLogEntries");
        plainCount.Should().Be(0,
            "without audit_admin_bypass a non-db_owner, no-tenant reader sees no audit rows");

        var status = await new InProcessAuditEvidenceClient(reader).GetIntegrityStatusAsync();

        status.TotalLogEntries.Should().BeGreaterThanOrEqualTo(1,
            "GetIntegrityStatusAsync must COUNT through the audit-admin bypass so NULL-tenant system " +
            "rows are included; a plain read would report 0 and under-count the D7 integrity total");
    }

    // ── GetRetentionPolicyStatusAsync(global): hot-row COUNT must flow through the admin bypass ──
    [Fact]
    public async Task GetRetentionPolicyStatus_GlobalView_CountsNullTenantHotRows_ProvingAdminBypassIsWired()
    {
        await SeedNullTenantAuditRowAsync(Guid.NewGuid());
        var reader = NewNullTenantAuditReader();

        // Mirror the method's global-view predicate (@TenantId IS NULL) on the plain opener.
        const string hotRowSql =
            "SELECT COUNT_BIG(1) FROM [audit].AuditLogEntries WHERE (@TenantId IS NULL OR TenantId = @TenantId)";
        var plainCount = await reader.QuerySingleAsync<long>(hotRowSql, new { TenantId = (Guid?)null });
        plainCount.Should().Be(0,
            "without audit_admin_bypass the global hot-row COUNT sees no rows on this login");

        var status = await new InProcessAuditEvidenceClient(reader)
            .GetRetentionPolicyStatusAsync(tenantId: null);

        status.HotRowCount.Should().BeGreaterThanOrEqualTo(1,
            "the global (tenantId=null) retention view must COUNT hot rows through the audit-admin " +
            "bypass so NULL-tenant rows are included; a plain read would report 0");
    }

    // ── Reader factory: rls_test_app login, null-tenant context (bypass sets only the flag) ──
    // Mirrors AuditBoundaryDapperIsolationTests.NewReader but pins a null-tenant context so the bypass
    // opener injects only audit_admin_bypass — isolating the FILTER carve-out under test.
    private DapperReadDbConnection NewNullTenantAuditReader()
    {
        var monitor = Substitute.For<IOptionsMonitor<DatabaseOptions>>();
        monitor.CurrentValue.Returns(new DatabaseOptions
        {
            ConnectionString = _fixture.ConstrainedConnectionString
        });
        var uow = Substitute.For<ICrossModuleUnitOfWork>();
        uow.IsActive.Returns(false);
        // isSystemAdmin: true models the real D7 security-health caller (admin-only). It is inert to
        // the DB proof — neither DapperReadDbConnection nor InProcessAuditEvidenceClient reads it; the
        // DENY/EXPOSE outcome is purely a SQL RLS + login-permission fact.
        return new DapperReadDbConnection(
            monitor, new TestTenantContext(null, isSystemAdmin: true), uow, _fixture.ConstrainedConnectionString);
    }

    // Seeds one NULL-tenant audit row via the sa (db_owner) connection — bypasses the RLS BLOCK
    // predicate and the tr_AuditLog_NoMutate trigger (INSERT-exempt). Column shape mirrors
    // AuditRlsEnforcementTests' minimal INSERT (int-coded Category/Severity, empty HMAC filled in prod).
    private async Task SeedNullTenantAuditRowAsync(Guid messageId)
    {
        const string sql = """
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, TenantId, CreatedAt, OccurredAt,
                 Action, ModuleName, ResourceType, UserName,
                 HmacSignature, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@MessageId, NULL, GETUTCDATE(), SYSDATETIMEOFFSET(),
                 'BypassWiringSeed', 'Tests', 'AuditLog', 'wiring-test',
                 '', 0, 0, 0, 2)
            """;
        await using var conn = new SqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MessageId", messageId);
        await cmd.ExecuteNonQueryAsync();
    }
}
