using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.SaveQuery;

/// <summary>
/// Persists a new QueryCatalogEntry.
/// SECURITY: @TenantId must be present in DapperSql — tenant isolation.
/// H-03: Added SQL validation (SELECT-only, no dangerous keywords) at save time.
/// </summary>
public sealed class SaveQueryCommandHandler(IQueryCatalogRepository repository)
    : IRequestHandler<SaveQueryCommand, Result<long>>
{
    public async Task<Result<long>> Handle(SaveQueryCommand request, CancellationToken cancellationToken)
    {
        if (!request.DapperSql.Contains("@TenantId", StringComparison.OrdinalIgnoreCase))
            return Result.Fail(ReportingErrors.MissingTenantFilter());

        // H-03: Validate SQL at save time (defense-in-depth — also validated at execution time)
        if (!SqlValidationHelper.StartsWithSelect(request.DapperSql))
            return Result.Fail(new ValidationError("Query must start with SELECT."));

        var dangerous = SqlValidationHelper.FindDangerousKeyword(request.DapperSql);
        if (dangerous is not null)
            return Result.Fail(new ValidationError($"Query contains dangerous keyword: {dangerous}"));

        var entry = QueryCatalogEntry.Create(
            request.Code,
            request.Description,
            request.DescriptionVi,
            request.DapperSql,
            request.RequiredParams,
            request.OptionalParams,
            request.RequiredRole);

        await repository.AddAsync(entry, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Ok(entry.Id);
    }
}
