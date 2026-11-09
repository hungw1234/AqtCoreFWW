using AqtCoreFW.MasterData.Application.Commands.WorkflowTaxonomy;
using AqtCoreFW.MasterData.Application.Queries.WorkflowTaxonomy;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

// ── Create ────────────────────────────────────────────────────────────

public sealed class CreateWorkflowStateTaxonomyCommandHandler(
    MasterDataDbContext db,
    IDistributedCache cache,
    IGuidGenerator guidGen)
    : IRequestHandler<CreateWorkflowStateTaxonomyCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateWorkflowStateTaxonomyCommand cmd, CancellationToken ct)
    {
        // IgnoreQueryFilters: a hard-deleted row still occupies the unique Code index
        var conflict = await db.WorkflowStateTaxonomies
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Code == cmd.Code, ct);
        if (conflict)
            return Result.Fail(new ConflictError($"Workflow state code '{cmd.Code}' already exists")
                .WithMetadata("ErrorCode", "GOV_DUPLICATE_CODE"));

        var entity = WorkflowStateTaxonomy.Create(
            guidGen,
            code: cmd.Code, nameVi: cmd.NameVi, nameEn: cmd.NameEn,
            category: cmd.Category, sortOrder: cmd.SortOrder,
            i18nKey: cmd.I18nKey, icon: cmd.Icon, color: cmd.Color);

        db.WorkflowStateTaxonomies.Add(entity);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(GetWorkflowStateTaxonomiesQueryHandler.CacheKey, ct);

        return Result.Ok(entity.PublicId);
    }
}

// ── Update ────────────────────────────────────────────────────────────

public sealed class UpdateWorkflowStateTaxonomyCommandHandler(
    MasterDataDbContext db,
    IDistributedCache cache)
    : IRequestHandler<UpdateWorkflowStateTaxonomyCommand, Result>
{
    public async Task<Result> Handle(
        UpdateWorkflowStateTaxonomyCommand cmd, CancellationToken ct)
    {
        var entity = await db.WorkflowStateTaxonomies
            .FirstOrDefaultAsync(x => x.PublicId == cmd.Id, ct);
        if (entity is null)
            return Result.Fail(new NotFoundError($"WorkflowStateTaxonomy {cmd.Id} not found"));

        // Optimistic concurrency: EF checks RowVersion against original when SaveChanges runs
        db.Entry(entity).Property(e => e.RowVersion).OriginalValue = cmd.RowVersion;

        entity.Update(
            nameVi: cmd.NameVi, nameEn: cmd.NameEn,
            i18nKey: cmd.I18nKey, icon: cmd.Icon, color: cmd.Color,
            category: cmd.Category, sortOrder: cmd.SortOrder);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(new ConflictError(
                "Another admin updated this row — reload and retry")
                .WithMetadata("ErrorCode", "GOV_CONCURRENCY_CONFLICT"));
        }

        await cache.RemoveAsync(GetWorkflowStateTaxonomiesQueryHandler.CacheKey, ct);
        return Result.Ok();
    }
}

// ── Delete (soft) ─────────────────────────────────────────────────────

public sealed class DeleteWorkflowStateTaxonomyCommandHandler(
    MasterDataDbContext db,
    IDistributedCache cache)
    : IRequestHandler<DeleteWorkflowStateTaxonomyCommand, Result>
{
    public async Task<Result> Handle(
        DeleteWorkflowStateTaxonomyCommand cmd, CancellationToken ct)
    {
        var entity = await db.WorkflowStateTaxonomies
            .FirstOrDefaultAsync(x => x.PublicId == cmd.Id, ct);
        if (entity is null)
            return Result.Fail(new NotFoundError($"WorkflowStateTaxonomy {cmd.Id} not found"));

        // Case-sensitive compare per plan §Validation Delete
        if (!string.Equals(cmd.ConfirmCode, entity.Code, StringComparison.Ordinal))
            return Result.Fail(new BadRequestError(
                $"ConfirmCode '{cmd.ConfirmCode}' does not match target code",
                "GOV_CONFIRM_CODE_MISMATCH"));

        entity.Deprecate(cmd.Reason);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(GetWorkflowStateTaxonomiesQueryHandler.CacheKey, ct);
        return Result.Ok();
    }
}

// ── Restore ───────────────────────────────────────────────────────────

public sealed class RestoreWorkflowStateTaxonomyCommandHandler(
    MasterDataDbContext db,
    IDistributedCache cache)
    : IRequestHandler<RestoreWorkflowStateTaxonomyCommand, Result>
{
    public async Task<Result> Handle(
        RestoreWorkflowStateTaxonomyCommand cmd, CancellationToken ct)
    {
        // Soft-deleted row visible via IgnoreQueryFilters
        var entity = await db.WorkflowStateTaxonomies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.PublicId == cmd.Id, ct);
        if (entity is null)
            return Result.Fail(new NotFoundError($"WorkflowStateTaxonomy {cmd.Id} not found"));

        // M1 guard: Restore on already-active row is a no-op / caller confusion
        if (entity.IsActive)
            return Result.Fail(new ConflictError(
                $"WorkflowStateTaxonomy '{entity.Code}' is already active")
                .WithMetadata("ErrorCode", "GOV_ALREADY_ACTIVE"));

        // Safety check: no OTHER active row with same Code (plan R6)
        var collision = await db.WorkflowStateTaxonomies
            .AnyAsync(x => x.Code == entity.Code && x.PublicId != entity.PublicId && x.IsActive, ct);
        if (collision)
            return Result.Fail(new ConflictError(
                $"Another active workflow state with code '{entity.Code}' exists")
                .WithMetadata("ErrorCode", "GOV_CODE_COLLISION"));

        entity.Restore();
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(GetWorkflowStateTaxonomiesQueryHandler.CacheKey, ct);
        return Result.Ok();
    }
}
