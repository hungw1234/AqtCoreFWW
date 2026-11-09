using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.CreateReportDefinition;

/// <summary>
/// Creates a new ReportDefinition.
/// SECURITY: validates SQL template has @TenantId, starts with SELECT, no dangerous DDL/DML.
/// T2 entity: returns PublicId (Guid v7 monotonic AK) — never the long internal PK.
/// </summary>
public sealed class CreateReportDefinitionCommandHandler(
    IReportDefinitionRepository repository,
    IGuidGenerator guidGenerator)
    : IRequestHandler<CreateReportDefinitionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateReportDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        if (!SqlValidationHelper.ContainsTenantIdParam(request.SqlTemplate))
            return Result.Fail(ReportingErrors.MissingTenantFilter());

        if (!SqlValidationHelper.StartsWithSelect(request.SqlTemplate))
            return Result.Fail(ReportingErrors.DangerousSql("SQL must start with SELECT"));

        var dangerousKeyword = SqlValidationHelper.FindDangerousKeyword(request.SqlTemplate);
        if (dangerousKeyword is not null)
            return Result.Fail(ReportingErrors.DangerousSql(dangerousKeyword));

        // T2: inject IGuidGenerator for v7 monotonic PublicId generation
        var definition = ReportDefinition.Create(
            guidGenerator,
            request.Name,
            request.NameVi,
            request.Description,
            request.SqlTemplate,
            request.ParametersSchema,
            request.OutputFormat,
            request.TenantId);

        await repository.AddAsync(definition, cancellationToken);

        // T2: return PublicId (Guid AK) — not the long internal PK
        return Result.Ok(definition.PublicId);
    }
}
