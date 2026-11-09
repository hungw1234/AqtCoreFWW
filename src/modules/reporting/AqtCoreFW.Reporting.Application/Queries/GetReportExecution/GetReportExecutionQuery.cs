using AqtCoreFW.Reporting.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetReportExecution;

/// <summary>
/// Get report execution status. RequestedBy filters for tenant isolation
/// (null = SystemAdmin bypass, matches any execution).
/// </summary>
public record GetReportExecutionQuery(
    Guid ExecutionId,
    Guid? RequestedBy = null) : IRequest<Result<ReportExecutionDto>>;
