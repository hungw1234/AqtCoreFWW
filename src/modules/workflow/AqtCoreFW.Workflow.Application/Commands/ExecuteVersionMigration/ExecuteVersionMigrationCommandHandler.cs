using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateVersionMigration;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ExecuteVersionMigration;

/// <summary>
/// Transitions an Approved migration to InProgress and enqueues the Hangfire batch executor.
/// Defense-in-depth: re-verifies MappingHash before enqueue (detects tampering between Approve and Execute).
/// Uses IWorkflowJobEnqueuer abstraction to avoid Hangfire dependency in Application layer.
/// </summary>
public sealed class ExecuteVersionMigrationCommandHandler(
    IWorkflowVersionMigrationRepository migrationRepository,
    IWorkflowJobEnqueuer jobEnqueuer)
    : IRequestHandler<ExecuteVersionMigrationCommand, Result<MigrationDto>>
{
    public async Task<Result<MigrationDto>> Handle(
        ExecuteVersionMigrationCommand request,
        CancellationToken cancellationToken)
    {
        var loadResult = await migrationRepository.GetByIdAsync(request.MigrationId, cancellationToken);
        if (loadResult.IsFailed)
            return Result.Fail(loadResult.Errors);

        var migration = loadResult.Value;

        // Tenant isolation
        if (migration.TenantId != request.TenantId)
            return Result.Fail(new ForbiddenError("Migration does not belong to the requesting tenant."));

        // Status guard — must be Approved
        if (migration.Status != MigrationStatus.Approved)
            return Result.Fail(new ValidationError(
                $"Cannot execute migration in status '{migration.Status}'. Must be Approved."));

        // Defense-in-depth hash re-verify (detects tampering between Approve and Execute)
        try
        {
            migration.VerifyMappingHash();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(new ValidationError(
                $"MappingHash verification failed at execute stage — mapping may have been tampered. {ex.Message}"));
        }

        // Enqueue Hangfire batch job via application-layer abstraction; returns string job ID
        var jobId = jobEnqueuer.EnqueueMigrationExecution(request.MigrationId);

        // Transition to InProgress; store Hangfire job ID
        migration.MarkStarted(jobId);
        await migrationRepository.UpdateAsync(migration, cancellationToken);

        return Result.Ok(CreateVersionMigrationCommandHandler.MapToDto(migration));
    }
}
