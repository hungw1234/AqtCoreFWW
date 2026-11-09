using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Commands.CreateModelProfile;

/// <summary>
/// Creates an AiModelProfile and persists it.
/// If IsDefault is requested, existing default is cleared first (one default invariant).
/// T2 retier: IGuidGenerator injected to set PublicId via factory.
/// </summary>
public sealed class CreateModelProfileCommandHandler(
    IAiModelProfileRepository repository,
    IGuidGenerator guidGenerator)
    : IRequestHandler<CreateModelProfileCommand, Result<AiModelProfileDto>>
{
    public async Task<Result<AiModelProfileDto>> Handle(
        CreateModelProfileCommand request,
        CancellationToken cancellationToken)
    {
        // Enforce single-default invariant
        if (request.IsDefault)
        {
            var existing = await repository.GetActiveAsync(cancellationToken);
            foreach (var p in existing.Where(p => p.IsDefault))
                p.ClearDefault();
        }

        var profile = AiModelProfile.Create(
            guidGenerator,
            request.Name,
            request.Provider,
            request.ModelId,
            request.Endpoint,
            request.MaxTokens,
            request.Temperature,
            request.CostPerToken,
            request.IsDefault);

        await repository.AddAsync(profile, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Ok(MapToDto(profile));
    }

    /// <summary>
    /// Static mapper — shared by UpdateModelProfileCommandHandler and GetModelProfilesQueryHandler.
    /// DTO.Id sourced from entity.PublicId (T2 AK external addressing).
    /// </summary>
    internal static AiModelProfileDto MapToDto(AiModelProfile p) => new(
        p.PublicId, p.Name, p.Provider.ToString(), p.ModelId,
        p.Endpoint, p.MaxTokens, p.Temperature, p.CostPerToken,
        p.IsDefault, p.IsActive, p.CreatedAt, p.UpdatedAt);
}
