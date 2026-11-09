using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations;
using AqtCoreFW.Workflow.Integration.Tests.Helpers;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests.Migrations;

/// <summary>
/// Integration test for the BackfillWorkflowTaskCandidateIndexFromSnapshot migration.
///
/// Seeds a pool task with CandidateRefsJson but ZERO index rows (simulating the pre-fix
/// state), then executes the backfill SQL directly, and asserts that index rows are
/// materialized with the correct canonical PrincipalRefId + TenantId.
///
/// Also verifies idempotency: running the SQL a second time inserts zero duplicates.
///
/// Uses the same SQL constants as the EF migration (single source of truth — DRY).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowBackfillCandidateIndexMigrationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public WorkflowBackfillCandidateIndexMigrationTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task BackfillSql_SnapshotOnlyPoolTask_MaterializesIndexRows()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        // Seed a snapshot-only pool task: CandidateRefsJson set, zero index rows.
        // This simulates the pre-fix state — task was invisible in inbox + claim.
        var taskId = await SeedSnapshotOnlyPoolTaskAsync(tenantId, createdBy);

        // Before: zero index rows for this task.
        var beforeCount = await CountIndexRowsAsync(taskId);
        beforeCount.Should().Be(0, "pre-seeding must not add any index rows");

        // Execute the backfill SQL against the live test DB.
        await ExecuteBackfillSqlAsync();

        // After: index rows materialized with correct PrincipalType, PrincipalRefId, TenantId.
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var rows = await db.WorkflowTaskCandidateIndex
            .Where(r => r.WorkflowTaskId == taskId)
            .ToListAsync();

        rows.Should().HaveCount(1, "one predicate in the snapshot → one index row");
        rows[0].PrincipalType.Should().Be(CandidatePrincipalType.Role);
        rows[0].PrincipalRefId.Should().Be("REVIEWER",
            "backfill must lift the canonical ref from CandidateRefsJson unchanged");
        rows[0].TenantId.Should().Be(tenantId,
            "backfill must carry the task's TenantId to the index row");
        rows[0].ClauseId.Should().Be(0,
            "backfill uses ClauseId=0 matching the producer contract");
        rows[0].IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task BackfillSql_ReRun_IsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var taskId = await SeedSnapshotOnlyPoolTaskAsync(tenantId, createdBy);

        // First run — materializes rows.
        await ExecuteBackfillSqlAsync();
        var countAfterFirst = await CountIndexRowsAsync(taskId);
        countAfterFirst.Should().Be(1, "first run must insert exactly one row");

        // Second run — idempotency: must insert zero additional rows.
        await ExecuteBackfillSqlAsync();
        var countAfterSecond = await CountIndexRowsAsync(taskId);
        countAfterSecond.Should().Be(1, "re-run must not duplicate rows (idempotency guard)");
    }

    [Fact]
    public async Task BackfillSql_CompletedTask_IsExcluded()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        // Seed a COMPLETED (Done=2) pool task with snapshot but zero index rows.
        // Backfill scope is Open=0 + InProgress=1 only.
        var taskId = await SeedSnapshotOnlyPoolTaskAsync(tenantId, createdBy, status: WorkflowTaskStatus.Done);

        await ExecuteBackfillSqlAsync();

        var count = await CountIndexRowsAsync(taskId);
        count.Should().Be(0, "completed tasks are out of backfill scope — must not be backfilled");
    }

    [Fact]
    public async Task BackfillSql_AssignedTask_IsExcluded()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        // Seed a task WITH a direct assignee (not a pool task).
        // Backfill scope is AssigneeId IS NULL only.
        var taskId = await SeedSnapshotOnlyPoolTaskAsync(tenantId, createdBy, assigneeId: createdBy);

        await ExecuteBackfillSqlAsync();

        var count = await CountIndexRowsAsync(taskId);
        count.Should().Be(0, "tasks with a direct assignee are not pool tasks — must not be backfilled");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a pool task with CandidateRefsJson = one ByRole:REVIEWER predicate and
    /// zero WorkflowTaskCandidateIndex rows. Simulates the pre-fix state.
    /// </summary>
    private async Task<long> SeedSnapshotOnlyPoolTaskAsync(
        Guid tenantId,
        Guid createdBy,
        WorkflowTaskStatus? status = null,
        Guid? assigneeId = null)
    {
        var seeded = await InlineWorkflowDefinitionFactory.CreateAsync(
            _app.Services, tenantId, createdBy);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var instance = WorkflowInstance.Create(
            gen,
            definitionId: seeded.DefinitionId,
            initialStateId: seeded.DraftStateId,
            entityType: "IntegrationTest",
            entityId: Guid.NewGuid(),
            tenantId: tenantId,
            createdBy: createdBy);

        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();

        // Create a pool task manually (bypassing the factory to simulate the pre-fix state).
        var task = WorkflowTask.Create(
            workflowInstanceId: instance.Id,
            title: $"Backfill-test pool task {Guid.NewGuid():N}",
            description: null,
            assigneeId: assigneeId,          // null = pool task; non-null = direct assignee (excluded)
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: tenantId,
            createdBy: createdBy);

        // Inject CandidateRefsJson — the pre-fix snapshot the backfill reads.
        // Wire shape: [{"t":<CandidatePrincipalType.Role=1>,"r":"REVIEWER","c":<AssignmentCombineMode.Union=0>}]
        task.SetCandidate("""[{"t":1,"r":"REVIEWER","c":0}]""");

        db.WorkflowTasks.Add(task);
        await db.SaveChangesAsync();

        // Override status if provided (simulate non-Open states).
        if (status.HasValue && status.Value != WorkflowTaskStatus.Open)
        {
            // Use raw SQL to set the status directly — domain methods enforce state-machine transitions.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [workflow].[WorkflowTasks] SET [Status] = {0} WHERE [Id] = {1}",
                (int)status.Value, task.Id);
        }

        // Intentionally do NOT create any WorkflowTaskCandidateIndex rows —
        // this simulates the pre-fix state where the producer gap left zero index rows.
        return task.Id;
    }

    private async Task<int> CountIndexRowsAsync(long taskId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        return await db.WorkflowTaskCandidateIndex.CountAsync(r => r.WorkflowTaskId == taskId);
    }

    private async Task ExecuteBackfillSqlAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        foreach (var sql in BackfillWorkflowTaskCandidateIndexSql.UpStatements)
            await db.Database.ExecuteSqlRawAsync(sql);
    }
}
