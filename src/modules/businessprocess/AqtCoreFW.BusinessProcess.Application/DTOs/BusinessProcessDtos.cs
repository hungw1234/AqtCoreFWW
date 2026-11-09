namespace AqtCoreFW.BusinessProcess.Application.DTOs;

/// <summary>
/// Shallow projection used by the BusinessProcess admin list page.
/// Id = Guid PublicId (T2 external addressing). WorkflowDefinitionId is the internal bigint FK —
/// resolving it to a workflow PublicId is deferred to 2C runtime via IWorkflowModuleClient.
/// </summary>
public sealed record BusinessProcessListItemDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    long WorkflowDefinitionId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Full BusinessProcessDefinition with state-form + transition-form children.
/// Form binding FormTemplateId/WorkflowStateId/WorkflowTransitionId are bigint internal FKs;
/// FE cross-references via separate Workflow/Forms reads.
/// WorkflowDefinitionPublicId (P7-FU1.2): T2 Guid AK of the workflow definition — FE form-map
/// designer uses this to call GET /api/v1/workflow/definitions/{id:guid} for picker options
/// (state/transition display names). Nullable: tolerates an orphan FK so the BP detail page
/// still renders if the workflow row was hard-deleted between writes.
/// RowVersion: base64-encoded SQL rowversion for ETag / If-Match concurrency on form-map replace.
/// </summary>
public sealed record BusinessProcessDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    long WorkflowDefinitionId,
    Guid? WorkflowDefinitionPublicId,
    long? IntakeFormTemplateId,
    int? IntakeFormTemplateVersion,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<BusinessProcessStateFormDto> StateForms,
    IReadOnlyList<BusinessProcessTransitionFormDto> TransitionForms,
    string RowVersion);

public sealed record BusinessProcessStateFormDto(
    Guid Id,
    long WorkflowStateId,
    long FormTemplateId,
    int FormTemplateVersion,
    bool IsRequired,
    int DisplayOrder)
{
    /// <summary>
    /// BUG2 fix 2026-05-18: T2 Guid AK of the bound form template, resolved server-side via
    /// IFormTemplateQueryClient (mirrors WorkflowDefinitionPublicId / IWorkflowModuleClient).
    /// FE binds the picker directly to this — the FE can never resolve the internal bigint
    /// FormTemplateId itself (picker DTO carries only the Guid). Nullable: tolerates an orphan
    /// FK so the BP detail page still renders. Not Dapper-mapped — set in the handler via `with`.
    /// </summary>
    public Guid? FormTemplatePublicId { get; init; }
}

public sealed record BusinessProcessTransitionFormDto(
    Guid Id,
    long WorkflowTransitionId,
    long FormTemplateId,
    int FormTemplateVersion,
    bool IsRequired,
    int DisplayOrder)
{
    /// <summary>
    /// BUG2 fix 2026-05-18: T2 Guid AK of the bound form template, resolved server-side via
    /// IFormTemplateQueryClient. See <see cref="BusinessProcessStateFormDto.FormTemplatePublicId"/>.
    /// </summary>
    public Guid? FormTemplatePublicId { get; init; }
}

/// <summary>
/// Response envelope for the intake-form lookup endpoint (BP MVP P4 Slice 2B).
/// FormTemplatePublicId is the T2 Guid AK from forms.FormTemplates (stable across versions);
/// VersionNumber pins the snapshot. SchemaJson is the raw JSON string (caller / FE parses).
/// When the BP has no intake form pinned, the endpoint returns 204 No Content instead of this DTO.
/// </summary>
public sealed record BusinessProcessIntakeFormDto(
    Guid FormTemplatePublicId,
    int VersionNumber,
    string SchemaJson);
