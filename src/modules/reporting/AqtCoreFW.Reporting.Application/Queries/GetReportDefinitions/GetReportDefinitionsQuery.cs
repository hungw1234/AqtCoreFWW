using AqtCoreFW.Reporting.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetReportDefinitions;

public record GetReportDefinitionsQuery(string? TenantId) : IRequest<Result<IReadOnlyList<ReportDefinitionDto>>>;
