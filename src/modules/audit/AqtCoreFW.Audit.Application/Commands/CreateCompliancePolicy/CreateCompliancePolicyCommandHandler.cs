using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.CreateCompliancePolicy;

/// <summary>
/// Creates a compliance policy.
/// Returns long Id — T1 entity (AuditableEntity, long PK, no PublicId AK).
/// </summary>
public sealed class CreateCompliancePolicyCommandHandler(ICompliancePolicyRepository repository)
    : IRequestHandler<CreateCompliancePolicyCommand, Result<long>>
{
    public async Task<Result<long>> Handle(
        CreateCompliancePolicyCommand request,
        CancellationToken cancellationToken)
    {
        var policy = CompliancePolicy.Create(
            request.Name,
            request.Category,
            request.Rules,
            request.Enforcement);

        await repository.AddAsync(policy, cancellationToken);
        return Result.Ok(policy.Id);
    }
}
