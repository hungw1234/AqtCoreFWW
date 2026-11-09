using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ISignerEnrollmentNonceRepository"/>. Tenant isolation is enforced
/// by RLS (DB-level, like the other Identity signing tables) plus the explicit TenantId predicate here.
/// <see cref="GetAsync"/> returns a tracked entity so the caller's <c>Consume</c> flushes via SaveChanges.
/// </summary>
public sealed class SignerEnrollmentNonceRepository(IdentityDbContext db) : ISignerEnrollmentNonceRepository
{
    public async Task AddAsync(SignerEnrollmentNonce nonce, CancellationToken ct = default)
    {
        await db.SignerEnrollmentNonces.AddAsync(nonce, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task<SignerEnrollmentNonce?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.SignerEnrollmentNonces
            .FirstOrDefaultAsync(n => n.TenantId == tenantId && n.Id == id, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
