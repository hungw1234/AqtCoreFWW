using System.Text.Json;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Events;
using AqtCoreFW.Workflow.Infrastructure.Jobs;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Tests.Helpers;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Jobs;

/// <summary>
/// Unit tests for WorkflowMigrationExecutor using EF InMemory.
/// IMPORTANT: TenantId must match ITenantContext.TenantId to pass global tenant query filter.
/// Transaction boundaries are not tested at unit level (InMemory doesn't support them);
/// correctness of per-batch isolation is verified in integration tests.
///
/// Phase-4b B6:
///   - WorkflowVersionMigration.Create: sourceDefId/targetDefId = long (T1/T2 internal FK).
///   - WorkflowVersionMigrationItem.Create: requires IGuidGenerator as first param (T3 T2 AK).
///   - StateMappingsJson: {oldStateId, newStateId} are long (T1 WorkflowState.Id bigint PKs).
///   - WorkflowInstancesMigratedEvent: SourceDefinitionPublicId/TargetDefinitionPublicId (Guid T2 AK).
///   - State/Definition IDs are available after SaveChangesAsync (EF InMemory assigns them).
/// </summary>
public sealed class WorkflowMigrationExecutorTests : IDisposable
{
    // Fixed TenantId used across all tests — must match ITenantContext.TenantId mock
    private static readonly Guid TestTenantId = new Guid("11111111-1111-1111-1111-111111111111");

    private readonly WorkflowDbContext _ctx;
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly WorkflowMigrationExecutor _executor;
    private readonly TestGuidGenerator _gen = TestGuidGenerator.Create();

    public WorkflowMigrationExecutorTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns((Guid?)TestTenantId);

        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"MigExec_{Guid.NewGuid()}")
            .Options;
        _ctx = new WorkflowDbContext(options, tenantCtx);

        // Phase-4b B6: executor constructor requires IGuidGenerator + ILogger
        _executor = new WorkflowMigrationExecutor(
            _ctx, _gen, _mediator, NullLogger<WorkflowMigrationExecutor>.Instance);
    }

    public void Dispose() => _ctx.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private WorkflowDefinition MakeDef(string key = "def")
        => WorkflowDefinition.Create(_gen, key, "desc", TestTenantId, Guid.NewGuid());

    /// <summary>
    /// WorkflowState.Create: definitionId is long (T2 WorkflowDefinition.Id — bigint PK).
    /// Id is assigned by EF InMemory after SaveChangesAsync; callers must save defs first.
    /// </summary>
    private static WorkflowState MakeState(long defId, string name)
        => WorkflowState.Create(defId, name, name, name, isInitial: false, isFinal: false, "#000", 0);

    /// <summary>
    /// WorkflowInstance.Create: definitionId + stateId are long (T2/T1 internal PKs).
    /// Requires IGuidGenerator for PublicId generation (T2 entity).
    /// </summary>
    private WorkflowInstance MakeInstance(long defId, long stateId)
        => WorkflowInstance.Create(_gen, defId, stateId, "Case", Guid.NewGuid(), TestTenantId, Guid.NewGuid());

    /// <summary>
    /// WorkflowVersionMigration.Create: sourceDefId/targetDefId = long (T2 internal PKs).
    /// StateMappingsJson uses long state IDs (T1 WorkflowState.Id bigint PKs).
    /// </summary>
    private WorkflowVersionMigration MakeMigrationInProgress(
        long sourceDefId, long targetDefId,
        Dictionary<long, long> stateMap)
    {
        // Serialise as {oldStateId, newStateId} long pairs — ParseStateMappings expects long
        var entries = stateMap.Select(kv => new { oldStateId = kv.Key, newStateId = kv.Value }).ToList();
        var json = JsonSerializer.Serialize(entries);
        var migration = WorkflowVersionMigration.Create(
            sourceDefId, targetDefId, json, TestTenantId, Guid.NewGuid());
        migration.MarkApproved(Guid.NewGuid());
        migration.MarkStarted("job-1");
        return migration;
    }

    // ── Tests ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_HappyPath_MigratesAllInstances_StatusCompleted()
    {
        // Add definitions first — EF assigns long PKs after save
        var srcDef = MakeDef("wf-happy");
        var tgtDef = MakeDef("wf-happy-tgt");
        await _ctx.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        // State IDs available after first save
        var oldState = MakeState(srcDef.Id, "Draft");
        var newState = MakeState(tgtDef.Id, "Draft");
        await _ctx.WorkflowStates.AddRangeAsync(oldState, newState);
        await _ctx.SaveChangesAsync();

        var mapping = new Dictionary<long, long> { [oldState.Id] = newState.Id };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);

        var inst1 = MakeInstance(srcDef.Id, oldState.Id);
        var inst2 = MakeInstance(srcDef.Id, oldState.Id);

        await _ctx.WorkflowInstances.AddRangeAsync(inst1, inst2);
        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        await _executor.ExecuteAsync(migration.Id);

        var updated = await _ctx.VersionMigrations.FindAsync(migration.Id);
        updated!.Status.Should().Be(MigrationStatus.Completed);
        updated.MigratedCount.Should().Be(2);
        updated.FailedCount.Should().Be(0);
        updated.TotalInstances.Should().Be(2);

        // Instances should now point to target definition
        var updInst1 = await _ctx.WorkflowInstances.FindAsync(inst1.Id);
        updInst1!.DefinitionId.Should().Be(tgtDef.Id);
        updInst1.CurrentStateId.Should().Be(newState.Id);

        // Domain event published once with PublicId fields (T2 Guid AK)
        await _mediator.Received(1).Publish(
            Arg.Is<WorkflowInstancesMigratedEvent>(e =>
                e.SourceDefinitionPublicId == srcDef.PublicId &&
                e.TargetDefinitionPublicId == tgtDef.PublicId &&
                e.TenantId == TestTenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_IdempotencyGuard_SkipsAlreadySucceededItems()
    {
        var srcDef = MakeDef("wf-idem");
        var tgtDef = MakeDef("wf-idem-tgt");
        await _ctx.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var oldState = MakeState(srcDef.Id, "Open");
        var newState = MakeState(tgtDef.Id, "Open");
        await _ctx.WorkflowStates.AddRangeAsync(oldState, newState);
        await _ctx.SaveChangesAsync();

        var mapping = new Dictionary<long, long> { [oldState.Id] = newState.Id };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);
        var inst = MakeInstance(srcDef.Id, oldState.Id);

        await _ctx.WorkflowInstances.AddAsync(inst);
        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        // Pre-insert a Succeeded item — simulates prior partial run (Hangfire crash recovery)
        var existingItem = WorkflowVersionMigrationItem.Create(
            _gen, migration.Id, inst.Id, oldState.Id, newState.Id);
        existingItem.MarkSucceeded();

        await _ctx.VersionMigrationItems.AddAsync(existingItem);
        await _ctx.SaveChangesAsync();

        await _executor.ExecuteAsync(migration.Id);

        // Only 1 item should exist — no duplicate created
        var itemCount = await _ctx.VersionMigrationItems
            .CountAsync(i => i.MigrationId == migration.Id && i.InstanceId == inst.Id);
        itemCount.Should().Be(1);

        var updated = await _ctx.VersionMigrations.FindAsync(migration.Id);
        updated!.Status.Should().Be(MigrationStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_HashTamper_MarksFailedWithoutTouchingInstances()
    {
        var srcDef = MakeDef("wf-tamper");
        var tgtDef = MakeDef("wf-tamper-tgt");
        await _ctx.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var oldState = MakeState(srcDef.Id, "Open");
        await _ctx.WorkflowStates.AddAsync(oldState);
        await _ctx.SaveChangesAsync();

        var mapping = new Dictionary<long, long> { [oldState.Id] = oldState.Id + 999 };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);

        // Tamper StateMappingsJson via reflection after migration created (bypasses hash lock)
        typeof(WorkflowVersionMigration)
            .GetProperty("StateMappingsJson")!
            .SetValue(migration, "[{\"oldStateId\":1,\"newStateId\":2}]");

        var inst = MakeInstance(srcDef.Id, oldState.Id);

        await _ctx.WorkflowInstances.AddAsync(inst);
        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        await _executor.ExecuteAsync(migration.Id);

        var updated = await _ctx.VersionMigrations.FindAsync(migration.Id);
        updated!.Status.Should().Be(MigrationStatus.Failed);

        // Instance must NOT have been migrated
        var updInst = await _ctx.WorkflowInstances.FindAsync(inst.Id);
        updInst!.DefinitionId.Should().Be(srcDef.Id);

        // No domain event — failed before batch processing
        await _mediator.DidNotReceive().Publish(
            Arg.Any<WorkflowInstancesMigratedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_UnmappedStateInstance_ItemFailed_OtherInstancesMigrated()
    {
        var srcDef = MakeDef("wf-unmap");
        var tgtDef = MakeDef("wf-unmap-tgt");
        await _ctx.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var mappedState = MakeState(srcDef.Id, "Open");
        var unmappedState = MakeState(srcDef.Id, "Blocked");
        var newState = MakeState(tgtDef.Id, "Open");
        await _ctx.WorkflowStates.AddRangeAsync(mappedState, unmappedState, newState);
        await _ctx.SaveChangesAsync();

        // Only map mappedState → newState; unmappedState has no mapping
        var mapping = new Dictionary<long, long> { [mappedState.Id] = newState.Id };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);

        var instMapped = MakeInstance(srcDef.Id, mappedState.Id);
        var instUnmapped = MakeInstance(srcDef.Id, unmappedState.Id);

        await _ctx.WorkflowInstances.AddRangeAsync(instMapped, instUnmapped);
        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        await _executor.ExecuteAsync(migration.Id);

        var updated = await _ctx.VersionMigrations.FindAsync(migration.Id);
        // Has failed items → final status = Failed
        updated!.Status.Should().Be(MigrationStatus.Failed);
        updated.MigratedCount.Should().Be(1);
        updated.FailedCount.Should().Be(1);

        // Mapped instance migrated to target
        var updMapped = await _ctx.WorkflowInstances.FindAsync(instMapped.Id);
        updMapped!.DefinitionId.Should().Be(tgtDef.Id);

        // Unmapped instance stays on source
        var updUnmapped = await _ctx.WorkflowInstances.FindAsync(instUnmapped.Id);
        updUnmapped!.DefinitionId.Should().Be(srcDef.Id);
    }

    [Fact]
    public async Task ExecuteAsync_CancelRequested_HaltsAfterBatch()
    {
        var srcDef = MakeDef("wf-cancel");
        var tgtDef = MakeDef("wf-cancel-tgt");
        await _ctx.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var oldState = MakeState(srcDef.Id, "Open");
        var newState = MakeState(tgtDef.Id, "Open");
        await _ctx.WorkflowStates.AddRangeAsync(oldState, newState);
        await _ctx.SaveChangesAsync();

        var mapping = new Dictionary<long, long> { [oldState.Id] = newState.Id };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);

        // Set CancelRequested before execution — simulates cancel arriving before first batch
        migration.RequestCancel();

        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        await _executor.ExecuteAsync(migration.Id);

        var updated = await _ctx.VersionMigrations.FindAsync(migration.Id);
        updated!.Status.Should().Be(MigrationStatus.Cancelled);

        // No items — no instances processed
        var itemCount = await _ctx.VersionMigrationItems
            .CountAsync(i => i.MigrationId == migration.Id);
        itemCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_NotInProgress_IsNoOp()
    {
        var srcDef = MakeDef("wf-noop");
        var tgtDef = MakeDef("wf-noop-tgt");
        await _ctx.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        // Build a minimal valid JSON (state IDs are placeholders — migration stays PendingApproval)
        var json = JsonSerializer.Serialize(
            new[] { new { oldStateId = 1L, newStateId = 2L } });
        var migration = WorkflowVersionMigration.Create(
            srcDef.Id, tgtDef.Id, json, TestTenantId, Guid.NewGuid());
        // Leave at PendingApproval — NOT InProgress

        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        await _executor.ExecuteAsync(migration.Id);

        var updated = await _ctx.VersionMigrations.FindAsync(migration.Id);
        updated!.Status.Should().Be(MigrationStatus.PendingApproval);

        await _mediator.DidNotReceive().Publish(
            Arg.Any<WorkflowInstancesMigratedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_EmptyInstanceSet_CompletesWithZeroCounts()
    {
        var srcDef = MakeDef("wf-empty");
        var tgtDef = MakeDef("wf-empty-tgt");
        await _ctx.WorkflowDefinitions.AddRangeAsync(srcDef, tgtDef);
        await _ctx.SaveChangesAsync();

        var oldState = MakeState(srcDef.Id, "Open");
        var newState = MakeState(tgtDef.Id, "Open");
        await _ctx.WorkflowStates.AddRangeAsync(oldState, newState);
        await _ctx.SaveChangesAsync();

        var mapping = new Dictionary<long, long> { [oldState.Id] = newState.Id };
        var migration = MakeMigrationInProgress(srcDef.Id, tgtDef.Id, mapping);

        // No instances — migration should complete immediately with zero counts
        await _ctx.VersionMigrations.AddAsync(migration);
        await _ctx.SaveChangesAsync();

        await _executor.ExecuteAsync(migration.Id);

        var updated = await _ctx.VersionMigrations.FindAsync(migration.Id);
        updated!.Status.Should().Be(MigrationStatus.Completed);
        updated.TotalInstances.Should().Be(0);
        updated.MigratedCount.Should().Be(0);
    }
}
