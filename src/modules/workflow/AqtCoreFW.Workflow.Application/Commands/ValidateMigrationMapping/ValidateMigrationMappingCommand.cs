using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ValidateMigrationMapping;

/// <summary>
/// Dry-run validation of a proposed state mapping between two definition versions.
/// Returns ValidationResult without persisting anything.
/// Permission: WORKFLOW.MIGRATION.CREATE
/// Phase-4b B4c: SourceDefinitionId/TargetDefinitionId = Guid (T2 WorkflowDefinition.PublicId AK).
///               StateMappingEntry.OldStateId/NewStateId = long (T1 WorkflowState.Id — no Guid AK).
/// </summary>
public sealed record ValidateMigrationMappingCommand(
    Guid SourceDefinitionId,
    Guid TargetDefinitionId,
    IReadOnlyList<StateMappingEntry> StateMappings,
    Guid TenantId,
    bool AllowDowngrade = false) : IRequest<Result<MigrationValidationResult>>;

/// <summary>
/// Maps one state in source → one state in target.
/// OldStateId/NewStateId = long (T1 WorkflowState.Id bigint PK — no Guid AK).
/// </summary>
public sealed record StateMappingEntry(long OldStateId, long NewStateId);
