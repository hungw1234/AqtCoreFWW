using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Audit.Integration.Tests;

[Collection(SqlServerCollection.CollectionName)]
public sealed class AuditLogImmutabilityTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    public AuditLogImmutabilityTests(WebAppFixture app) { _app = app; }

    [Fact]
    public async Task RawUpdate_WithoutBypass_RejectedByTrigger()
    {
        var entryId = await SeedEntryAsync();
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE [audit].[AuditLogEntries] SET Action = N'TAMPERED' WHERE MessageId = @id;";
        cmd.Parameters.Add(new SqlParameter("@id", (object)entryId));
        var act = async () => await cmd.ExecuteNonQueryAsync();
        var ex = await act.Should().ThrowAsync<SqlException>();
        ex.Which.Message.Should().Contain("append-only");
    }

    [Fact]
    public async Task RawDelete_WithoutBypass_RejectedByTrigger()
    {
        var entryId = await SeedEntryAsync();
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM [audit].[AuditLogEntries] WHERE MessageId = @id;";
        cmd.Parameters.Add(new SqlParameter("@id", (object)entryId));
        var act = async () => await cmd.ExecuteNonQueryAsync();
        var ex = await act.Should().ThrowAsync<SqlException>();
        ex.Which.Message.Should().Contain("append-only");
    }

    [Fact]
    public async Task RawDelete_WithArchiveBypass_Succeeds()
    {
        var entryId = await SeedEntryAsync();
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using (var setCtx = conn.CreateCommand())
        {
            setCtx.CommandText = "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'archive', @read_only=0;";
            await setCtx.ExecuteNonQueryAsync();
        }
        await using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM [audit].[AuditLogEntries] WHERE MessageId = @id;";
        deleteCmd.Parameters.Add(new SqlParameter("@id", (object)entryId));
        var affected = await deleteCmd.ExecuteNonQueryAsync();
        affected.Should().Be(1);
    }

    [Fact]
    public async Task UpdateSignatureAsync_RepoPath_BypassesTrigger()
    {
        var entryId = await SeedEntryWithEmptySignatureAsync();
        using var scope = _app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<AqtCoreFW.Audit.Domain.Repositories.IAuditLogRepository>();
        var act = async () => await repo.UpdateSignatureAsync(entryId, "DEADBEEF");
        await act.Should().NotThrowAsync();
        using var verifyScope = _app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AuditDbContext>();
        // T3 entity: MessageId is the Guid AK
        var row = await db.AuditLogEntries.AsNoTracking().FirstAsync(e => e.MessageId == entryId);
        row.HmacSignature.Should().Be("DEADBEEF");
    }

    // ── QD742-AUDIT-403-FU1: presence guards against future migration-regen loss ──
    // The Phase-06 "drop 217 legacy migrations + regen" sweep silently dropped the
    // append-only trigger and the global-default retention seed row (both restored by
    // 20260512034220_RestoreAuditImmutabilityTriggerAndDefaultRetention). These two
    // mechanical checks fail loudly if either disappears again.

    [Fact]
    public async Task Trigger_tr_AuditLog_NoMutate_Exists_AndEnabled()
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT is_disabled
            FROM sys.triggers
            WHERE name = N'tr_AuditLog_NoMutate'
              AND parent_id = OBJECT_ID(N'[audit].[AuditLogEntries]');
            """;
        var result = await cmd.ExecuteScalarAsync();
        result.Should().NotBeNull("the [audit].[tr_AuditLog_NoMutate] append-only trigger must exist on [audit].[AuditLogEntries]");
        Convert.ToInt32(result).Should().Be(0, "the append-only trigger must be enabled");
    }

    [Fact]
    public async Task GlobalDefaultRetentionPolicy_RowExists()
    {
        await using var conn = new SqlConnection(GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM [audit].[AuditRetentionPolicies] WHERE TenantId IS NULL AND Category IS NULL;";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().BeGreaterThanOrEqualTo(1, "the global-default AuditRetentionPolicies row (TenantId IS NULL, Category IS NULL) must be seeded");
    }

    private async Task<Guid> SeedEntryWithEmptySignatureAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var entry = AuditLogEntry.Create(gen: gen, tenantId: Guid.NewGuid(), userId: Guid.NewGuid(),
            userName: "test-user", action: AuditAction.Login.Value, moduleName: "Identity",
            resourceType: "User", resourceId: Guid.NewGuid().ToString(),
            dataSnapshot: null, ipAddress: "127.0.0.1", correlationId: null,
            category: AuditCategory.AppAccess, severity: AuditSeverity.Low);
        db.AuditLogEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.MessageId;
    }

    private string GetConnectionString()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return db.Database.GetConnectionString()!;
    }

    private async Task<Guid> SeedEntryAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var entry = AuditLogEntry.Create(gen: gen, tenantId: Guid.NewGuid(), userId: Guid.NewGuid(),
            userName: "test-user", action: AuditAction.Login.Value, moduleName: "Identity",
            resourceType: "User", resourceId: Guid.NewGuid().ToString(),
            dataSnapshot: null, ipAddress: "127.0.0.1", correlationId: null,
            category: AuditCategory.AppAccess, severity: AuditSeverity.Low);
        entry.SetHmacSignature("STUB");
        db.AuditLogEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.MessageId;
    }
}
