using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Files.Infrastructure.Persistence;

/// <summary>
/// Phase-4b B3b: FileRecord is T2 (long Id PK + Guid PublicId AK).
/// IFileRepository : IRepository&lt;FileRecord, FileId&gt; where FileId wraps Guid (PublicId).
/// Override GetByIdAsync to look up by PublicId instead of EF FindAsync (which uses long PK).
/// </summary>
public sealed class FileRepository(FilesDbContext context)
    : GenericRepository<FileRecord, FileId>(context), IFileRepository
{
    private FilesDbContext FilesContext => (FilesDbContext)Context;

    /// <summary>Lookup by PublicId (Guid AK) — FileId.Value is the PublicId Guid.</summary>
    public override async Task<Result<FileRecord>> GetByIdAsync(
        FileId id,
        CancellationToken cancellationToken = default)
    {
        var entity = await FilesContext.FileRecords
            .FirstOrDefaultAsync(f => f.PublicId == id.Value, cancellationToken);

        return entity is null
            ? Result.Fail(new NotFoundError($"FileRecord with PublicId '{id.Value}' not found."))
            : Result.Ok(entity);
    }

    public async Task<FileRecord?> FindByStorageKeyAsync(
        string storageKey,
        CancellationToken cancellationToken = default) =>
        await FilesContext.FileRecords
            .FirstOrDefaultAsync(f => f.StorageKey == storageKey, cancellationToken);

    public async Task<IReadOnlyList<FileRecord>> GetByStatusAsync(
        FileStatus status,
        CancellationToken cancellationToken = default) =>
        await FilesContext.FileRecords
            .Where(f => f.Status == status)
            .ToListAsync(cancellationToken);
}
