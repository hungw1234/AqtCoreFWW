using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ValidateMigrationMapping;

/// <summary>
/// Handles dry-run mapping validation.
/// Checks: same tenant, same DefinitionKey, target active, version direction,
/// no branch-status instances on source (V1 scope cut), unmapped states with active instance counts.
/// Does NOT persist anything.
/// </summary>
public sealed class ValidateMigrationMappingCommandHandler(
    IWorkflowDefinitionRepository definitionRepository,
    IWorkflowInstanceRepository instanceRepository)
    : IRequestHandler<ValidateMigrationMappingCommand, Result<MigrationValidationResult>>
{
    public async Task<Result<MigrationValidationResult>> Handle(
        ValidateMigrationMappingCommand request,
        CancellationToken cancellationToken)
    {
        // Load source + target definitions with their states
        var sourceResult = await definitionRepository.GetWithStatesAndTransitionsAsync(
            request.SourceDefinitionId, cancellationToken);
        if (sourceResult.IsFailed)
            return Result.Fail($"Source definition '{request.SourceDefinitionId}' not found.");

        var targetResult = await definitionRepository.GetWithStatesAndTransitionsAsync(
            request.TargetDefinitionId, cancellationToken);
        if (targetResult.IsFailed)
            return Result.Fail($"Target definition '{request.TargetDefinitionId}' not found.");

        var source = sourceResult.Value;
        var target = targetResult.Value;
        var validation = new MigrationValidationResult { IsValid = true };

        // Cross-tenant guard
        if (source.TenantId != request.TenantId || target.TenantId != request.TenantId)
        {
            validation.Errors.Add("Source and target definitions must belong to the requesting tenant.");
            validation.IsValid = false;
        }

        // Same DefinitionKey guard (migrations only between versions of the same workflow)
        if (source.DefinitionKey != target.DefinitionKey)
        {
            validation.Errors.Add(
                $"Source key '{source.DefinitionKey}' and target key '{target.DefinitionKey}' must match.");
            validation.IsValid = false;
        }

        // Target must be active
        if (!target.IsActive)
        {
            validation.Errors.Add($"Target definition '{request.TargetDefinitionId}' is not active.");
            validation.IsValid = false;
        }

        // Version direction: downgrade rejected unless explicitly allowed
        if (!request.AllowDowngrade && target.Version <= source.Version)
        {
            validation.Errors.Add(
                $"Target version {target.Version} is not greater than source version {source.Version}. " +
                "Set AllowDowngrade=true to permit downgrade.");
            validation.IsValid = false;
        }

        // Abort early on structural errors — instance checks cannot be evaluated reliably
        if (!validation.IsValid)
            return Result.Ok(validation);

        // V1 scope cut: reject if any instance has an active parallel branch
        var hasBranchInstances = await instanceRepository.HasInstancesWithActiveBranchAsync(
            request.SourceDefinitionId, cancellationToken);
        if (hasBranchInstances)
        {
            validation.Errors.Add(
                "Source definition has instances with active parallel branches. " +
                "These instances are ineligible for migration in v1 (scope cut).");
            validation.IsValid = false;
        }

        // Count active instances per state to find unmapped states.
        // GetActiveInstanceCountByStateAsync takes long (T2 definition internal Id bigint PK).
        // source.Id is the internal long PK loaded from the definition entity.
        var instanceCountByState = await instanceRepository.GetActiveInstanceCountByStateAsync(
            source.Id, cancellationToken);

        var mappedOldStateIds = request.StateMappings
            .Select(m => m.OldStateId)
            .ToHashSet();

        foreach (var state in source.States)
        {
            if (mappedOldStateIds.Contains(state.Id)) continue;
            if (!instanceCountByState.TryGetValue(state.Id, out var count) || count == 0) continue;

            validation.UnmappedStates.Add(new UnmappedStateDto
            {
                StateId = state.Id,
                StateName = state.Name,
                ActiveInstanceCount = count
            });
        }

        if (validation.UnmappedStates.Count > 0)
        {
            validation.Errors.Add(
                $"{validation.UnmappedStates.Count} source state(s) have active instances but are not mapped.");
            validation.IsValid = false;
        }

        return Result.Ok(validation);
    }
}
