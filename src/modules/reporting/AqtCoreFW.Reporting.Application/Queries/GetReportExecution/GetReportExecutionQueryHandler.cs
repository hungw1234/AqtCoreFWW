using AqtCoreFW.Reporting.Application.DTOs;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetReportExecution;

/// <summary>
/// T2 handler: returns PublicId (Guid) for both execution and its definition.
/// ReportExecution.ReportDefinitionId is a long FK — we resolve the definition's PublicId
/// via GetByInternalIdAsync so the DTO never leaks internal long PKs.
/// </summary>
public sealed class GetReportExecutionQueryHandler(
    IReportExecutionRepository repository,
    IReportDefinitionRepository definitionRepository)
    : IRequestHandler<GetReportExecutionQuery, Result<ReportExecutionDto>>
{
    public async Task<Result<ReportExecutionDto>> Handle(
        GetReportExecutionQuery request,
        CancellationToken cancellationToken)
    {
        // T2: look up execution by PublicId (Guid)
        var execution = await repository.GetByPublicIdAsync(request.ExecutionId, cancellationToken);
        if (execution is null)
            return Result.Fail(ReportingErrors.ExecutionNotFound(request.ExecutionId));

        // Tenant isolation: verify caller owns this execution (SystemAdmin bypasses via null)
        if (request.RequestedBy.HasValue && execution.RequestedBy != request.RequestedBy.Value)
            return Result.Fail(new ForbiddenError("Access denied to this report execution."));

        // Resolve the definition's PublicId from its internal long FK
        // to avoid leaking the internal long PK in the DTO.
        var definition = await definitionRepository.GetByInternalIdAsync(
            execution.ReportDefinitionId, cancellationToken);

        // Definition PublicId for DTO — use Guid.Empty sentinel if definition was deleted
        var definitionPublicId = definition?.PublicId ?? Guid.Empty;

        // T2: expose PublicId (Guid) as external Id — never the long internal PK
        return Result.Ok(new ReportExecutionDto(
            execution.PublicId,
            definitionPublicId,
            execution.Parameters,
            execution.RequestedBy,
            execution.RequestedAt,
            execution.Status,
            execution.StartedAt,
            execution.CompletedAt,
            HasResult: !string.IsNullOrEmpty(execution.ResultFilePath),
            execution.ErrorMessage));
    }
}
