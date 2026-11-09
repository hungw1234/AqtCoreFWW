using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

public sealed class ConsentRepository : IConsentRepository
{
    private readonly IdentityDbContext _db;
    private readonly ICrossModuleUnitOfWork? _uow;

    // uow is optional. When a cross-module write span is active (e.g. SubmitFormCommandHandler
    // recording PDPL consent inside its atomic submit), SaveChangesAsync enlists this context onto
    // the shared connection+transaction so the consent row commits or rolls back WITH the form
    // submission — no orphaned consent on a rolled-back submit (FORMS-CONSENT-PRE-SPAN-ORPHAN).
    // When no UoW is active (ConsentController / GrantConsent path), enlist no-ops and SaveChanges
    // commits standalone exactly as before. Mirrors GenericRepository's enlist-aware pattern.
    public ConsentRepository(IdentityDbContext db, ICrossModuleUnitOfWork? uow = null)
    {
        _db = db;
        _uow = uow;
    }

    public Task<ConsentRecord?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default)
        => _db.ConsentRecords.FirstOrDefaultAsync(c => c.PublicId == publicId, ct);

    public Task<List<ConsentRecord>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.ConsentRecords.Where(c => c.DataSubjectId == userId)
            .OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public Task<ConsentRecord?> GetByUserAndPurposeAsync(Guid userId, string purpose, CancellationToken ct = default)
        => _db.ConsentRecords.FirstOrDefaultAsync(c => c.DataSubjectId == userId && c.Purpose == purpose && !c.IsWithdrawn, ct);

    public Task AddAsync(ConsentRecord record, CancellationToken ct = default)
    {
        _db.ConsentRecords.Add(record);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        // Enlist BEFORE the SaveChanges I/O — SetDbConnection requires a closed connection, and the
        // preceding AddAsync is in-memory only so the context connection is still closed here. Callers
        // that read via this repository inside an active span must not do so before this write, or the
        // read would open the un-enlisted connection first and the enlist would then throw (submit path
        // records consent with no prior read, so this holds).
        if (_uow is { IsActive: true })
            await _uow.EnlistAsync(_db, ct);

        await _db.SaveChangesAsync(ct);
    }
}
