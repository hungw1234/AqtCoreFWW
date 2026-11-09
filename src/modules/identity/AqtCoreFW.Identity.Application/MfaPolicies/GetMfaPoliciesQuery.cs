using AqtCoreFW.Identity.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.MfaPolicies;

/// <summary>Returns all MFA policies ordered by role name.</summary>
public sealed record GetMfaPoliciesQuery : IRequest<Result<IReadOnlyList<MfaPolicyDto>>>;

public sealed class GetMfaPoliciesQueryHandler(IMfaPolicyRepository repository)
    : IRequestHandler<GetMfaPoliciesQuery, Result<IReadOnlyList<MfaPolicyDto>>>
{
    public async Task<Result<IReadOnlyList<MfaPolicyDto>>> Handle(
        GetMfaPoliciesQuery request, CancellationToken ct)
    {
        var policies = await repository.ListAllAsync(ct);

        var dtos = policies
            .OrderBy(p => p.Role)
            .Select(p => new MfaPolicyDto(
                p.PublicId,   // T2: external Guid — never expose internal long Id
                p.Role,
                p.IsMandatory,
                p.GraceDays,
                p.AllowedFactors,
                p.CreatedAt,
                p.UpdatedAt))
            .ToList();

        return Result.Ok<IReadOnlyList<MfaPolicyDto>>(dtos);
    }
}
