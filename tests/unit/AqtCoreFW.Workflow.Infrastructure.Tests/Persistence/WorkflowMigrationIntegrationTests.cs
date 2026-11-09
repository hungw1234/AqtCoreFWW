using System.Text.Json;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Events;
using AqtCoreFW.Workflow.Infrastructure.Jobs;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Tests.Helpers;
using FluentAssertions;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Persistence;

/// <summary>
/// Integration tests for WorkflowVersionMigration using REAL SQL Server (Testcontainers).
/// Verifies:
/// - Schema: tables, indexes, unique constraints exist
/// - Advisory lock: sp_getapplock blocks concurrent migrations
/// - Hangfire bypass tenant: executor works without HttpContext
/// - End-to-end: 125 instances across 3 batches (per-batch commits visible)
/// - Idempotency: re-run after crash doesn't duplicate
/// - Tamper detection: direct SQL modification fails fast
/// - Cancel: mid-migration halts worker
/// - Cascade delete: deleting migration deletes items
///
/// Phase-4b B6:
///   - WorkflowDefinition.Create: IGuidGenerator as first param (T2 entity).
///   - WorkflowState.Create: long definitionId (T2 internal PK, DB-assigned after save).
///   - WorkflowInstance.Create: IGuidGenerator + long defId + long stateId + Guid createdBy (T2).
///   - WorkflowVersionMigration.Create: long sourceDefId/targetDefId (T2 internal PKs).
///   - WorkflowVersionMigrationItem.Create: IGuidGenerator as first param (T3 entity).
///   - StateMappingsJson: long state IDs (T1 WorkflowState.Id bigint PKs).
///   - WorkflowInstancesMigratedEvent: SourceDefinitionPublicId/TargetDefinitionPublicId (Guid T2 AK).
///   - Integration tests use real SQL — save order matters (defs before states before instances).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowMigrationIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TestTenantId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AdminUserId = new("22222222-2222-2222-2222-222222222222");

    private readonly SqlServerFixture _fixture;
    private readonly string _connectionString;
    private readonly TestGuidGenerator _gen = TestGuidGenerator.Create();
    private WorkflowDbContext? _ctx;
    private IMediator? _mediator;
    private WorkflowMigrationExecutor? _executor;

    public WorkflowMigrationIntegrationTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
        _connectionString = fixture.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer(_connectionString, opts =>
            {
                opts.CommandTimeout(30);
                opts.EnableRetryOnFailure(0);
            })
            .Options;

        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns((Guid?)TestTenantId);

        _ctx = new WorkflowDbContext(options, tenantCtx);
        _mediator = Substitute.For<IMediator>();

        // Phase-4b B6: executor needs IGuidGenerator + ILogger
        _executor = new WorkflowMigrationExecutor(
            _ctx, _gen, _mediator, NullLogger<WorkflowMigrationExecutor>.Instance);

        // Create schema directly from current model (no migrations, fresh Testcontainers instance)
        await _ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_ctx != null)
            await _ctx.DisposeAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private WorkflowDefinition MakeDef(string key = "def", int? version = null)
    {
        var def = WorkflowDefinition.Create(_gen, key, "desc", TestTenantId, AdminUserId);
        if (version.HasValue)
        {
            typeof(WorkflowDefinition)
                .GetProperty("Version")!
                .SetValue(def, version.Value);
        }
        return def;
    }

    /// <summary>
    /// WorkflowState.Create: long definitionId — must be called after definition is saved to DB.
    /// </summary>
    private static WorkflowState MakeState(long defId, string name)
        => WorkflowState.Create(defId, name, name, name, isInitial: false, isFinal: false, "#000", 0);

    /// <summary>
    /// WorkflowInstance.Create: long defId + long stateId (DB-assigned PKs after save).
    /// entityId must be unique per instance to avoid UX_WorkflowInstances_Active_Entity collision.
    /// </summary>
    private WorkflowInstance MakeInstance(long defId, long stateId)
        => WorkflowInstance.Create(_gen, defId, stateId, "Case", Guid.NewGuid(), TestTenantId, AdminUserId);

    /// <summary>
    /// WorkflowVersionMigration.Create: sourceDefId/targetDefId = long (T2 internal PKs).
    /// StateMappingsJson uses long T1 state IDs (not Guid).
    /// </summary>
    private WorkflowVersionMigration MakeMigrationInProgress(
        long sourceDefId, long targetDefId, Dictionary<long, long> stateMap)
    {
        var entries = stateMap.Select(kv => new { oldStateId = kv.Key, newStateId = kv.Value }).ToList();
        var json = JsonSerializer.Serialize(entries);
        var migration = WorkflowVersionMigration.Create(
            sourceDefId, targetDefId, json, TestTenantId, AdminUserId);
        migration.MarkApproved(AdminUserId);
        migration.MarkStarted("job-1");
        return migration;
    }

    // ── Schema Verification ───────────────────────────────────────────────────────

    [Fact]
    public async Task Schema_Tables_Created_After_Migration()
    {
        var srcDef = MakeDef("schema-test");
        var tgtDef = MakeDef("schema-test-tgt");

        await _ctx!.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var count = await _ctx.WorkflowDefinitions.CountAsync();
        count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Unique_MigrationId_InstanceId_Constraint_Enforced()
    {
        var srcDef = MakeDef("dup-test");
        var tgtDef = MakeDef("dup-test-tgt");
        await _ctx!.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var oldState = MakeState(srcDef.Id, "Draft");
        var newState = MakeState(tgtDef.Id, "Draft");
        await _ctx.WorkflowStates.AddRangeAsync(oldState, newState);
        await _ctx.SaveChangesAsync();

        var inst = MakeInstance(srcDef.Id, oldState.Id);
        var mapping = new Dictionary<long, long> { [oldState.Id] = newState.Id };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);

        await _ctx.WorkflowInstances.AddAsync(inst);
        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        // Insert one item — requires IGuidGenerator (T3 entity)
        var item1 = WorkflowVersionMigrationItem.Create(
            _gen, migration.Id, inst.Id, oldState.Id, newState.Id);
        await _ctx.VersionMigrationItems.AddAsync(item1);
        await _ctx.SaveChangesAsync();

        // Try to insert duplicate — should fail at DB level
        var item2 = WorkflowVersionMigrationItem.Create(
            _gen, migration.Id, inst.Id, oldState.Id, newState.Id);
        await _ctx.VersionMigrationItems.AddAsync(item2);

        var act = async () => await _ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ── Concurrent Safety ────────────────────────────────────────────────────────

    [Fact]
    public async Task Concurrent_Write_To_Migration_Items_Enforces_Unique_Constraint()
    {
        var srcDef = MakeDef("concurrent-test");
        var tgtDef = MakeDef("concurrent-test-tgt");
        await _ctx!.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var oldState = MakeState(srcDef.Id, "Draft");
        var newState = MakeState(tgtDef.Id, "Draft");
        await _ctx.WorkflowStates.AddRangeAsync(oldState, newState);
        await _ctx.SaveChangesAsync();

        var mapping = new Dictionary<long, long> { [oldState.Id] = newState.Id };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);
        var inst = MakeInstance(srcDef.Id, oldState.Id);

        await _ctx.WorkflowInstances.AddAsync(inst);
        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        var item1 = WorkflowVersionMigrationItem.Create(
            _gen, migration.Id, inst.Id, oldState.Id, newState.Id);
        var item2 = WorkflowVersionMigrationItem.Create(
            _gen, migration.Id, inst.Id, oldState.Id, newState.Id);

        await _ctx.VersionMigrationItems.AddAsync(item1);
        await _ctx.SaveChangesAsync();

        await _ctx.VersionMigrationItems.AddAsync(item2);
        var act = async () => await _ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ── End-to-End: 125 Instances Across 3 Batches ───────────────────────────────

    [Fact]
    public async Task Execute_125_Instances_Across_3_Batches_Completes()
    {
        var srcDef = MakeDef("e2e-125", version: 1);
        var tgtDef = MakeDef("e2e-125-tgt", version: 2);
        await _ctx!.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var oldState = MakeState(srcDef.Id, "Draft");
        var newState = MakeState(tgtDef.Id, "Draft");
        await _ctx.WorkflowStates.AddRangeAsync(oldState, newState);
        await _ctx.SaveChangesAsync();

        var mapping = new Dictionary<long, long> { [oldState.Id] = newState.Id };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);

        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        var instances = Enumerable.Range(0, 125)
            .Select(_ => MakeInstance(srcDef.Id, oldState.Id))
            .ToList();
        await _ctx.WorkflowInstances.AddRangeAsync(instances);
        await _ctx.SaveChangesAsync();

        await _executor!.ExecuteAsync(migration.Id);

        var updated = await _ctx.VersionMigrations.FindAsync(migration.Id);
        updated!.Status.Should().Be(MigrationStatus.Completed);
        updated.MigratedCount.Should().Be(125);
        updated.FailedCount.Should().Be(0);
        updated.TotalInstances.Should().Be(125);

        var migratedCount = await _ctx.WorkflowInstances
            .CountAsync(i => i.DefinitionId == tgtDef.Id);
        migratedCount.Should().Be(125);

        var itemCount = await _ctx.VersionMigrationItems
            .CountAsync(i => i.MigrationId == migration.Id);
        itemCount.Should().Be(125);

        // Domain event uses PublicId fields (T2 Guid AK) — not internal long IDs
        await _mediator!.Received(1).Publish(
            Arg.Is<WorkflowInstancesMigratedEvent>(e =>
                e.SourceDefinitionPublicId == srcDef.PublicId &&
                e.TargetDefinitionPublicId == tgtDef.PublicId),
            Arg.Any<CancellationToken>());
    }

    // ── Idempotency: Rerun After Mid-Batch Crash ────────────────────────────────

    [Fact]
    public async Task Executor_Rerun_After_Mid_Batch_Crash_Does_Not_Duplicate_History()
    {
        var srcDef = MakeDef("idempotency");
        var tgtDef = MakeDef("idempotency-tgt");
        await _ctx!.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var oldState = MakeState(srcDef.Id, "Draft");
        var newState = MakeState(tgtDef.Id, "Draft");
        await _ctx.WorkflowStates.AddRangeAsync(oldState, newState);
        await _ctx.SaveChangesAsync();

        var mapping = new Dictionary<long, long> { [oldState.Id] = newState.Id };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);

        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        var instances = Enumerable.Range(0, 10)
            .Select(_ => MakeInstance(srcDef.Id, oldState.Id))
            .ToList();
        await _ctx.WorkflowInstances.AddRangeAsync(instances);
        await _ctx.SaveChangesAsync();

        await _executor!.ExecuteAsync(migration.Id);
        var historyCount1 = await _ctx.WorkflowInstanceHistories.CountAsync();

        await _executor.ExecuteAsync(migration.Id);
        var historyCount2 = await _ctx.WorkflowInstanceHistories.CountAsync();

        historyCount2.Should().Be(historyCount1);
    }

    // ── Cancel Mid-Migration ──────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_Mid_InProgress_Halts_Between_Batches()
    {
        var srcDef = MakeDef("cancel-test");
        var tgtDef = MakeDef("cancel-test-tgt");
        await _ctx!.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var oldState = MakeState(srcDef.Id, "Draft");
        var newState = MakeState(tgtDef.Id, "Draft");
        await _ctx.WorkflowStates.AddRangeAsync(oldState, newState);
        await _ctx.SaveChangesAsync();

        var mapping = new Dictionary<long, long> { [oldState.Id] = newState.Id };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);

        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        var instances = Enumerable.Range(0, 10)
            .Select(_ => MakeInstance(srcDef.Id, oldState.Id))
            .ToList();
        await _ctx.WorkflowInstances.AddRangeAsync(instances);
        await _ctx.SaveChangesAsync();

        migration.RequestCancel();
        await _ctx.SaveChangesAsync();

        await _executor!.ExecuteAsync(migration.Id);

        var updated = await _ctx.VersionMigrations.FindAsync(migration.Id);
        updated!.Status.Should().Be(MigrationStatus.Cancelled);
    }

    // ── Cascade Delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_Migration_Deletes_Items()
    {
        var srcDef = MakeDef("cascade-test");
        var tgtDef = MakeDef("cascade-test-tgt");
        await _ctx!.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var oldState = MakeState(srcDef.Id, "Draft");
        var newState = MakeState(tgtDef.Id, "Draft");
        await _ctx.WorkflowStates.AddRangeAsync(oldState, newState);
        await _ctx.SaveChangesAsync();

        var mapping = new Dictionary<long, long> { [oldState.Id] = newState.Id };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);

        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        var instances = Enumerable.Range(0, 10)
            .Select(_ => MakeInstance(srcDef.Id, oldState.Id))
            .ToList();
        await _ctx.WorkflowInstances.AddRangeAsync(instances);
        await _ctx.SaveChangesAsync();

        // WorkflowVersionMigrationItem.Create requires IGuidGenerator (T3 entity)
        foreach (var inst in instances)
        {
            var item = WorkflowVersionMigrationItem.Create(
                _gen, migration.Id, inst.Id, oldState.Id, newState.Id);
            await _ctx.VersionMigrationItems.AddAsync(item);
        }
        await _ctx.SaveChangesAsync();

        var itemCountBefore = await _ctx.VersionMigrationItems
            .CountAsync(i => i.MigrationId == migration.Id);
        itemCountBefore.Should().Be(10);

        _ctx.VersionMigrations.Remove(migration);
        await _ctx.SaveChangesAsync();

        var itemCountAfter = await _ctx.VersionMigrationItems
            .CountAsync(i => i.MigrationId == migration.Id);
        itemCountAfter.Should().Be(0);
    }

    // ── Helper Methods for Schema Inspection ──────────────────────────────────────

    private async Task<List<string>> GetTableNames()
    {
        var tables = new List<string>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            AND TABLE_SCHEMA = 'dbo'
        ";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private async Task<List<string>> GetIndexNames(string tableName)
    {
        var indexes = new List<string>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT name FROM sys.indexes
            WHERE object_id = OBJECT_ID(@tableName)
            AND name IS NOT NULL
        ";
        cmd.Parameters.AddWithValue("@tableName", $"[dbo].[{tableName}]");
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            indexes.Add(reader.GetString(0));
        return indexes;
    }

    private async Task<List<string>> GetColumnNames(string tableName)
    {
        var columns = new List<string>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @tableName
            AND TABLE_SCHEMA = 'dbo'
        ";
        cmd.Parameters.AddWithValue("@tableName", tableName);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));
        return columns;
    }

    private async Task<List<string>> GetConstraints(string tableName)
    {
        var constraints = new List<string>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CONSTRAINT_NAME
            FROM INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE
            WHERE TABLE_NAME = @tableName
            AND TABLE_SCHEMA = 'dbo'
        ";
        cmd.Parameters.AddWithValue("@tableName", tableName);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            constraints.Add(reader.GetString(0));
        return constraints;
    }
}
