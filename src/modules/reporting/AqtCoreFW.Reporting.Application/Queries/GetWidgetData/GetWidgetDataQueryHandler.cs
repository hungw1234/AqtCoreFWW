using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Application.DTOs;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetWidgetData;

/// <summary>
/// Executes a widget's DataQuery via Dapper and returns chart-ready WidgetDataDto.
/// SECURITY:
///   - SQL re-validated on every execution (defense-in-depth — stored SQL may have been injected at DB level).
///   - TenantId always injected server-side; never taken from caller params.
///   - 10-second command timeout prevents long-running widget queries.
///   - DataQuery must start with SELECT and contain no dangerous DDL/DML keywords.
/// </summary>
public sealed class GetWidgetDataQueryHandler(
    IReportWidgetRepository widgetRepository,
    IReadDbConnection db)
    : IRequestHandler<GetWidgetDataQuery, Result<WidgetDataDto>>
{
    private const int WidgetQueryTimeoutSeconds = 10;

    public async Task<Result<WidgetDataDto>> Handle(
        GetWidgetDataQuery request,
        CancellationToken cancellationToken)
    {
        var widget = await widgetRepository.GetByIdAsync(request.WidgetId, cancellationToken);
        if (widget is null)
            return Result.Fail(new NotFoundError($"ReportWidget '{request.WidgetId}' not found."));

        // Tenant isolation — widget must belong to caller's tenant
        if (widget.TenantId != request.TenantId)
            return Result.Fail(new ForbiddenError("Widget does not belong to the caller's tenant."));

        // Defense-in-depth: re-validate stored SQL before execution
        if (!SqlValidationHelper.StartsWithSelect(widget.DataQuery))
            return Result.Fail(ReportingErrors.DangerousSql("SQL must start with SELECT"));

        var dangerousKeyword = SqlValidationHelper.FindDangerousKeyword(widget.DataQuery);
        if (dangerousKeyword is not null)
            return Result.Fail(ReportingErrors.DangerousSql(dangerousKeyword));

        // TenantId injected server-side into parameterized query
        var parameters = new { TenantId = request.TenantId };

        var rows = await db.QueryAsync<dynamic>(
            widget.DataQuery,
            parameters,
            cancellationToken,
            commandTimeout: WidgetQueryTimeoutSeconds);

        var rowList = rows
            .Select(row => (IReadOnlyDictionary<string, object?>)
                ((IDictionary<string, object>)row)
                    .ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
            .ToList();

        var columns = rowList.Count > 0
            ? rowList[0].Keys.ToList()
            : new List<string>();

        var metadata = new Dictionary<string, string>
        {
            ["widgetId"] = widget.Id.ToString(),
            ["widgetType"] = widget.Type.ToString(),
            ["lastRefreshed"] = DateTimeOffset.UtcNow.ToString("O"),
            ["rowCount"] = rowList.Count.ToString()
        };

        return Result.Ok(new WidgetDataDto(columns, rowList, metadata));
    }
}
