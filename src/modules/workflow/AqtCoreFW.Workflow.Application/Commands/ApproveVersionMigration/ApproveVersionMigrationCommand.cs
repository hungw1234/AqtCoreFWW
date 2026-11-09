using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ApproveVersionMigration;

/// <summary>
/// Approves a migration at PendingApproval status.
/// Enforces 4-eyes: ApproverId must differ from migration.CreatedBy.
/// Re-verifies MappingHash to detect tampering since creation.
/// Permission: WORKFLOW.MIGRATION.APPROVE
/// </summary>
public sealed record ApproveVersionMigrationCommand(
    long MigrationId,
    Guid ApproverId,
    Guid TenantId) : IRequest<Result<MigrationDto>>;
