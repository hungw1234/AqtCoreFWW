using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateVersionMigration;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CancelVersionMigration;

/// <summary>
/// Cancels a migration with status-aware transitions:
/// - PendingApproval | Approved → immediate MarkCancelled() (no Hangfire worker running)
/// - InProgress → RequestCancel() sets CancelRequested=true; worker polls between batches
/// - Completed | Failed | Cancelled → rejected (terminal states)
/// </summary>
public sealed class CancelVersionMigrationCommandHandler(
    IWorkflowVersionMigrationRepository migrationRepository)
    : IRequestHandler<CancelVersionMigrationCommand, Result<MigrationDto>>
{
    public async Task<Result<MigrationDto>> Handle(
        CancelVersionMigrationCommand request,
        CancellationToken cancellationToken)
    {
        var loadResult = await migrationRepository.GetByIdAsync(request.MigrationId, cancellationToken);
        if (loadResult.IsFailed)
            return Result.Fail(loadResult.Errors);

        var migration = loadResult.Value;

        // Tenant isolation
        if (migration.TenantId != request.TenantId)
            return Result.Fail(new ForbiddenError("Migration does not belong to the requesting tenant."));

        switch (migration.Status)
        {
            case MigrationStatus.PendingApproval:
            case MigrationStatus.Approved:
                // No worker running — cancel immediately
                migration.MarkCancelled();
                break;

            case MigrationStatus.InProgress:
                // Worker is active — signal it to stop between batches
                migration.RequestCancel();
                break;

            default:
                // Completed, Failed, Cancelled — terminal states, cannot cancel
                return Result.Fail(new ValidationError(
                    $"Cannot cancel migration in status '{migration.Status}'. " +
                    "Only PendingApproval, Approved, or InProgress migrations can be cancelled."));
        }

        await migrationRepository.UpdateAsync(migration, cancellationToken);
        return Result.Ok(CreateVersionMigrationCommandHandler.MapToDto(migration));
    }
}
