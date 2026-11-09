using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.CreateAuditRetentionPolicy;

/// <summary>
/// Creates a retention policy row.
/// Returns long Id — T1 entity (AuditableEntity, long PK, no PublicId AK).
/// </summary>
public sealed class CreateAuditRetentionPolicyCommandHandler(IAuditRetentionPolicyRepository repository)
    : IRequestHandler<CreateAuditRetentionPolicyCommand, Result<long>>
{
    public async Task<Result<long>> Handle(
        CreateAuditRetentionPolicyCommand request,
        CancellationToken cancellationToken)
    {
        AuditRetentionPolicy policy;
        try
        {
            policy = AuditRetentionPolicy.Create(
                request.TenantId,
                request.Category,
                request.RetentionDays,
                request.ArchiveBeforePurge,
                request.ArchivePath);
        }
        catch (ArgumentException ex)
        {
            return Result.Fail(new BadRequestError(ex.Message, "AUDIT_RETENTION_INVALID_INPUT"));
        }

        await repository.AddAsync(policy, cancellationToken);
        return Result.Ok(policy.Id);
    }
}
