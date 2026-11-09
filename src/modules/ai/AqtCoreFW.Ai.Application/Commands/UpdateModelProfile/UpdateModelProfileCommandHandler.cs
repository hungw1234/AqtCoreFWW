using AqtCoreFW.Ai.Application.Commands.CreateModelProfile;
using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Commands.UpdateModelProfile;

/// <summary>
/// Updates an existing AiModelProfile.
/// Enforces single-default invariant when IsDefault=true is requested.
/// </summary>
public sealed class UpdateModelProfileCommandHandler(IAiModelProfileRepository repository)
    : IRequestHandler<UpdateModelProfileCommand, Result<AiModelProfileDto>>
{
    public async Task<Result<AiModelProfileDto>> Handle(
        UpdateModelProfileCommand request,
        CancellationToken cancellationToken)
    {
        var profile = await repository.GetByPublicIdAsync(request.PublicId, cancellationToken);

        if (profile is null)
            return Result.Fail<AiModelProfileDto>(
                new NotFoundError($"AiModelProfile {request.PublicId} not found."));

        // Update inference config
        if (request.MaxTokens.HasValue || request.Temperature.HasValue)
        {
            profile.UpdateConfig(
                request.MaxTokens ?? profile.MaxTokens,
                request.Temperature ?? profile.Temperature);
        }

        // Default flag — clear others first, then promote this one
        if (request.IsDefault == true && !profile.IsDefault)
        {
            var active = await repository.GetActiveAsync(cancellationToken);
            foreach (var other in active.Where(p => p.IsDefault && p.Id != profile.Id))
                other.ClearDefault();

            profile.SetAsDefault();
        }

        // Active flag
        if (request.IsActive == false && profile.IsActive)
            profile.Deactivate();

        await repository.SaveChangesAsync(cancellationToken);

        return Result.Ok(CreateModelProfileCommandHandler.MapToDto(profile));
    }
}
