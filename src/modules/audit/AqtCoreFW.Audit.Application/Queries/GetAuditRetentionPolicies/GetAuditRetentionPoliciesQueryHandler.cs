using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.GetAuditRetentionPolicies;

public sealed class GetAuditRetentionPoliciesQueryHandler(IAuditRetentionPolicyRepository repository)
    : IRequestHandler<GetAuditRetentionPoliciesQuery, Result<IReadOnlyList<AuditRetentionPolicyDto>>>
{
    public async Task<Result<IReadOnlyList<AuditRetentionPolicyDto>>> Handle(
        GetAuditRetentionPoliciesQuery request,
        CancellationToken cancellationToken)
    {
        var rows = await repository.ListAsync(request.TenantIdFilter, cancellationToken);
        IReadOnlyList<AuditRetentionPolicyDto> dtos = rows
            .Select(p => new AuditRetentionPolicyDto(
                p.Id,
                p.TenantId,
                p.Category,
                p.RetentionDays,
                p.ArchiveBeforePurge,
                p.ArchivePath,
                Convert.ToBase64String(p.RowVersion),
                p.CreatedAt,
                p.UpdatedAt))
            .ToList();
        return Result.Ok(dtos);
    }
}
