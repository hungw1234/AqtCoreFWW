using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.SubmitForm;

/// <summary>
/// Submits data against an Active form template.
/// Data values are object? to support typed input from API consumers.
/// Routes to JsonFormDataStorage or TableFormDataStorage based on template StorageMode.
/// FormTemplatePublicId = FormTemplate.PublicId (T2 Guid AK — external addressing).
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record SubmitFormCommand(
    Guid TenantId,
    Guid FormTemplatePublicId,
    Guid SubmittedBy,
    Dictionary<string, object?> Data,
    bool ConsentGiven = false,
    // BP-Run linkage (P2). When WorkflowInstanceId is set, handler routes to CreateForBpRun factory:
    //   - R14 cross-tenant guard fires (load instance, assert TenantId match, else GOV_BP_002)
    //   - skip the template.ApprovalWorkflowDefinitionId workflow-instance-create block (instance pre-exists)
    // WorkflowStateId null = intake form; non-null = state form.
    Guid? WorkflowInstanceId = null,
    long? WorkflowStateId = null,
    SignaturePayload? Signature = null) : ICommand<Guid>;
