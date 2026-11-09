using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateVersionMigration;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ApproveVersionMigration;

/// <summary>
/// Approves a migration. Enforces:
/// - Status must be PendingApproval
/// - 4-eyes: ApproverId != migration.CreatedBy (GOV requirement)
/// - MappingHash re-verified to detect tampering since creation
/// Sets Status=Approved, ApprovedBy, ApprovedAt.
/// </summary>
public sealed class ApproveVersionMigrationCommandHandler(
    IWorkflowVersionMigrationRepository migrationRepository)
    : IRequestHandler<ApproveVersionMigrationCommand, Result<MigrationDto>>
{
    public async Task<Result<MigrationDto>> Handle(
        ApproveVersionMigrationCommand request,
        CancellationToken cancellationToken)
    {
        var loadResult = await migrationRepository.GetByIdAsync(request.MigrationId, cancellationToken);
        if (loadResult.IsFailed)
            return Result.Fail(loadResult.Errors);

        var migration = loadResult.Value;

        // Tenant isolation
        if (migration.TenantId != request.TenantId)
            return Result.Fail(new ForbiddenError("Migration does not belong to the requesting tenant."));

        // Status guard — must be PendingApproval
        if (migration.Status != Domain.Enums.MigrationStatus.PendingApproval)
            return Result.Fail(new ValidationError(
                $"Cannot approve migration in status '{migration.Status}'. Must be PendingApproval."));

        // 4-eyes GOV check: approver must differ from creator
        if (request.ApproverId == migration.CreatedBy)
            return Result.Fail(new ForbiddenError(
                "migration.error.self_approval: Approver cannot be the same as the creator (4-eyes requirement)."));

        // Tamper detection: re-verify MappingHash matches current StateMappingsJson
        try
        {
            migration.VerifyMappingHash();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(new ValidationError(
                $"MappingHash verification failed — mapping may have been tampered with. {ex.Message}"));
        }

        // Approve
        migration.MarkApproved(request.ApproverId);
        await migrationRepository.UpdateAsync(migration, cancellationToken);

        return Result.Ok(CreateVersionMigrationCommandHandler.MapToDto(migration));
    }
}
