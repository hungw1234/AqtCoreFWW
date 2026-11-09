using AqtCoreFW.Reporting.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.UpdateReportDefinition;

public record UpdateReportDefinitionCommand(
    Guid Id,
    string Name,
    string NameVi,
    string Description,
    string SqlTemplate,
    string ParametersSchema,
    OutputFormat OutputFormat) : IRequest<Result>;
