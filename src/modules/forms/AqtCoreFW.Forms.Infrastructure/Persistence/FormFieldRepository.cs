using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Forms.Infrastructure.Persistence;

public sealed class FormFieldRepository(FormsDbContext dbContext)
    : GenericRepository<FormField, long>(dbContext), IFormFieldRepository
{
    private FormsDbContext Db => (FormsDbContext)Context;

    public override async Task<Result<FormField>> GetByIdAsync(
        long id, CancellationToken cancellationToken = default)
    {
        var entity = await Db.FormFields
            .Include(f => f.Options)
            .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, cancellationToken);

        return entity is null
            ? Result.Fail(new NotFoundError($"FormField {id} not found."))
            : Result.Ok(entity);
    }

    public async Task<IReadOnlyList<FormField>> GetByTemplateIdAsync(
        long templateId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = Db.FormFields
            .Include(f => f.Options)
            .Where(f => f.FormTemplateId == templateId && !f.IsDeleted);

        if (activeOnly)
            query = query.Where(f => f.IsActive);

        return await query
            .OrderBy(f => f.DisplayOrder)
            .ToListAsync(cancellationToken);
    }
}
