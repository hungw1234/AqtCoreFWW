using AqtCoreFW.Organization.Application.DTOs;
using AqtCoreFW.Organization.Application.Queries;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Organization.Infrastructure.CommandHandlers;

/// <summary>
/// Query handlers that require OrgDbContext live here in Infrastructure
/// to avoid Application → Infrastructure circular dependency.
/// Pure Dapper queries (IReadDbConnection only) stay in Application.
/// OrgUnit is T2: GetOrgUnitQuery.Id = PublicId (Guid).
/// </summary>

public sealed class GetOrgTreeQueryHandler(OrgUnitService orgService)
    : IRequestHandler<GetOrgTreeQuery, Result<IReadOnlyList<OrgUnitDto>>>
{
    public async Task<Result<IReadOnlyList<OrgUnitDto>>> Handle(GetOrgTreeQuery req, CancellationToken ct)
        => Result.Ok(await orgService.GetTreeAsync(req.TenantId, ct));
}

public sealed class GetOrgUnitQueryHandler(OrgUnitService orgService)
    : IRequestHandler<GetOrgUnitQuery, Result<OrgUnitDto>>
{
    public async Task<Result<OrgUnitDto>> Handle(GetOrgUnitQuery req, CancellationToken ct)
    {
        // req.Id = PublicId (Guid) — T2 external addressing
        var unit = await orgService.GetByIdAsync(req.Id, req.TenantId, ct);
        return unit is null
            ? Result.Fail(new NotFoundError($"OrgUnit '{req.Id}' not found."))
            : Result.Ok(unit);
    }
}

/// <summary>
/// [RT-03] Refactored: no cross-schema JOIN. Uses IIdentityModuleClient for user enrichment.
/// GetOrgUnitMembersQuery.OrgUnitId = PublicId (Guid) — resolved to internal long for SQL.
/// </summary>
public sealed class GetOrgUnitMembersQueryHandler(
    IReadDbConnection readDb,
    IIdentityModuleClient identityClient)
    : IRequestHandler<GetOrgUnitMembersQuery, Result<IReadOnlyList<UserOrgUnitAssignmentDto>>>
{
    public async Task<Result<IReadOnlyList<UserOrgUnitAssignmentDto>>> Handle(
        GetOrgUnitMembersQuery req, CancellationToken ct)
    {
        // Resolve PublicId → internal Id via subquery in SQL.
        // UserOrgUnitAssignment.OrgUnitId = long FK to OrgUnit.Id.
        const string sql = """
            SELECT a.Id, a.UserId, a.OrgUnitId, a.RoleInOrg, a.ValidFrom, a.ValidTo, a.IsActive
            FROM [organization].UserOrgUnitAssignments a
            INNER JOIN [organization].OrgUnits ou ON ou.Id = a.OrgUnitId
            WHERE ou.PublicId = @OrgUnitPublicId AND a.TenantId = @TenantId
              AND a.IsActive = 1 AND a.IsDeleted = 0
            """;

        var rows = (await readDb.QueryAsync<UserOrgUnitAssignmentDto>(
            sql, new { OrgUnitPublicId = req.OrgUnitId, req.TenantId }, ct)).ToList();

        if (rows.Count == 0)
            return Result.Ok<IReadOnlyList<UserOrgUnitAssignmentDto>>(rows);

        // Enrich with user names/emails via IIdentityModuleClient (cross-module ACL)
        var userIds = rows.Select(r => r.UserId).Distinct();
        var userMap = await identityClient.GetUserInfoByIdsAsync(userIds, req.TenantId, ct);

        var enriched = rows.Select(r =>
        {
            userMap.TryGetValue(r.UserId, out var user);
            return r with { FullName = user?.FullName, Email = user?.Email };
        })
        .OrderBy(r => r.FullName)
        .ToList();

        return Result.Ok<IReadOnlyList<UserOrgUnitAssignmentDto>>(enriched);
    }
}

public sealed class GetStaffHistoryQueryHandler(OrgDbContext db)
    : IRequestHandler<GetStaffHistoryQuery, Result<StaffHistoryDto>>
{
    public async Task<Result<StaffHistoryDto>> Handle(GetStaffHistoryQuery req, CancellationToken ct)
    {
        // T1 entities: Id and OrgUnitId are long — match DTO fields directly
        var assignments = await db.Query<UserOrgUnitAssignment>()
            .Where(x => x.UserId == req.UserId && x.TenantId == req.TenantId)
            .OrderByDescending(x => x.ValidFrom)
            .Select(x => new UserOrgUnitAssignmentDto
            {
                Id = x.Id,
                UserId = x.UserId,
                OrgUnitId = x.OrgUnitId,
                RoleInOrg = x.RoleInOrg,
                ValidFrom = x.ValidFrom,
                ValidTo = x.ValidTo,
                IsActive = x.IsActive,
            })
            .ToListAsync(ct);

        var positions = await db.Query<StaffPositionHistory>()
            .Where(x => x.UserId == req.UserId && x.TenantId == req.TenantId)
            .OrderByDescending(x => x.StartDate)
            .Select(x => new StaffPositionHistoryDto(
                x.Id, x.UserId, x.OrgUnitId, x.PositionTitle, x.StartDate, x.EndDate))
            .ToListAsync(ct);

        return Result.Ok(new StaffHistoryDto(assignments, positions));
    }
}
