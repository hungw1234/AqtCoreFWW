using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.UpdateFormTemplate;

/// <summary>
/// Update form template name and description.
/// PublicId = FormTemplate.PublicId (Guid AK — external addressing).
/// </summary>
public sealed record UpdateFormTemplateCommand(
    Guid PublicId,
    Guid TenantId,
    string Name,
    string NameVi,
    Guid UpdatedBy,
    bool? RequiresConsent = null,
    string? ConsentText = null,
    Guid? ApprovalWorkflowDefinitionId = null,
    bool UpdateWorkflow = false) : IRequest<Result>;
