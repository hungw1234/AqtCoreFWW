using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Files.Infrastructure.Clients;

/// <summary>
/// Monolith in-process implementation of IFilesModuleClient.
/// Zero overhead — direct repository call within same process.
/// Swap for GrpcFilesModuleClient when Files module is extracted to microservice (1 DI line change).
/// </summary>
public sealed class InProcessFilesModuleClient(IFileRepository fileRepository) : IFilesModuleClient
{
    public async Task<bool> IsFileAvailableAsync(
        Guid fileId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = await fileRepository.GetByIdAsync(FileId.From(fileId), cancellationToken);
        if (result.IsFailed) return false;

        var file = result.Value;
        return file.TenantId == tenantId && file.Status == FileStatus.Available;
    }

    public async Task<FileReferenceInfo?> GetFileReferenceAsync(
        Guid fileId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = await fileRepository.GetByIdAsync(FileId.From(fileId), cancellationToken);
        if (result.IsFailed) return null;

        var file = result.Value;
        if (file.TenantId != tenantId) return null;

        // Phase-4b B3b: Id is long (bigint), PublicId is the Guid AK exposed externally.
        return new FileReferenceInfo(
            file.PublicId,
            file.OriginalFileName,
            file.ContentType,
            file.SizeBytes,
            file.Status.ToString());
    }
}
