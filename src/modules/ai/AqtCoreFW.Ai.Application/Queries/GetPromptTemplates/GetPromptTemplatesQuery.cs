using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Ai.Application.Queries.GetPromptTemplates;

/// <summary>Returns active prompt templates, optionally filtered by category.</summary>
public sealed record GetPromptTemplatesQuery(
    PromptCategory? Category = null) : IQuery<IReadOnlyList<AiPromptTemplateDto>>;
