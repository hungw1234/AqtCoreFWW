using System.Text.Json;
using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Audit.Application.Queries.GetLoginAudit;

public sealed class GetLoginAuditQueryHandler(
    [FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection db,
    IIdentityModuleClient identityClient)
    : IRequestHandler<GetLoginAuditQuery, Result<PagedResult<LoginAttemptDto>>>
{
    public async Task<Result<PagedResult<LoginAttemptDto>>> Handle(
        GetLoginAuditQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var p = new DynamicParameters();
        var clauses = new List<string>();

        if (request.UserId.HasValue) { clauses.Add("UserId = @UserId"); p.Add("UserId", request.UserId); }
        if (request.From.HasValue) { clauses.Add("AttemptedAt >= @From"); p.Add("From", request.From); }
        if (request.To.HasValue) { clauses.Add("AttemptedAt <= @To"); p.Add("To", request.To); }
        if (request.Success.HasValue) { clauses.Add("Success = @Success"); p.Add("Success", request.Success); }

        // Server-side search: LIKE on Email and IpAddress
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var escaped = request.SearchTerm.EscapeSqlLike();
            p.Add("SearchPattern", $"%{escaped}%");
            clauses.Add("(Email LIKE @SearchPattern ESCAPE '\\' OR IpAddress LIKE @SearchPattern ESCAPE '\\')");
        }

        // Tenant isolation: LoginAttempts has no TenantId column and no RLS policy. A
        // non-privileged caller only sees attempts whose owning user lives in their tenant.
        // The tenant's user-id set is resolved through IIdentityModuleClient (no cross-schema
        // SQL — keeps the audit module DB-decoupled from the identity schema) and injected as a
        // single JSON parameter expanded via OPENJSON, which sidesteps the SQL Server ~2100
        // parameter ceiling a plain `IN @list` would hit for large tenants. Attempts with a null
        // UserId (unknown email / pre-resolution probes) are not tenant-attributable: `NULL IN (...)`
        // is UNKNOWN so they are excluded here and stay visible to SystemAdmin (@IsPrivileged) only.
        if (!request.IsPrivileged)
        {
            var tenantUserIds = request.CallerTenantId.HasValue
                ? await identityClient.GetUserIdsInTenantAsync(request.CallerTenantId.Value, cancellationToken)
                : (IReadOnlyList<Guid>)[];

            clauses.Add(
                "la.UserId IN (SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON(@TenantUserIdsJson))");
            p.Add("TenantUserIdsJson", JsonSerializer.Serialize(tenantUserIds));
        }

        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : string.Empty;
        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);

        var total = await db.QuerySingleAsync<int>(
            $"SELECT COUNT(*) FROM audit.LoginAttempts la {where}", p, cancellationToken);

        var rows = await db.QueryAsync<LoginAttemptDto>($"""
            SELECT la.MessageId AS Id, la.UserId, la.Email, la.IpAddress, la.Success,
                   la.FailureReason, la.UserAgent, la.AttemptedAt
            FROM audit.LoginAttempts la {where}
            ORDER BY la.AttemptedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """, p, cancellationToken);

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return Result.Ok(new PagedResult<LoginAttemptDto>(rows.ToList(), total, meta));
    }
}
