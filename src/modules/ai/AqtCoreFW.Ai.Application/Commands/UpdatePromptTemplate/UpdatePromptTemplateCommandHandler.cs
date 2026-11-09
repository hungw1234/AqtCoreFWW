using AqtCoreFW.Ai.Application.Commands.CreatePromptTemplate;
using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Commands.UpdatePromptTemplate;

/// <summary>
/// Updates AiPromptTemplate content (auto-increments version) and/or active flag.
/// Both prompts must be supplied together — partial prompt update not allowed.
/// </summary>
public sealed class UpdatePromptTemplateCommandHandler(IAiPromptTemplateRepository repository)
    : IRequestHandler<UpdatePromptTemplateCommand, Result<AiPromptTemplateDto>>
{
    public async Task<Result<AiPromptTemplateDto>> Handle(
        UpdatePromptTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var template = await repository.GetByPublicIdAsync(request.PublicId, cancellationToken);

        if (template is null)
            return Result.Fail<AiPromptTemplateDto>(
                new NotFoundError($"AiPromptTemplate {request.PublicId} not found."));

        // Update prompts — version auto-increments inside UpdatePrompts()
        if (request.SystemPrompt is not null || request.UserPromptTemplate is not null)
        {
            template.UpdatePrompts(
                request.SystemPrompt ?? template.SystemPrompt,
                request.UserPromptTemplate ?? template.UserPromptTemplate);
        }

        if (request.IsActive == false && template.IsActive)
            template.Deactivate();

        await repository.SaveChangesAsync(cancellationToken);

        return Result.Ok(CreatePromptTemplateCommandHandler.MapToDto(template));
    }
}
