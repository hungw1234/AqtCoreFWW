using AqtCoreFW.Reporting.Application.DTOs;
using AqtCoreFW.Reporting.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetReportDefinitions;

public sealed class GetReportDefinitionsQueryHandler(IReportDefinitionRepository repository)
    : IRequestHandler<GetReportDefinitionsQuery, Result<IReadOnlyList<ReportDefinitionDto>>>
{
    public async Task<Result<IReadOnlyList<ReportDefinitionDto>>> Handle(
        GetReportDefinitionsQuery request,
        CancellationToken cancellationToken)
    {
        var definitions = await repository.GetAllAsync(request.TenantId, cancellationToken);

        // T2 entity: expose PublicId (Guid AK) as the external Id — never the long internal PK
        var dtos = definitions
            .Select(d => new ReportDefinitionDto(
                d.PublicId, d.Name, d.NameVi, d.Description,
                d.ParametersSchema, d.OutputFormat,
                d.IsActive, d.TenantId, d.CreatedAt))
            .ToList();

        return Result.Ok<IReadOnlyList<ReportDefinitionDto>>(dtos);
    }
}
