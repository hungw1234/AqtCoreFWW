using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Forms.Infrastructure.Persistence;

public sealed class ViewDefinitionRepository(FormsDbContext dbContext)
    : GenericRepository<ViewDefinition, long>(dbContext), IViewDefinitionRepository
{
    private FormsDbContext Db => (FormsDbContext)Context;

    public override async Task<Result<ViewDefinition>> GetByIdAsync(
        long id, CancellationToken cancellationToken = default)
    {
        var entity = await Db.ViewDefinitions
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted, cancellationToken);

        return entity is null
            ? Result.Fail(new NotFoundError($"ViewDefinition {id} không tìm thấy."))
            : Result.Ok(entity);
    }

    public async Task<ViewDefinition?> GetByIdWithDetailsAsync(
        long id, Guid tenantId, CancellationToken cancellationToken = default) =>
        await Db.ViewDefinitions
            .Include(v => v.Columns)
            .Include(v => v.Filters)
            .FirstOrDefaultAsync(
                v => v.Id == id && v.TenantId == tenantId && !v.IsDeleted,
                cancellationToken);

    public async Task<IReadOnlyList<ViewDefinition>> GetByEntityTypeAsync(
        string entityType, Guid tenantId, CancellationToken cancellationToken = default) =>
        await Db.ViewDefinitions
            .Where(v => v.EntityType == entityType && v.TenantId == tenantId && !v.IsDeleted)
            .OrderByDescending(v => v.IsDefault)
            .ThenBy(v => v.CreatedAt)
            .ToListAsync(cancellationToken);
}
