using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.ExecuteSavedQuery;

/// <summary>
/// Executes a stored QueryCatalogEntry SQL via Dapper.
/// SECURITY: validates stored SQL against dangerous keyword blocklist before execution.
/// TenantId is always injected server-side — never trusted from caller params.
/// E-05: RequiredRole is enforced before SQL execution — unauthorized users receive ForbiddenError.
/// </summary>
public sealed class ExecuteSavedQueryQueryHandler(
    IQueryCatalogRepository queryCatalogRepository,
    IReadDbConnection db,
    ICurrentUser currentUser)
    : IRequestHandler<ExecuteSavedQueryQuery, Result<IReadOnlyList<Dictionary<string, object?>>>>
{
    public async Task<Result<IReadOnlyList<Dictionary<string, object?>>>> Handle(
        ExecuteSavedQueryQuery request,
        CancellationToken cancellationToken)
    {
        // Fetch full entry — needed for RequiredRole and DapperSql
        var entry = await queryCatalogRepository.GetByIdAsync(request.EntryId, cancellationToken);

        if (entry is null || !entry.IsActive)
            return Result.Fail(ReportingErrors.QueryCatalogEntryNotFound(request.EntryId.ToString()));

        // E-05: Enforce RequiredRole before executing the query
        if (!string.IsNullOrEmpty(entry.RequiredRole) &&
            !currentUser.Roles.Any(r => r.Equals(entry.RequiredRole, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Fail(new ForbiddenError(
                $"Query '{entry.Code}' requires role '{entry.RequiredRole}'."));
        }

        // Re-validate stored SQL (defense-in-depth)
        var dangerousKeyword = SqlValidationHelper.FindDangerousKeyword(entry.DapperSql);
        if (dangerousKeyword is not null)
            return Result.Fail(ReportingErrors.DangerousSql(dangerousKeyword));

        var mergedParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in request.Parameters)
            mergedParams[kvp.Key] = kvp.Value;
        mergedParams["TenantId"] = request.TenantId;

        var rows = await db.QueryAsync<dynamic>(entry.DapperSql, mergedParams, cancellationToken);

        var result = rows
            .Select(row => ((IDictionary<string, object>)row)
                .ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
            .ToList();

        return Result.Ok<IReadOnlyList<Dictionary<string, object?>>>(result);
    }
}
