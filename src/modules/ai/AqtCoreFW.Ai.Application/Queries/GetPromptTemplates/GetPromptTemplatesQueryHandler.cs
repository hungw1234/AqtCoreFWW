using AqtCoreFW.Ai.Application.Commands.CreatePromptTemplate;
using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Queries.GetPromptTemplates;

/// <summary>Repository read-path — returns active templates, optionally filtered by category.</summary>
public sealed class GetPromptTemplatesQueryHandler(IAiPromptTemplateRepository repository)
    : IRequestHandler<GetPromptTemplatesQuery, Result<IReadOnlyList<AiPromptTemplateDto>>>
{
    public async Task<Result<IReadOnlyList<AiPromptTemplateDto>>> Handle(
        GetPromptTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        var templates = await repository.GetActiveAsync(request.Category, cancellationToken);

        IReadOnlyList<AiPromptTemplateDto> result = templates
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name)
            .Select(CreatePromptTemplateCommandHandler.MapToDto)
            .ToList();

        return Result.Ok(result);
    }
}
