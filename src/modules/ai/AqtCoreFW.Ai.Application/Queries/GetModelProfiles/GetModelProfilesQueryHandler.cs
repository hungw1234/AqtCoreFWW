using AqtCoreFW.Ai.Application.Commands.CreateModelProfile;
using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Queries.GetModelProfiles;

/// <summary>Repository read-path — returns all active profiles ordered by IsDefault desc, Name asc.</summary>
public sealed class GetModelProfilesQueryHandler(IAiModelProfileRepository repository)
    : IRequestHandler<GetModelProfilesQuery, Result<IReadOnlyList<AiModelProfileDto>>>
{
    public async Task<Result<IReadOnlyList<AiModelProfileDto>>> Handle(
        GetModelProfilesQuery request,
        CancellationToken cancellationToken)
    {
        var profiles = await repository.GetActiveAsync(cancellationToken);

        IReadOnlyList<AiModelProfileDto> result = profiles
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name)
            .Select(CreateModelProfileCommandHandler.MapToDto)
            .ToList();

        return Result.Ok(result);
    }
}
