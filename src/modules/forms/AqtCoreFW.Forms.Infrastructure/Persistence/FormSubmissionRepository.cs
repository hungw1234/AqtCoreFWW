using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Forms.Infrastructure.Persistence;

public sealed class FormSubmissionRepository(
    FormsDbContext dbContext,
    ICrossModuleUnitOfWork? uow = null)
    : GenericRepository<FormSubmission, Guid>(dbContext, uow), IFormSubmissionRepository
{
    private FormsDbContext Db => (FormsDbContext)Context;

    // FormSubmission is T2: IRepository<FormSubmission, Guid> uses PublicId (external AK) as key.
    // Override to query by PublicId instead of the long internal PK.
    public override async Task<Result<FormSubmission>> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var entity = await Db.FormSubmissions
            .FirstOrDefaultAsync(s => s.PublicId == id && !s.IsDeleted, cancellationToken);

        return entity is null
            ? Result.Fail(new NotFoundError($"FormSubmission {id} not found."))
            : Result.Ok(entity);
    }

    // Same T2 reason as GetByIdAsync above: the base FindAsync keys on the long PK, so a Guid
    // (PublicId) delete would throw the Guid-vs-long key mismatch. Resolve by PublicId first.
    // No caller hard-deletes a FormSubmission today (submissions are soft-deleted), but the override
    // keeps key resolution consistent with GetByIdAsync so a future delete path does not hit the throw.
    public override async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var entity = await Db.FormSubmissions
            .FirstOrDefaultAsync(s => s.PublicId == id, cancellationToken);
        if (entity is null) return;

        // SoftDeleteInterceptor converts EntityState.Deleted → IsDeleted = true (mirrors base DeleteAsync).
        Db.FormSubmissions.Remove(entity);
        await Db.SaveChangesAsync(cancellationToken);
    }
}
