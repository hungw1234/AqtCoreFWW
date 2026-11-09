using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Ai.Application.Commands.CreatePromptTemplate;

/// <summary>Creates a new versioned AI prompt template.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreatePromptTemplateCommand(
    string Name,
    PromptCategory Category,
    string SystemPrompt,
    string UserPromptTemplate) : ICommand<AiPromptTemplateDto>;
