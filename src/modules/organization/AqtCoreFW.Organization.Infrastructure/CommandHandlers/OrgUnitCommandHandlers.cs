using AqtCoreFW.Organization.Application.Commands;
using AqtCoreFW.Organization.Application.DTOs;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Organization.Infrastructure.CommandHandlers;

public sealed class CreateOrgUnitCommandHandler(
    OrgDbContext db,
    OrgUnitService orgService,
    IGuidGenerator gen)
    : IRequestHandler<CreateOrgUnitCommand, Result<OrgUnitDto>>
{
    public async Task<Result<OrgUnitDto>> Handle(CreateOrgUnitCommand req, CancellationToken ct)
    {
        var exists = await db.OrgUnits
            .AnyAsync(x => x.TenantId == req.TenantId && x.Code == req.Code, ct);
        if (exists)
            return Result.Fail(new ConflictError($"OrgUnit code '{req.Code}' already exists in this tenant."));

        // OrgUnit is T2: ParentId on wire = PublicId (Guid); entity.ParentId = long.
        int level = 1;
        long? parentLongId = null;
        if (req.ParentId.HasValue)
        {
            var parent = await db.OrgUnits
                .FirstOrDefaultAsync(x => x.PublicId == req.ParentId.Value && x.TenantId == req.TenantId, ct);
            if (parent is null)
                return Result.Fail(new NotFoundError($"Parent OrgUnit '{req.ParentId}' not found."));
            level = parent.Level + 1;
            parentLongId = parent.Id;
        }

        var unit = OrgUnit.Create(gen, req.Name, req.NameEn, req.Code, req.TenantId, parentLongId, level);
        db.OrgUnits.Add(unit);
        await db.SaveChangesAsync(ct);
        await orgService.InvalidateCacheAsync(req.TenantId, ct);

        // ParentId in DTO = req.ParentId (already Guid/PublicId from wire)
        return Result.Ok(new OrgUnitDto(
            unit.PublicId, req.ParentId, unit.Name, unit.NameEn,
            unit.Code, unit.Level, unit.IsActive, unit.TenantId,
            SuccessorId: null));
    }
}

public sealed class UpdateOrgUnitCommandHandler(OrgDbContext db, OrgUnitService orgService)
    : IRequestHandler<UpdateOrgUnitCommand, Result<OrgUnitDto>>
{
    public async Task<Result<OrgUnitDto>> Handle(UpdateOrgUnitCommand req, CancellationToken ct)
    {
        // req.Id = PublicId (Guid) — T2 external addressing
        var unit = await db.TrackingQuery<OrgUnit>()
            .FirstOrDefaultAsync(x => x.PublicId == req.Id && x.TenantId == req.TenantId, ct);
        if (unit is null)
            return Result.Fail(new NotFoundError($"OrgUnit '{req.Id}' not found."));

        unit.Update(req.Name, req.NameEn);
        await db.SaveChangesAsync(ct);
        await orgService.InvalidateCacheAsync(req.TenantId, ct);

        return Result.Ok(await orgService.GetByIdAsync(unit.PublicId, req.TenantId, ct)
            ?? new OrgUnitDto(unit.PublicId, null, unit.Name, unit.NameEn,
                unit.Code, unit.Level, unit.IsActive, unit.TenantId, null));
    }
}

public sealed class DeleteOrgUnitCommandHandler(OrgDbContext db, OrgUnitService orgService)
    : IRequestHandler<DeleteOrgUnitCommand, Result>
{
    public async Task<Result> Handle(DeleteOrgUnitCommand req, CancellationToken ct)
    {
        // req.Id = PublicId (Guid) — T2 external addressing
        var unit = await db.TrackingQuery<OrgUnit>()
            .FirstOrDefaultAsync(x => x.PublicId == req.Id && x.TenantId == req.TenantId, ct);
        if (unit is null)
            return Result.Fail(new NotFoundError($"OrgUnit '{req.Id}' not found."));

        // HasActiveChildrenAsync takes internal long Id
        if (await orgService.HasActiveChildrenAsync(unit.Id, ct))
            return Result.Fail(new ValidationError(
                "Cannot deactivate org unit with active children. Deactivate children first.", "Id"));

        if (req.SuccessorId.HasValue)
        {
            // req.SuccessorId = PublicId (Guid) — resolve to internal long Id
            var successor = await db.OrgUnits
                .FirstOrDefaultAsync(x => x.PublicId == req.SuccessorId.Value && x.TenantId == req.TenantId, ct);
            if (successor is not null)
                unit.SetSuccessor(successor.Id);
        }

        unit.Deactivate();
        await db.SaveChangesAsync(ct);
        await orgService.InvalidateCacheAsync(req.TenantId, ct);

        return Result.Ok();
    }
}
