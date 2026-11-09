using AqtCoreFW.Reporting.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.RunReport;

/// <summary>
/// Queues a report execution job and returns the executionId immediately (202 Accepted pattern).
/// The actual SQL + file generation runs in Hangfire (ExecuteReportJob).
/// </summary>
public record RunReportCommand(
    Guid ReportDefinitionId,
    string ParametersJson,
    string TenantId,
    Guid RequestedBy,
    OutputFormat? FormatOverride = null) : IRequest<Result<Guid>>;
