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

public sealed class CreateWorkflowActionTaxonomyCommandHandler(
    MasterDataDbContext db,
    IDistributedCache cache,
    IGuidGenerator guidGen)
    : IRequestHandler<CreateWorkflowActionTaxonomyCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateWorkflowActionTaxonomyCommand cmd, CancellationToken ct)
    {
        var conflict = await db.WorkflowActionTaxonomies
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Code == cmd.Code, ct);
        if (conflict)
            return Result.Fail(new ConflictError($"Workflow action code '{cmd.Code}' already exists")
                .WithMetadata("ErrorCode", "GOV_DUPLICATE_CODE"));

        var entity = WorkflowActionTaxonomy.Create(
            guidGen,
            code: cmd.Code, nameVi: cmd.NameVi, nameEn: cmd.NameEn,
            sortOrder: cmd.SortOrder, i18nKey: cmd.I18nKey, icon: cmd.Icon);

        db.WorkflowActionTaxonomies.Add(entity);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(GetWorkflowActionTaxonomiesQueryHandler.CacheKey, ct);

        return Result.Ok(entity.PublicId);
    }
}

// ── Update ────────────────────────────────────────────────────────────

public sealed class UpdateWorkflowActionTaxonomyCommandHandler(
    MasterDataDbContext db,
    IDistributedCache cache)
    : IRequestHandler<UpdateWorkflowActionTaxonomyCommand, Result>
{
    public async Task<Result> Handle(
        UpdateWorkflowActionTaxonomyCommand cmd, CancellationToken ct)
    {
        var entity = await db.WorkflowActionTaxonomies
            .FirstOrDefaultAsync(x => x.PublicId == cmd.Id, ct);
        if (entity is null)
            return Result.Fail(new NotFoundError($"WorkflowActionTaxonomy {cmd.Id} not found"));

        db.Entry(entity).Property(e => e.RowVersion).OriginalValue = cmd.RowVersion;

        entity.Update(
            nameVi: cmd.NameVi, nameEn: cmd.NameEn,
            i18nKey: cmd.I18nKey, icon: cmd.Icon, sortOrder: cmd.SortOrder);

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

        await cache.RemoveAsync(GetWorkflowActionTaxonomiesQueryHandler.CacheKey, ct);
        return Result.Ok();
    }
}

// ── Delete (soft) ─────────────────────────────────────────────────────

public sealed class DeleteWorkflowActionTaxonomyCommandHandler(
    MasterDataDbContext db,
    IDistributedCache cache)
    : IRequestHandler<DeleteWorkflowActionTaxonomyCommand, Result>
{
    public async Task<Result> Handle(
        DeleteWorkflowActionTaxonomyCommand cmd, CancellationToken ct)
    {
        var entity = await db.WorkflowActionTaxonomies
            .FirstOrDefaultAsync(x => x.PublicId == cmd.Id, ct);
        if (entity is null)
            return Result.Fail(new NotFoundError($"WorkflowActionTaxonomy {cmd.Id} not found"));

        if (!string.Equals(cmd.ConfirmCode, entity.Code, StringComparison.Ordinal))
            return Result.Fail(new BadRequestError(
                $"ConfirmCode '{cmd.ConfirmCode}' does not match target code",
                "GOV_CONFIRM_CODE_MISMATCH"));

        entity.Deprecate(cmd.Reason);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(GetWorkflowActionTaxonomiesQueryHandler.CacheKey, ct);
        return Result.Ok();
    }
}

// ── Restore ───────────────────────────────────────────────────────────

public sealed class RestoreWorkflowActionTaxonomyCommandHandler(
    MasterDataDbContext db,
    IDistributedCache cache)
    : IRequestHandler<RestoreWorkflowActionTaxonomyCommand, Result>
{
    public async Task<Result> Handle(
        RestoreWorkflowActionTaxonomyCommand cmd, CancellationToken ct)
    {
        var entity = await db.WorkflowActionTaxonomies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.PublicId == cmd.Id, ct);
        if (entity is null)
            return Result.Fail(new NotFoundError($"WorkflowActionTaxonomy {cmd.Id} not found"));

        // M1 guard
        if (entity.IsActive)
            return Result.Fail(new ConflictError(
                $"WorkflowActionTaxonomy '{entity.Code}' is already active")
                .WithMetadata("ErrorCode", "GOV_ALREADY_ACTIVE"));

        var collision = await db.WorkflowActionTaxonomies
            .AnyAsync(x => x.Code == entity.Code && x.PublicId != entity.PublicId && x.IsActive, ct);
        if (collision)
            return Result.Fail(new ConflictError(
                $"Another active workflow action with code '{entity.Code}' exists")
                .WithMetadata("ErrorCode", "GOV_CODE_COLLISION"));

        entity.Restore();
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(GetWorkflowActionTaxonomiesQueryHandler.CacheKey, ct);
        return Result.Ok();
    }
}
