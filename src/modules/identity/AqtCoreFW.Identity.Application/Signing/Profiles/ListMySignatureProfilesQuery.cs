using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Self-service: lists the caller's own signature profiles (ordered by display name). Scoped to
/// (TenantId, CallerUserId) injected from the JWT — a caller never sees another user's profiles.
/// </summary>
public sealed record ListMySignatureProfilesQuery(
    Guid TenantId,
    Guid CallerUserId) : IQuery<IReadOnlyList<SignatureProfileDto>>;

public sealed class ListMySignatureProfilesQueryHandler(IUserSignatureProfileRepository repository)
    : IRequestHandler<ListMySignatureProfilesQuery, Result<IReadOnlyList<SignatureProfileDto>>>
{
    public async Task<Result<IReadOnlyList<SignatureProfileDto>>> Handle(
        ListMySignatureProfilesQuery query, CancellationToken ct)
    {
        var profiles = await repository.ListByUserAsync(query.TenantId, query.CallerUserId, ct);

        IReadOnlyList<SignatureProfileDto> dtos = profiles.Select(SignatureProfileDto.From).ToList();

        return Result.Ok(dtos);
    }
}
