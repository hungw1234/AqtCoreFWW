using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.CreateRetentionPolicy;

// Phase-4b B3b: RetentionPolicy is T1 — long PK, so IRepository<RetentionPolicy, long>.
public sealed class CreateRetentionPolicyCommandHandler(
    IRepository<RetentionPolicy, long> repository)
    : IRequestHandler<CreateRetentionPolicyCommand, Result<RetentionPolicyDto>>
{
    public async Task<Result<RetentionPolicyDto>> Handle(
        CreateRetentionPolicyCommand request,
        CancellationToken cancellationToken)
    {
        var policy = RetentionPolicy.Create(
            request.Name,
            request.DocumentType,
            request.RetainDays,
            request.ArchiveAfterDays,
            request.DestroyAfterDays,
            request.TenantId,
            request.CreatedBy);

        await repository.AddAsync(policy, cancellationToken);

        return Result.Ok(MapToDto(policy));
    }

    // Phase-4b B3b: T1 entity — Id is long (bigint identity PK).
    internal static RetentionPolicyDto MapToDto(RetentionPolicy p) => new(
        p.Id, p.Name, p.DocumentType, p.RetainDays,
        p.ArchiveAfterDays, p.DestroyAfterDays, p.IsActive, p.TenantId);
}
