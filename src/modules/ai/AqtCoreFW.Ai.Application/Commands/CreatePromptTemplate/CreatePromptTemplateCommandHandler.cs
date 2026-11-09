using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Commands.CreatePromptTemplate;

/// <summary>Creates and persists a new AiPromptTemplate at version 1. DTO.Id = entity.PublicId (T2 retier).</summary>
public sealed class CreatePromptTemplateCommandHandler(
    IAiPromptTemplateRepository repository,
    IGuidGenerator guidGenerator)
    : IRequestHandler<CreatePromptTemplateCommand, Result<AiPromptTemplateDto>>
{
    public async Task<Result<AiPromptTemplateDto>> Handle(
        CreatePromptTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var template = AiPromptTemplate.Create(
            guidGenerator,
            request.Name,
            request.Category,
            request.SystemPrompt,
            request.UserPromptTemplate);

        await repository.AddAsync(template, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Ok(MapToDto(template));
    }

    internal static AiPromptTemplateDto MapToDto(AiPromptTemplate t) => new(
        t.PublicId, t.Name, t.Category.ToString(),
        t.SystemPrompt, t.UserPromptTemplate,
        t.Version, t.IsActive, t.CreatedAt, t.UpdatedAt);
}
