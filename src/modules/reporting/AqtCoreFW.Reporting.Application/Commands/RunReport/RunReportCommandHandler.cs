using AqtCoreFW.Reporting.Application.Jobs;
using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.RunReport;

/// <summary>
/// Creates a ReportExecution record (Queued) and enqueues Hangfire job.
/// Returns executionId (PublicId Guid) immediately — caller polls GET /executions/{id} for status.
/// T2 entities: ReportDefinition addressed by PublicId (Guid), ReportExecution returns PublicId.
/// </summary>
public sealed class RunReportCommandHandler(
    IReportDefinitionRepository definitionRepo,
    IReportExecutionRepository executionRepo,
    IBackgroundJobService backgroundJobs,
    IGuidGenerator guidGenerator)
    : IRequestHandler<RunReportCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(RunReportCommand request, CancellationToken cancellationToken)
    {
        // T2: look up definition by PublicId (Guid) — the external addressing key
        var definition = await definitionRepo.GetByPublicIdAsync(request.ReportDefinitionId, cancellationToken);
        if (definition is null)
            return Result.Fail(ReportingErrors.DefinitionNotFound(request.ReportDefinitionId));

        if (!definition.IsActive)
            return Result.Fail(new Error("ReportDefinition is inactive."));

        // T2: ReportExecution.Create needs IGuidGenerator + long FK to definition
        // definition.Id = long internal PK (correct FK for ReportDefinitionId column)
        var execution = ReportExecution.Create(
            guidGenerator,
            definition.Id,
            request.ParametersJson,
            request.RequestedBy);

        await executionRepo.AddAsync(execution, cancellationToken);

        // Enqueue Hangfire job with PublicId (Guid) — job uses GetByPublicIdAsync internally
        backgroundJobs.Enqueue<IExecuteReportJob>(
            job => job.ExecuteAsync(execution.PublicId, CancellationToken.None));

        // T2: return PublicId (Guid AK) — not the long internal PK
        return Result.Ok(execution.PublicId);
    }
}
