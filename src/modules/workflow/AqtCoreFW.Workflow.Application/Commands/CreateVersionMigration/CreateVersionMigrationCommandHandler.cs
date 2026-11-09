using System.Text.Json;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.ValidateMigrationMapping;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateVersionMigration;

/// <summary>
/// Creates a WorkflowVersionMigration at PendingApproval status.
/// Steps: acquire advisory lock → resolve definition long IDs (T2 PublicId → internal Id) →
///        reject duplicate pending → rate-limit check → validate mapping (via mediator) →
///        serialize JSON → create entity → persist.
/// Phase-4b B4c: SourceDefinitionId/TargetDefinitionId in command are Guid PublicIds (T2 AK);
///   WorkflowVersionMigration.Create and GetActiveOnSourceAsync both take long (internal FK).
///   Resolve by loading definitions by PublicId first.
/// </summary>
public sealed class CreateVersionMigrationCommandHandler(
    IWorkflowVersionMigrationRepository migrationRepository,
    IWorkflowDefinitionRepository definitionRepository,
    ISourceDefinitionLockService lockService,
    IMediator mediator)
    : IRequestHandler<CreateVersionMigrationCommand, Result<MigrationDto>>
{
    public async Task<Result<MigrationDto>> Handle(
        CreateVersionMigrationCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1: Acquire distributed advisory lock on source definition (5 s timeout)
        var acquired = await lockService.AcquireAsync(
            request.SourceDefinitionId,
            TimeSpan.FromSeconds(5),
            cancellationToken);

        if (!acquired)
            return Result.Fail(new ConflictError(
                $"Could not acquire lock on source definition '{request.SourceDefinitionId}'. " +
                "Another migration may be in progress. Retry in a few seconds."));

        // Step 2: Resolve PublicIds → internal long Ids (T2 entity pattern)
        var sourceDefResult = await definitionRepository.GetWithStatesAndTransitionsAsync(
            request.SourceDefinitionId, cancellationToken);
        if (sourceDefResult.IsFailed)
            return Result.Fail(sourceDefResult.Errors);

        var targetDefResult = await definitionRepository.GetWithStatesAndTransitionsAsync(
            request.TargetDefinitionId, cancellationToken);
        if (targetDefResult.IsFailed)
            return Result.Fail(targetDefResult.Errors);

        var sourceInternalId = sourceDefResult.Value.Id;   // long internal PK
        var targetInternalId = targetDefResult.Value.Id;   // long internal PK

        // Step 3: Reject if an active migration already exists on this source definition
        var existing = await migrationRepository.GetActiveOnSourceAsync(
            sourceInternalId, request.TenantId, cancellationToken);
        if (existing is not null)
            return Result.Fail(new ConflictError(
                $"An active migration (Id={existing.Id}, Status={existing.Status}) " +
                $"already exists for source definition '{request.SourceDefinitionId}'."));

        // Step 4: Tenant rate-limit — max 3 active migrations per tenant
        var activeCount = await migrationRepository.CountActiveByTenantAsync(
            request.TenantId, cancellationToken);
        if (activeCount >= 3)
            return Result.Fail(new ValidationError(
                $"Tenant '{request.TenantId}' already has {activeCount} active migrations. " +
                "Maximum 3 concurrent active migrations allowed (rate limit M10)."));

        // Step 5: Run validation dry-run via mediator (reuses ValidateMigrationMappingCommand)
        var validateCmd = new ValidateMigrationMappingCommand(
            request.SourceDefinitionId,
            request.TargetDefinitionId,
            request.StateMappings,
            request.TenantId,
            request.AllowDowngrade);

        var validateResult = await mediator.Send(validateCmd, cancellationToken);
        if (validateResult.IsFailed)
            return Result.Fail(validateResult.Errors);

        var validation = validateResult.Value;
        if (!validation.IsValid)
        {
            var errorMsg = string.Join("; ", validation.Errors);
            return Result.Fail(new ValidationError($"Migration mapping invalid: {errorMsg}"));
        }

        // Step 6: Serialize mappings — canonical JSON for deterministic hash
        var mappingRecords = request.StateMappings
            .OrderBy(m => m.OldStateId)
            .Select(m => new { oldStateId = m.OldStateId, newStateId = m.NewStateId })
            .ToList();
        var stateMappingsJson = JsonSerializer.Serialize(mappingRecords);

        // Step 7: Create entity using internal long IDs (T1 entity stores FK long, not PublicId)
        var migration = WorkflowVersionMigration.Create(
            sourceInternalId,
            targetInternalId,
            stateMappingsJson,
            request.TenantId,
            request.CreatedBy);

        await migrationRepository.AddAsync(migration, cancellationToken);

        return Result.Ok(MapToDto(migration));
    }

    internal static MigrationDto MapToDto(WorkflowVersionMigration m) => new()
    {
        Id = m.Id,
        SourceDefinitionId = m.SourceDefinitionId,
        TargetDefinitionId = m.TargetDefinitionId,
        Status = m.Status,
        MappingHash = m.MappingHash,
        TotalInstances = m.TotalInstances,
        MigratedCount = m.MigratedCount,
        FailedCount = m.FailedCount,
        SkippedCount = m.SkippedCount,
        CreatedBy = m.CreatedBy,
        ApprovedBy = m.ApprovedBy,
        ApprovedAt = m.ApprovedAt,
        StartedAt = m.StartedAt,
        CompletedAt = m.CompletedAt,
        HangfireJobId = m.HangfireJobId,
        HangfireJobState = null // populated by query handler via IMonitoringApi
    };
}
