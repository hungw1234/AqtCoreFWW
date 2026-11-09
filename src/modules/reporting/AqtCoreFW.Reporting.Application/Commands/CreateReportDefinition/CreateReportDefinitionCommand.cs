using AqtCoreFW.Reporting.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.CreateReportDefinition;

public record CreateReportDefinitionCommand(
    string Name,
    string NameVi,
    string Description,
    string SqlTemplate,
    string ParametersSchema,
    OutputFormat OutputFormat,
    string? TenantId) : IRequest<Result<Guid>>;
