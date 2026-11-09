using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.UpdateReportDefinition;

public sealed class UpdateReportDefinitionCommandHandler(IReportDefinitionRepository repository)
    : IRequestHandler<UpdateReportDefinitionCommand, Result>
{
    public async Task<Result> Handle(
        UpdateReportDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        // T2 entity: look up by PublicId (Guid) — the external addressing key
        var definition = await repository.GetByPublicIdAsync(request.Id, cancellationToken);
        if (definition is null)
            return Result.Fail(ReportingErrors.DefinitionNotFound(request.Id));

        if (!SqlValidationHelper.ContainsTenantIdParam(request.SqlTemplate))
            return Result.Fail(ReportingErrors.MissingTenantFilter());

        if (!SqlValidationHelper.StartsWithSelect(request.SqlTemplate))
            return Result.Fail(ReportingErrors.DangerousSql("SQL must start with SELECT"));

        var dangerousKeyword = SqlValidationHelper.FindDangerousKeyword(request.SqlTemplate);
        if (dangerousKeyword is not null)
            return Result.Fail(ReportingErrors.DangerousSql(dangerousKeyword));

        definition.UpdateDefinition(
            request.Name,
            request.NameVi,
            request.Description,
            request.SqlTemplate,
            request.ParametersSchema,
            request.OutputFormat);

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
