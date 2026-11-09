using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests.Migrations;

/// <summary>
/// Integration tests proving the BP multi-run DossierId fix (AC2, AC4, AC5):
/// - AC2: DB-level OUTBOX-1 keystone: unique index filter scopes NULL-DossierId rows only.
/// - AC4: Migration applies cleanly; BP runs with distinct DossierIds co-exist.
/// - AC5: Migration Down guard prevents raw 1505 when multi-run data exists.
///
/// These tests run under the shared <see cref="SqlServerCollection"/> fixture, where the
/// migration is already applied (index UX_WorkflowInstances_Active_Entity with filter
/// [CompletedAt] IS NULL AND [DossierId] IS NULL present). Each test uses a fresh random
/// TenantId to avoid cross-test pollution.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class BpMultiRunDossierIdTests : IClassFixture<WebAppFixture>, IAsyncLifetime
{
    private const string IndexName = "UX_WorkflowInstances_Active_Entity";

    private const string DropIndexSql = @"
DROP INDEX IF EXISTS [UX_WorkflowInstances_Active_Entity] ON [workflow].[WorkflowInstances];";

    private const string CreateIndexSql = @"
CREATE UNIQUE INDEX [UX_WorkflowInstances_Active_Entity]
ON [workflow].[WorkflowInstances] ([EntityType], [EntityId], [TenantId])
WHERE [CompletedAt] IS NULL AND [DossierId] IS NULL;";

    /// <summary>
    /// Migration Down guard: extracted from
    /// 20260606083540_AddWorkflowInstanceDossierIdAndScopeActiveIndex.Down().
    /// Detects multi-run state and raises THROW 50000 instead of allowing a raw 1505.
    /// </summary>
    private const string DownGuardSql = @"
IF EXISTS (
    SELECT 1
    FROM [workflow].[WorkflowInstances]
    WHERE [CompletedAt] IS NULL
    GROUP BY [EntityType], [EntityId], [TenantId]
    HAVING COUNT(*) > 1
)
    THROW 50000, 'Cannot revert: one or more entities have multiple active workflow instances (BusinessProcess multi-run). Complete or cancel the extra active runs per entity before downgrading, then retry.', 1;";

    private const string EnsureIndexSql = @"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UX_WorkflowInstances_Active_Entity'
      AND object_id = OBJECT_ID('workflow.WorkflowInstances')
)
BEGIN
    CREATE UNIQUE INDEX [UX_WorkflowInstances_Active_Entity]
    ON [workflow].[WorkflowInstances] ([EntityType], [EntityId], [TenantId])
    WHERE [CompletedAt] IS NULL AND [DossierId] IS NULL;
