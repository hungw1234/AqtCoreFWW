using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Queries.GetFileSigningSteps;

/// <summary>
/// Lists signing steps for a file owned by the caller's tenant.
/// File tenant guard runs first (cross-tenant file id → 404). Then delegates to
/// <see cref="ISigningRequestClient.GetStepsForFileAsync"/> which is tenant-scoped on the
/// Identity side. Returns an empty list for files that have never been signed (not an error).
/// </summary>
public sealed class GetFileSigningStepsQueryHandler(
    IFileRepository fileRepository,
    ISigningRequestClient signingClient)
    : IRequestHandler<GetFileSigningStepsQuery, Result<IReadOnlyList<SigningStepDto>>>
{
    public async Task<Result<IReadOnlyList<SigningStepDto>>> Handle(
        GetFileSigningStepsQuery request,
        CancellationToken cancellationToken)
    {
        // File ownership guard — cross-tenant file id must return 404, not 200 with empty list.
        var fileResult = await fileRepository.GetByIdAsync(
            FileId.From(request.FileId), cancellationToken);

        if (fileResult.IsFailed || fileResult.Value.TenantId != request.TenantId)
            return Result.Fail(new NotFoundError($"File {request.FileId} not found."));

        // Delegate to the Identity cross-module read surface (tenant-scoped inside the impl).
        var steps = await signingClient.GetStepsForFileAsync(
            request.TenantId, request.FileId, cancellationToken);

        return Result.Ok(steps);
    }
}
