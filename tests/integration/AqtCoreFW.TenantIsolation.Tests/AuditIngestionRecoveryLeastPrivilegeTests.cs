using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// Proves the cell #7 L1 recovery INSERT persists under the prod least-privilege (non-db_owner)
/// login — the dimension a db_owner (sa) integration test CANNOT prove, because IS_MEMBER('db_owner')=1
/// short-circuits the RLS BLOCK predicate (see feedback-sa-dbowner-false-greens-rls-assertions).
///
/// The durable retry job runs in a Hangfire scope whose ambient ITenantContext is the null
/// BackgroundJobTenantContext, so the audit row's tenant is NOT auto-pinned into SESSION_CONTEXT by the
/// interceptor. <c>AuditEntryPersister</c> therefore begins its unit of work with the ROW's tenant
/// (<see cref="CrossModuleUnitOfWork.BeginAsync(Guid, System.Threading.CancellationToken)"/>) so the RLS
/// BLOCK predicate (<c>@TenantId = SESSION_CONTEXT(N'TenantId')</c>) permits the named-tenant INSERT.
/// These tests drive that exact BeginAsync(Guid) path under <c>rls_test_app</c>, with a paired
/// non-vacuity guard proving the tenant context is load-bearing (without it the INSERT is blocked).
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class AuditIngestionRecoveryLeastPrivilegeTests
{
    private readonly RlsPolicyPresenceFixture _fixture;

    public AuditIngestionRecoveryLeastPrivilegeTests(RlsPolicyPresenceFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task NamedTenantInsert_WithTenantSessionContext_Succeeds_UnderLeastPrivilege()
    {
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        // Ambient tenant is null (a Hangfire job); the fix pins the ROW's tenant explicitly.
        await using var uow = NewConstrainedUow();
        await uow.BeginAsync(tenantId);
        await InsertAuditRowAsync(uow, messageId, tenantId);
        await uow.CommitAsync();

        (await RowExistsAsync(messageId)).Should().BeTrue(
            "BeginAsync(tenant) sets SESSION_CONTEXT N'TenantId' so the RLS BLOCK predicate permits the " +
            "named-tenant INSERT under the non-db_owner runtime login");
    }

    [Fact]
    public async Task NamedTenantInsert_WithoutTenantSessionContext_IsBlocked_UnderLeastPrivilege()
    {
        // Non-vacuity + false-green guard: WITHOUT the tenant context (parameterless Begin, ambient
        // null — the pre-fix background-job condition) the SAME INSERT must be rejected by RLS.
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await using var uow = NewConstrainedUow();
        await uow.BeginAsync(); // ambient null → NO TenantId session context

        var act = async () => await InsertAuditRowAsync(uow, messageId, tenantId);

        await act.Should().ThrowAsync<SqlException>(
            "without the tenant SESSION_CONTEXT the RLS BLOCK predicate must reject a named-tenant INSERT " +
            "under a non-db_owner login — the exact failure a db_owner integration test masks");

        (await RowExistsAsync(messageId)).Should().BeFalse("the blocked INSERT must not have persisted");
    }

    private CrossModuleUnitOfWork NewConstrainedUow()
    {
        var dbOpts = Substitute.For<IOptionsMonitor<DatabaseOptions>>();
        dbOpts.CurrentValue.Returns(new DatabaseOptions { ConnectionString = _fixture.ConstrainedConnectionString });
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns((Guid?)null);   // Hangfire BackgroundJobTenantContext behaviour
        tenantCtx.IsSystemAdmin.Returns(true);
        return new CrossModuleUnitOfWork(dbOpts, tenantCtx);
    }

    private static async Task InsertAuditRowAsync(CrossModuleUnitOfWork uow, Guid messageId, Guid tenantId)
    {
        var cmd = uow.Connection.CreateCommand();
        cmd.Transaction = uow.Transaction;
        cmd.CommandText = """
            INSERT INTO [audit].[AuditLogEntries]
                (MessageId, TenantId, CreatedAt, OccurredAt, Action, ModuleName, ResourceType,
                 UserName, HmacSignature, SequenceId, Category, Severity, SchemaVersion)
            VALUES
                (@msg, @tenant, GETUTCDATE(), SYSDATETIMEOFFSET(), 'Create', 'Forms', 'Template',
                 'recovery-leastpriv-test', '', 0, 0, 0, 2)
            """;
        var pMsg = cmd.CreateParameter(); pMsg.ParameterName = "@msg"; pMsg.Value = messageId; cmd.Parameters.Add(pMsg);
        var pTenant = cmd.CreateParameter(); pTenant.ParameterName = "@tenant"; pTenant.Value = tenantId; cmd.Parameters.Add(pTenant);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<bool> RowExistsAsync(Guid messageId)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString); // sa reads back
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM [audit].[AuditLogEntries] WHERE MessageId = @m", conn);
        cmd.Parameters.AddWithValue("@m", messageId);
        return (int)(await cmd.ExecuteScalarAsync())! > 0;
    }
}