END";

    private readonly WebAppFixture _app;

    public BpMultiRunDossierIdTests(WebAppFixture app)
    {
        _app = app;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Restore index if any test left it dropped.
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        await EnsureIndexExistsAsync(db);
    }

    /// <summary>
    /// AC2 (part 1): Two NULL-DossierId rows sharing (EntityType, EntityId, TenantId)
    /// → 2nd INSERT raises SqlException 2601/2627 (unique index guard).
    /// Proves B' index filter includes NULL-DossierId rows in the constraint.
    /// </summary>
    [Fact]
    public async Task ActiveEntityIndex_TwoNullDossierSameKey_SecondInsertThrows2601()
    {
        var tenant = NewTenantGuid();
        var entityId = Guid.NewGuid();
        const string entityType = "BP_MULTI_RUN_AC2A";

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        try
        {
            await EnsureIndexExistsAsync(db);

            var t0 = DateTimeOffset.UtcNow;
            // Seed first NULL-DossierId row (active, CompletedAt=NULL)
            await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0, dossierId: null);

            // Second NULL-DossierId insert with same key must collide
            Func<Task> attemptDuplicate = () =>
                SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0.AddSeconds(1), dossierId: null);

            var ex = await attemptDuplicate.Should().ThrowAsync<SqlException>();
            ex.Which.Number.Should().BeOneOf(new[] { 2627, 2601 },
                "unique index on (EntityType, EntityId, TenantId) WHERE CompletedAt IS NULL AND DossierId IS NULL must reject second NULL-DossierId row");
        }
        finally
        {
            await PurgeTenantAsync(db, tenant);
        }
    }

    /// <summary>
    /// AC2 (part 2): Two rows sharing (EntityType, EntityId, TenantId) but with DISTINCT
    /// non-null DossierIds → BOTH inserts succeed. Proves B' index filter excludes
    /// non-null-DossierId rows (they don't participate in the unique constraint).
    /// </summary>
    [Fact]
    public async Task ActiveEntityIndex_TwoDistinctDossierSameKey_BothInsert()
    {
        var tenant = NewTenantGuid();
        var entityId = Guid.NewGuid();
        const string entityType = "BP_MULTI_RUN_AC2B";

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        try
        {
            await EnsureIndexExistsAsync(db);

            var t0 = DateTimeOffset.UtcNow;
            var dossier1 = Guid.NewGuid();
            var dossier2 = Guid.NewGuid();

            // Both inserts with distinct non-null DossierIds must succeed
            var id1 = await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0, dossierId: dossier1);
            var id2 = await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0.AddSeconds(1), dossierId: dossier2);

            // Verify both rows exist
            var activeCount = await CountActiveByTenantAndEntityAsync(db, tenant, entityType, entityId);
            activeCount.Should().Be(2,
                "two active rows with distinct DossierIds and same (EntityType, EntityId, TenantId) must both be persisted");

            // Verify they have distinct DossierIds
            var dossiers = await GetDossierIdsByTenantAsync(db, tenant);
            dossiers.Should().Contain(new[] { dossier1, dossier2 },
                "both distinct DossierIds must be present");
        }
        finally
        {
            await PurgeTenantAsync(db, tenant);
        }
    }

    /// <summary>
    /// AC4: Migration has been applied (index with B' filter exists). A seeded BP active row
    /// (NULL DossierId, CompletedAt=NULL) shares (EntityType, EntityId, TenantId) with a NEW BP run
    /// having a non-null DossierId → both can coexist without collision.
    /// Proves the migration correctly re-scoped the index to permit multi-run.
    /// </summary>
    [Fact]
    public async Task MigrationApplied_BpRunsCoexistWithOldNullRow()
    {
        var tenant = NewTenantGuid();
        var entityId = Guid.NewGuid();
        const string entityType = "BP_MULTI_RUN_AC4";

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        try
        {
            await EnsureIndexExistsAsync(db);

            var t0 = DateTimeOffset.UtcNow;

            // Seed a legacy NULL-DossierId active row (pre-multi-run state)
            var oldId = await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0, dossierId: null);

            // Now add a BP run (non-null DossierId) for the same BP entity
            // This must succeed — proves B' filter excludes the non-null row from constraint
            var dossier = Guid.NewGuid();
            var bpRunId = await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0.AddSeconds(1), dossierId: dossier);

            // Both must be present
            var activeCount = await CountActiveByTenantAndEntityAsync(db, tenant, entityType, entityId);
            activeCount.Should().Be(2,
                "legacy NULL-DossierId row + new BP multi-run must coexist");

            // Verify they have different DossierIds (one NULL, one non-null)
            var rows = await GetActiveRowsAsync(db, tenant, entityType, entityId);
            var nullCount = rows.Count(r => r.DossierId == null);
            var nonNullCount = rows.Count(r => r.DossierId != null);
            nullCount.Should().Be(1, "exactly one NULL-DossierId row");
            nonNullCount.Should().Be(1, "exactly one non-null-DossierId row");
        }
        finally
        {
            await PurgeTenantAsync(db, tenant);
        }
    }

    /// <summary>
    /// AC5: When multi-run data exists (multiple active rows per entity), the migration Down
    /// guard SQL must detect it and THROW 50000 (clear error) instead of allowing a raw 1505.
    /// This test runs the guard SQL directly without actually downgrading the migration
    /// (which would be destructive to the shared fixture).
    /// </summary>
    [Fact]
    public async Task DownGuard_MultipleActiveRuns_ThrowsNotRaw1505()
    {
        var tenant = NewTenantGuid();
        var entityId = Guid.NewGuid();
        const string entityType = "BP_MULTI_RUN_AC5";

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        try
        {
            await EnsureIndexExistsAsync(db);

            var t0 = DateTimeOffset.UtcNow;

            // Seed 2 active runs for the same entity with distinct DossierIds
            var dossier1 = Guid.NewGuid();
            var dossier2 = Guid.NewGuid();
            await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0, dossierId: dossier1);
            await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0.AddSeconds(1), dossierId: dossier2);

            // Run the Down guard SQL — should detect multi-run and THROW 50000
            Func<Task> executeDownGuard = () => db.Database.ExecuteSqlRawAsync(DownGuardSql);

            var ex = await executeDownGuard.Should().ThrowAsync<SqlException>();
            ex.Which.Number.Should().Be(50000,
                "Down guard must THROW 50000 (application error) not 1505 (raw index violation) when multi-run data exists");
            ex.Which.Message.Should().Contain("BusinessProcess multi-run",
                "error message must explain the blocking reason");
        }
        finally
        {
            await PurgeTenantAsync(db, tenant);
        }
    }

    /// <summary>
    /// Sanity: Down guard is idempotent — when no multi-run rows exist (single active per entity),
    /// the guard SQL succeeds (no THROW) and returns cleanly.
    /// </summary>
    [Fact]
    public async Task DownGuard_SingleActivePerEntity_Succeeds()
    {
        var tenant = NewTenantGuid();
        var entityId = Guid.NewGuid();
        const string entityType = "BP_MULTI_RUN_AC5_SANITY";

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        try
        {
            await EnsureIndexExistsAsync(db);

            var t0 = DateTimeOffset.UtcNow;

            // Seed only one active row
            var dossier = Guid.NewGuid();
            await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0, dossierId: dossier);

            // Down guard must NOT throw when only one active row exists
            Func<Task> executeDownGuard = () => db.Database.ExecuteSqlRawAsync(DownGuardSql);
            await executeDownGuard.Should().NotThrowAsync<SqlException>(
                "Down guard must pass when each (EntityType, EntityId, TenantId) group has ≤1 active row");
        }
        finally
        {
            await PurgeTenantAsync(db, tenant);
        }
    }

    /// <summary>
    /// AC2a (Layer-1 Repository Guard): Two sequential EF SaveChangesAsync calls creating
    /// GenericWorkflowInstance rows with the same (EntityType, EntityId, TenantId) and both
    /// DossierId=NULL must result in the SECOND SaveChangesAsync returning a failed Result
    /// with WorkflowErrorCode=GOV_WFL_020 (InstanceAlreadyActive).
    ///
    /// This proves the repository's SaveChangesAsync catch handler successfully translates
    /// the raw SQL Server 2601/2627 duplicate-key exception into a clean Result.Fail with
    /// the correct error code — not bubbling a 500 server error.
    ///
    /// Uses real EF change-tracker path (NOT raw INSERT which would bypass the catch).
    /// Each SaveChangesAsync is independent — first creates and persists; second stages
    /// a fresh DI scope's repository and SaveChangesAsync to ensure clean change-tracker isolation.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_SecondActiveNullDossierSameEntity_FailsWithGovWfl020()
    {
        var tenant = NewTenantGuid();
        var entityId = Guid.NewGuid();
        const string entityType = "AC2A_LAYER1_GUARD";

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var repo = new WorkflowInstanceRepository(db);

        try
        {
            await EnsureIndexExistsAsync(db);

            var t0 = DateTimeOffset.UtcNow;

            // Synthetic FK IDs (FKs not enforced in this test context; schema-valid for testing).
            const long syntheticDefinitionId = 1L;
            const long syntheticStateId = 1L;

            // ────────────────────────────────────────────────────────────────────────
            // FIRST SaveChangesAsync: Create and persist first active instance
            // (EntityType="AC2A_LAYER1_GUARD", EntityId, TenantId, DossierId=NULL)
            // ────────────────────────────────────────────────────────────────────────
            var instance1 = WorkflowInstance.Create(
                gen: _app.Services.GetRequiredService<IGuidGenerator>(),
                definitionId: syntheticDefinitionId,
                initialStateId: syntheticStateId,
                entityType: entityType,
                entityId: entityId,
                tenantId: tenant,
                createdBy: Guid.NewGuid(),
                dossierId: null);

            repo.Stage(instance1);
            var result1 = await repo.SaveChangesAsync();

            result1.IsSuccess.Should().BeTrue(
                "first instance with NULL DossierId must persist successfully");

            // ────────────────────────────────────────────────────────────────────────
            // SECOND SaveChangesAsync: Create and attempt to persist a second active
            // instance with same (EntityType, EntityId, TenantId) and DossierId=NULL
            // ────────────────────────────────────────────────────────────────────────

            // Fresh scope to ensure clean DbContext change-tracker.
            await using var scope2 = _app.Services.CreateAsyncScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var repo2 = new WorkflowInstanceRepository(db2);

            var instance2 = WorkflowInstance.Create(
                gen: _app.Services.GetRequiredService<IGuidGenerator>(),
                definitionId: syntheticDefinitionId,
                initialStateId: syntheticStateId,
                entityType: entityType,
                entityId: entityId,
                tenantId: tenant,
                createdBy: Guid.NewGuid(),
                dossierId: null);

            repo2.Stage(instance2);
            var result2 = await repo2.SaveChangesAsync();

            // ────────────────────────────────────────────────────────────────────────
            // ASSERT: second SaveChangesAsync must fail with InstanceAlreadyActive
            // ────────────────────────────────────────────────────────────────────────
            result2.IsFailed.Should().BeTrue(
                "second active instance with same (EntityType, EntityId, TenantId) and NULL DossierId must collide with index");

            var error = result2.Errors.First();

            // Verify the error carries the correct WorkflowErrorCode metadata.
            error.Metadata.Should().ContainKey("WorkflowErrorCode",
                "Layer-1 catch handler must set WorkflowErrorCode metadata on the Result.Fail");
            error.Metadata["WorkflowErrorCode"].Should().Be(
                WorkflowErrorCodes.InstanceAlreadyActive,
                "duplicate active entity (2601/2627) must map to GOV_WFL_020 InstanceAlreadyActive, not 500");

            // Sanity: verify exactly ONE active row remains for this (EntityType, EntityId, TenantId).
            var activeCount = await CountActiveByTenantAndEntityAsync(
                db, tenant, entityType, entityId);
            activeCount.Should().Be(1,
                "only the first instance should be persisted; second was rejected by Layer-1 guard");
        }
        finally
        {
            await PurgeTenantAsync(db, tenant);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static Guid NewTenantGuid() => Guid.NewGuid();

    /// <summary>
    /// Raw-INSERT active WorkflowInstance row with optional DossierId.
    /// Used by all tests to seed multi-run scenarios. Returns inserted [Id].
    /// </summary>
    private static async Task<long> SeedActiveInstanceAsync(
        WorkflowDbContext db,
        Guid tenantId,
        string entityType,
        Guid entityId,
        DateTimeOffset createdAt,
        Guid? dossierId = null)
    {
        var publicId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        const long syntheticFk = 1L;

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO [workflow].[WorkflowInstances]
    ([PublicId], [DefinitionId], [CurrentStateId], [EntityType], [EntityId],
     [DossierId], [StartedAt], [CompletedAt], [CurrentStateEnteredAt], [DueAt], [SlaBreachNotifiedAt],
     [TenantId], [CreatedAt], [UpdatedAt], [IsDeleted], [CreatedBy], [ModifiedBy],
     [ClassificationLevel], [LastMigrationId], [OriginalDefinitionId])
OUTPUT INSERTED.[Id]
VALUES
    (@publicId, @defId, @stateId, @entityType, @entityId,
     @dossierId, @startedAt, NULL, @startedAt, NULL, NULL,
     @tenantId, @createdAt, NULL, 0, @actorId, NULL,
     0, NULL, NULL);";

        void AddParam(string name, object? value, System.Data.DbType type)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            p.DbType = type;
            cmd.Parameters.Add(p);
        }

        AddParam("@publicId", publicId, System.Data.DbType.Guid);
        AddParam("@defId", syntheticFk, System.Data.DbType.Int64);
        AddParam("@stateId", syntheticFk, System.Data.DbType.Int64);
        AddParam("@entityType", entityType, System.Data.DbType.String);
        AddParam("@entityId", entityId, System.Data.DbType.Guid);
        AddParam("@dossierId", dossierId, System.Data.DbType.Guid);
        AddParam("@startedAt", createdAt, System.Data.DbType.DateTimeOffset);
        AddParam("@tenantId", tenantId, System.Data.DbType.Guid);
        AddParam("@createdAt", createdAt.UtcDateTime, System.Data.DbType.DateTime2);
        AddParam("@actorId", actorId, System.Data.DbType.Guid);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result!);
    }

    private static async Task<int> CountActiveByTenantAndEntityAsync(
        WorkflowDbContext db, Guid tenantId, string entityType, Guid entityId)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(*) FROM [workflow].[WorkflowInstances]
