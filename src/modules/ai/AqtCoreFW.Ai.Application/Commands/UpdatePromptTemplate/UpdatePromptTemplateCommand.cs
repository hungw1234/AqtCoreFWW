using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Ai.Application.Commands.UpdatePromptTemplate;

/// <summary>Updates prompt content and/or active flag. Version auto-increments on content change. PublicId = Guid AK (T2 retier).</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdatePromptTemplateCommand(
    Guid PublicId,
    string? SystemPrompt,
    string? UserPromptTemplate,
    bool? IsActive) : ICommand<AiPromptTemplateDto>;
