using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.BusinessProcess.Application.Commands.CreateBusinessProcess;

/// <summary>
/// Create a new BusinessProcessDefinition (starts inactive — admin flips via Activate command).
/// Intake form pinning (template + version) is all-or-nothing per GOV_BP_010 — both or neither.
/// BP MVP P7: command takes Guid PublicIds (FE never sees bigint internal PKs).
/// Handler resolves WorkflowDefinitionPublicId → bigint via IWorkflowModuleClient,
/// and IntakeFormTemplatePublicId → bigint via IFormTemplateQueryClient.
/// Returns the new BP's Guid PublicId in the response body; the controller wraps in 200 OK + ApiResponse{Guid}
/// (project convention is 200-with-body for Create, not 201+Location — see ApiControllerBase.ToApiResponse).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateBusinessProcessCommand(
    Guid TenantId,
    string Code,
    string Name,
    string? Description,
    Guid WorkflowDefinitionPublicId,
    Guid? IntakeFormTemplatePublicId,
    int? IntakeFormTemplateVersion,
    Guid CreatedBy)
    : ICommand<Guid>;
