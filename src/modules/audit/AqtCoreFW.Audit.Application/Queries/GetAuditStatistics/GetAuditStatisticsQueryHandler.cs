using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Audit.Application.Queries.GetAuditStatistics;

public sealed class GetAuditStatisticsQueryHandler(
    [FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection db,
    ITenantContext tenantContext)
    : IRequestHandler<GetAuditStatisticsQuery, Result<AuditStatisticsDto>>
{
    public async Task<Result<AuditStatisticsDto>> Handle(
        GetAuditStatisticsQuery request,
        CancellationToken cancellationToken)
    {
        var p = new DynamicParameters();
        var tenantFilter = string.Empty;
        if (request.TenantId.HasValue)
        {
            tenantFilter = "WHERE TenantId = @TenantId";
            p.Add("TenantId", request.TenantId);
        }

        var countSql = $"""
            SELECT
                SUM(CASE WHEN OccurredAt >= CAST(GETUTCDATE() AS DATE) THEN 1 ELSE 0 END) AS TodayTotal,
                SUM(CASE WHEN OccurredAt >= DATEADD(day, -7, GETUTCDATE()) THEN 1 ELSE 0 END) AS WeekTotal,
                SUM(CASE WHEN OccurredAt >= DATEADD(day, -30, GETUTCDATE()) THEN 1 ELSE 0 END) AS MonthTotal
            FROM audit.AuditLogEntries {tenantFilter}
            """;

        var byActionSql = $"""
            SELECT Action, COUNT(*) AS Count
            FROM audit.AuditLogEntries {tenantFilter}
            WHERE OccurredAt >= DATEADD(day, -30, GETUTCDATE())
            GROUP BY Action ORDER BY Count DESC
            """;

        var byModuleSql = $"""
            SELECT ModuleName AS Module, COUNT(*) AS Count
            FROM audit.AuditLogEntries {tenantFilter}
            WHERE OccurredAt >= DATEADD(day, -30, GETUTCDATE())
            GROUP BY ModuleName ORDER BY Count DESC
            """;

        // RLS: audit_admin_bypass — admin read of system/global audit rows
        // When no TenantId filter is applied (system-wide stats), a SystemAdmin must also
        // see NULL-tenant rows. The bypass variant sets audit_admin_bypass='true' on a
        // single dedicated connection so the RLS FILTER exposes those rows.
        // Non-admin callers always filter by TenantId and never need NULL-tenant rows.
        if (tenantContext.IsSystemAdmin)
        {
            var adminCounts = await db.QuerySingleWithAuditAdminBypassAsync<(int TodayTotal, int WeekTotal, int MonthTotal)>(
                countSql, p, cancellationToken);
            var byAction = await db.QueryWithAuditAdminBypassAsync<ActionSummary>(byActionSql, p, cancellationToken);
            var byModule = await db.QueryWithAuditAdminBypassAsync<ModuleSummary>(byModuleSql, p, cancellationToken);

            return Result.Ok(new AuditStatisticsDto(
                adminCounts.TodayTotal,
                adminCounts.WeekTotal,
                adminCounts.MonthTotal,
                byAction.ToList(),
                byModule.ToList()));
        }

        var stdCounts = await db.QueryFirstOrDefaultAsync<(int TodayTotal, int WeekTotal, int MonthTotal)>(countSql, p, cancellationToken);
        var stdByAction = await db.QueryAsync<ActionSummary>(byActionSql, p, cancellationToken);
        var stdByModule = await db.QueryAsync<ModuleSummary>(byModuleSql, p, cancellationToken);

        return Result.Ok(new AuditStatisticsDto(
            stdCounts.TodayTotal,
            stdCounts.WeekTotal,
            stdCounts.MonthTotal,
            stdByAction.ToList(),
            stdByModule.ToList()));
    }
}
