using AqtCoreFW.Organization.Application.Commands;
using AqtCoreFW.Organization.Application.DTOs;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Organization.Infrastructure.CommandHandlers;

public sealed class AssignStaffCommandHandler(OrgDbContext db)
    : IRequestHandler<AssignStaffCommand, Result<UserOrgUnitAssignmentDto>>
{
    public async Task<Result<UserOrgUnitAssignmentDto>> Handle(AssignStaffCommand req, CancellationToken ct)
    {
        // req.OrgUnitId = PublicId (Guid) — T2 OrgUnit, resolve to internal long Id
        var orgUnit = await db.Query<OrgUnit>()
            .Where(x => x.PublicId == req.OrgUnitId && x.TenantId == req.TenantId && x.IsActive)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(ct);
        if (orgUnit is null)
            return Result.Fail(new NotFoundError($"Active OrgUnit '{req.OrgUnitId}' not found."));

        // T1 factories accept long orgUnitId
        var assignment = UserOrgUnitAssignment.Create(req.UserId, orgUnit.Id, req.RoleInOrg, req.TenantId);
        db.Assignments.Add(assignment);

        var history = StaffPositionHistory.Create(req.UserId, orgUnit.Id, req.PositionTitle, req.TenantId);
        db.PositionHistories.Add(history);

        await db.SaveChangesAsync(ct);

        return Result.Ok(new UserOrgUnitAssignmentDto
        {
            Id = assignment.Id,
            UserId = assignment.UserId,
            OrgUnitId = assignment.OrgUnitId,
            RoleInOrg = assignment.RoleInOrg,
            ValidFrom = assignment.ValidFrom,
            ValidTo = assignment.ValidTo,
            IsActive = assignment.IsActive,
        });
    }
}

public sealed class TransferStaffCommandHandler(OrgDbContext db)
    : IRequestHandler<TransferStaffCommand, Result<UserOrgUnitAssignmentDto>>
{
    public async Task<Result<UserOrgUnitAssignmentDto>> Handle(TransferStaffCommand req, CancellationToken ct)
    {
        // req.ToOrgUnitId = PublicId (Guid) — T2 OrgUnit, resolve to internal long Id
        var targetUnit = await db.Query<OrgUnit>()
            .Where(x => x.PublicId == req.ToOrgUnitId && x.TenantId == req.TenantId && x.IsActive)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(ct);
        if (targetUnit is null)
            return Result.Fail(new NotFoundError($"Target OrgUnit '{req.ToOrgUnitId}' not found."));

        var now = DateTimeOffset.UtcNow;

        var activeAssignments = await db.TrackingQuery<UserOrgUnitAssignment>()
            .Where(x => x.UserId == req.UserId && x.TenantId == req.TenantId && x.IsActive)
            .ToListAsync(ct);
        foreach (var a in activeAssignments) a.Close(now);

        var openHistories = await db.TrackingQuery<StaffPositionHistory>()
            .Where(x => x.UserId == req.UserId && x.TenantId == req.TenantId && x.EndDate == null)
            .ToListAsync(ct);
        foreach (var h in openHistories) h.Close(now);

        // T1 factories accept long orgUnitId
        var newAssignment = UserOrgUnitAssignment.Create(req.UserId, targetUnit.Id, req.RoleInOrg, req.TenantId, now);
        db.Assignments.Add(newAssignment);

        var newHistory = StaffPositionHistory.Create(req.UserId, targetUnit.Id, req.PositionTitle, req.TenantId, now);
        db.PositionHistories.Add(newHistory);

        await db.SaveChangesAsync(ct);

        return Result.Ok(new UserOrgUnitAssignmentDto
        {
            Id = newAssignment.Id,
            UserId = newAssignment.UserId,
            OrgUnitId = newAssignment.OrgUnitId,
            RoleInOrg = newAssignment.RoleInOrg,
            ValidFrom = newAssignment.ValidFrom,
            ValidTo = newAssignment.ValidTo,
            IsActive = newAssignment.IsActive,
        });
    }
}
