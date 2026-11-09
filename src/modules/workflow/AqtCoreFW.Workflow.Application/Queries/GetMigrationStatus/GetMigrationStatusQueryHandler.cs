using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateVersionMigration;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetMigrationStatus;

/// <summary>
/// Loads migration via EF repository; enriches with live Hangfire job state.
/// Null-safe for HangfireJobId (migration not yet executed).
/// </summary>
public sealed class GetMigrationStatusQueryHandler(
    IWorkflowVersionMigrationRepository repository,
    IHangfireJobStateProvider hangfireState)
    : IRequestHandler<GetMigrationStatusQuery, Result<MigrationDto>>
{
    public async Task<Result<MigrationDto>> Handle(
        GetMigrationStatusQuery request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetByIdAsync(request.MigrationId, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(new NotFoundError(
                $"Migration '{request.MigrationId}' not found."));

        var migration = result.Value;
        var dto = CreateVersionMigrationCommandHandler.MapToDto(migration);
        dto.HangfireJobState = hangfireState.GetJobState(migration.HangfireJobId);

        return Result.Ok(dto);
    }
}
