using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.DeleteFile;

public sealed class DeleteFileCommandHandler(
    IFileRepository fileRepository) : IRequestHandler<DeleteFileCommand, Result>
{
    public async Task<Result> Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var fileResult = await fileRepository.GetByIdAsync(
            FileId.From(request.FileId), cancellationToken);

        if (fileResult.IsFailed)
            return Result.Fail(new NotFoundError($"File {request.FileId} not found."));

        var file = fileResult.Value;

        if (file.TenantId != request.TenantId)
            return Result.Fail(new ForbiddenError("File does not belong to tenant."));

        file.Delete();
        await fileRepository.UpdateAsync(file, cancellationToken);

        return Result.Ok();
    }
}