WHERE [TenantId] = @t AND [EntityType] = @et AND [EntityId] = @eid AND [CompletedAt] IS NULL;";
        var pT = cmd.CreateParameter();
        pT.ParameterName = "@t";
        pT.Value = tenantId;
        cmd.Parameters.Add(pT);
        var pEt = cmd.CreateParameter();
        pEt.ParameterName = "@et";
        pEt.Value = entityType;
        cmd.Parameters.Add(pEt);
        var pEid = cmd.CreateParameter();
        pEid.ParameterName = "@eid";
        pEid.Value = entityId;
        cmd.Parameters.Add(pEid);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result ?? 0);
    }

    private static async Task<List<Guid>> GetDossierIdsByTenantAsync(WorkflowDbContext db, Guid tenantId)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT DISTINCT [DossierId] FROM [workflow].[WorkflowInstances]
WHERE [TenantId] = @t AND [CompletedAt] IS NULL;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@t";
        p.Value = tenantId;
        cmd.Parameters.Add(p);
        await using var reader = await cmd.ExecuteReaderAsync();
        var dossiers = new List<Guid>();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0))
                dossiers.Add(reader.GetGuid(0));
        }
        return dossiers;
    }

    private static async Task<List<(long Id, Guid? DossierId)>> GetActiveRowsAsync(
        WorkflowDbContext db, Guid tenantId, string entityType, Guid entityId)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT [Id], [DossierId] FROM [workflow].[WorkflowInstances]
WHERE [TenantId] = @t AND [EntityType] = @et AND [EntityId] = @eid AND [CompletedAt] IS NULL;";
        var pT = cmd.CreateParameter();
        pT.ParameterName = "@t";
        pT.Value = tenantId;
        cmd.Parameters.Add(pT);
        var pEt = cmd.CreateParameter();
        pEt.ParameterName = "@et";
        pEt.Value = entityType;
        cmd.Parameters.Add(pEt);
        var pEid = cmd.CreateParameter();
        pEid.ParameterName = "@eid";
        pEid.Value = entityId;
        cmd.Parameters.Add(pEid);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(long, Guid?)>();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt64(0);
            var dossierId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
            rows.Add((id, dossierId));
        }
        return rows;
    }

    private static async Task PurgeTenantAsync(WorkflowDbContext db, Guid tenantId)
    {
        // Cascade FK removes WorkflowInstanceHistories.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [workflow].[WorkflowInstances] WHERE [TenantId] = {tenantId};");
    }

    private static async Task EnsureIndexExistsAsync(WorkflowDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(EnsureIndexSql);
    }
}
