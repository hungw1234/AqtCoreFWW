using AqtCoreFW.Workflow.Application.Commands.ValidateMigrationMapping;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateVersionMigration;

/// <summary>
/// Creates a new version migration starting at PendingApproval status.
/// Acquires advisory lock on SourceDefinitionId, validates mapping, creates entity + hash.
/// Permission: WORKFLOW.MIGRATION.CREATE
/// </summary>
public sealed record CreateVersionMigrationCommand(
    Guid SourceDefinitionId,
    Guid TargetDefinitionId,
    IReadOnlyList<StateMappingEntry> StateMappings,
    Guid TenantId,
    Guid CreatedBy,
    bool AllowDowngrade = false) : IRequest<Result<MigrationDto>>;
