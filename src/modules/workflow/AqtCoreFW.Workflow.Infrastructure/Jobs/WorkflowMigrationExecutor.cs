using System.Text.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Events;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Infrastructure.Jobs;

/// <summary>
/// Hangfire worker that executes a version migration batch-by-batch.
/// Implements IWorkflowMigrationExecutor — invoked by Hangfire after ExecuteVersionMigrationCommand
/// enqueues the job. Idempotent: Hangfire retry after crash re-enters and skips Succeeded items.
/// Per-batch transaction via MigrationBatchProcessor (SQL timeout safety).
/// Phase-4b B4c: migrationId = long (T1 WorkflowVersionMigration.Id bigint PK).
///               stateMapping = Dictionary&lt;long, long&gt; (T1 WorkflowState.Id bigint PKs).
///               WorkflowInstance.Id is long; keyset pagination uses long afterId.
///               WorkflowInstancesMigratedEvent uses SourceDefinitionPublicId/TargetDefinitionPublicId (Guid T2 AK).
/// </summary>
[AutomaticRetry(Attempts = 3)]
[DisableConcurrentExecution(timeoutInSeconds: 600)]
public sealed class WorkflowMigrationExecutor(
    WorkflowDbContext context,
    IGuidGenerator gen,
    IMediator mediator,
    ILogger<WorkflowMigrationExecutor> logger)
    : IWorkflowMigrationExecutor
{
    private const int BatchSize = 50;

    // migrationId is long (T1 PK)
    public async Task ExecuteAsync(long migrationId)
    {
        var ct = CancellationToken.None; // Hangfire does not pass CT

        // Step 1: Load migration; assert InProgress
        // IgnoreQueryFilters: Hangfire runs without HTTP context — no tenant or soft-delete filter
        var migration = await context.VersionMigrations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == migrationId, ct);

        if (migration is null)
        {
            logger.LogError("Migration {MigrationId} not found. Aborting.", migrationId);
            return;
        }

        if (migration.Status != MigrationStatus.InProgress)
        {
            logger.LogWarning(
                "Migration {MigrationId} is in status {Status}, expected InProgress. Aborting (no-op).",
                migrationId, migration.Status);
            return;
        }

        // Step 2: Re-verify MappingHash (tamper detection)
        try { migration.VerifyMappingHash(); }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Migration {MigrationId}: hash tamper detected. Marking failed.", migrationId);
            migration.MarkFailed();
            await context.SaveChangesAsync(ct);
            return;
        }

        // Step 3: Parse state mappings — Dictionary<long, long> (T1 state Id bigint PKs)
        Dictionary<long, long> stateMapping;
        try
        {
            stateMapping = ParseStateMappings(migration.StateMappingsJson);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration {MigrationId}: failed to parse StateMappingsJson.", migrationId);
            migration.MarkFailed();
            await context.SaveChangesAsync(ct);
            return;
        }

        // Step 4: Count total active instances for progress tracking
        // IgnoreQueryFilters: explicit IsDeleted + tenant filter applied manually below
        var total = await context.WorkflowInstances
            .IgnoreQueryFilters()
            .CountAsync(i => i.DefinitionId == migration.SourceDefinitionId
                          && i.TenantId == migration.TenantId
                          && i.CompletedAt == null
                          && !i.IsDeleted, ct);
        migration.SetTotalInstances(total);
        await context.SaveChangesAsync(ct);

        // Step 5: Keyset-paginate and process batches (afterId is long — WorkflowInstance.Id bigint PK)
        var processor = new MigrationBatchProcessor(context, gen, logger);
        long lastId = 0;
        var completed = false;

        while (!completed)
        {
            // Reload migration to pick up CancelRequested flag set by admin between batches
            await context.Entry(migration).ReloadAsync(ct);
            if (migration.CancelRequested)
            {
                migration.MarkCancelled();
                await context.SaveChangesAsync(ct);
                logger.LogInformation("Migration {MigrationId} cancelled (polled pre-batch).", migration.Id);
                return;
            }

            var batch = await LoadNextBatchAsync(migration.SourceDefinitionId, lastId, ct);

            if (batch.Count == 0)
            {
                completed = true;
                break;
            }

            await processor.ProcessBatchAsync(migration, batch, stateMapping, ct);

            // batch[^1].Id is long (T1 WorkflowInstance bigint PK)
            lastId = batch[^1].Id;

            logger.LogInformation(
                "Migration {MigrationId}: batch processed. Migrated={Migrated}, Failed={Failed}, Skipped={Skipped}.",
                migrationId, migration.MigratedCount, migration.FailedCount, migration.SkippedCount);

            if (batch.Count < BatchSize) completed = true;
        }

        // Step 6: Finalize status
        if (migration.FailedCount > 0)
            migration.MarkFailed();
        else
            migration.MarkCompleted();

        await context.SaveChangesAsync(ct);

        // Step 7: Publish domain event → cache invalidation.
        // WorkflowInstancesMigratedEvent now uses PublicId fields (T2 Guid AK).
        // WorkflowVersionMigration.SourceDefinitionId / TargetDefinitionId are long (internal FK).
        // Lookup PublicIds from the context for the event.
        var sourcePubId = await context.WorkflowDefinitions
            .IgnoreQueryFilters()
            .Where(d => d.Id == migration.SourceDefinitionId)
            .Select(d => d.PublicId)
            .FirstOrDefaultAsync(ct);

        var targetPubId = await context.WorkflowDefinitions
            .IgnoreQueryFilters()
            .Where(d => d.Id == migration.TargetDefinitionId)
            .Select(d => d.PublicId)
            .FirstOrDefaultAsync(ct);

        await mediator.Publish(new WorkflowInstancesMigratedEvent(
            sourcePubId,
            targetPubId,
            migration.TenantId,
            gen.NewIdMonotonic()), ct);

        logger.LogInformation(
            "Migration {MigrationId} finished with status {Status}. Total={Total}, Migrated={Migrated}, Failed={Failed}, Skipped={Skipped}.",
            migrationId, migration.Status, migration.TotalInstances,
            migration.MigratedCount, migration.FailedCount, migration.SkippedCount);
    }

    /// <summary>
    /// Keyset-paginate active (non-completed) instances on the source definition.
    /// Ordered by Id ASC (long) for stable pagination across batches.
    /// sourceDefinitionId = long (T2 WorkflowDefinition internal bigint PK — FK on WorkflowInstance).
    /// afterId = long (last WorkflowInstance.Id from previous batch; 0 for first batch).
    /// </summary>
    private async Task<List<WorkflowInstance>> LoadNextBatchAsync(
        long sourceDefinitionId,
        long afterId,
        CancellationToken ct) =>
        // IgnoreQueryFilters: Hangfire context has no tenant; explicit IsDeleted check retained
        await context.WorkflowInstances
            .IgnoreQueryFilters()
            .Where(i => i.DefinitionId == sourceDefinitionId
                     && i.CompletedAt == null
                     && !i.IsDeleted
                     && i.Id > afterId)
            .OrderBy(i => i.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

    /// <summary>
    /// Parses StateMappingsJson into a Dictionary&lt;long, long&gt; for O(1) lookup per instance.
    /// Expected format: [{oldStateId, newStateId}] — values serialized as long integers.
    /// Phase-4b B4c: T1 WorkflowState.Id is bigint — mapped as long not Guid.
    /// </summary>
    private static Dictionary<long, long> ParseStateMappings(string json)
    {
        var entries = JsonSerializer.Deserialize<List<StateMappingEntry>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("StateMappingsJson deserialized to null.");

        return entries.ToDictionary(e => e.OldStateId, e => e.NewStateId);
    }

    // Internal DTO matching the JSON stored in StateMappingsJson
    // OldStateId/NewStateId are long (T1 WorkflowState.Id bigint PK — Phase-4b B4c)
    private sealed record StateMappingEntry(long OldStateId, long NewStateId);
}
