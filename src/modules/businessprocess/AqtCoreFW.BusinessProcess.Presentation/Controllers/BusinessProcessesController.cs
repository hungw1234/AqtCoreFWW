using AqtCoreFW.BusinessProcess.Application.Commands.ActivateBusinessProcess;
using AqtCoreFW.BusinessProcess.Application.Commands.CreateBusinessProcess;
using AqtCoreFW.BusinessProcess.Application.Commands.DeactivateBusinessProcess;
using AqtCoreFW.BusinessProcess.Application.Commands.ExecuteBusinessProcessTransition;
using AqtCoreFW.BusinessProcess.Application.Commands.ReplaceFormMaps;
using AqtCoreFW.BusinessProcess.Application.Commands.StartBusinessProcessRun;
using AqtCoreFW.BusinessProcess.Application.Commands.SubmitBusinessProcessStateForm;
using AqtCoreFW.BusinessProcess.Application.Commands.UpdateBusinessProcess;
using AqtCoreFW.BusinessProcess.Application.Queries.GetBpRunSubmissions;
using AqtCoreFW.BusinessProcess.Application.Queries.GetBusinessProcessById;
using AqtCoreFW.BusinessProcess.Application.Queries.GetIntakeFormSchema;
using AqtCoreFW.BusinessProcess.Application.Queries.GetStateFormSchema;
using AqtCoreFW.BusinessProcess.Application.Queries.ListBusinessProcesses;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.SharedKernel.Presentation;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.BusinessProcess.Presentation.Controllers;

/// <summary>
/// BusinessProcess admin + read endpoints.
/// - Slice 2A-Read (shipped 2026-05-16): List, ListActive, GetById.
/// - Slice 2A-Write (this ship): Create, Update (metadata-only), Activate, Deactivate.
/// - Slice 2C: runtime endpoints (Start Run, Transition, Submit State Form).
/// TenantId resolved from JWT via ResolveTenantId(); cross-tenant denied unless SystemAdmin.
/// </summary>
[Route("api/v1/business-processes")]
[Authorize]
public sealed class BusinessProcessesController(ISender mediator) : ApiControllerBase
{
    /// <summary>List BusinessProcessDefinitions for the caller's tenant (paged, filterable).</summary>
    [HttpGet]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new ListBusinessProcessesQuery(ResolveTenantId(), search, isActive, pageNumber, pageSize),
            cancellationToken));
    }

    /// <summary>Active BPs (capped at 100) — feeds the user BP-Run picker. Convenience over /?isActive=true.</summary>
    [HttpGet("active")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListActive(CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ListBusinessProcessesQuery(ResolveTenantId(), Search: null, IsActive: true, Page: 1, PageSize: 100),
            cancellationToken));

    /// <summary>Full BusinessProcessDefinition with state-form + transition-form children.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetBusinessProcessByIdQuery(id, ResolveTenantId()), cancellationToken));

    /// <summary>Create a new BusinessProcessDefinition. Starts inactive; operator runs Activate afterwards.</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.Manage)]
    [ProducesResponseType(200)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateBusinessProcessRequest body,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new CreateBusinessProcessCommand(
                ResolveTenantId(), body.Code, body.Name, body.Description,
                body.WorkflowDefinitionPublicId, body.IntakeFormTemplatePublicId, body.IntakeFormTemplateVersion,
                ResolveUserId()),
            cancellationToken));

    /// <summary>Update mutable metadata (Name, Description). Form-map mutations defer to a later slice.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateBusinessProcessRequest body,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateBusinessProcessCommand(ResolveTenantId(), id, body.Name, body.Description, ResolveUserId()),
            cancellationToken));

    /// <summary>Flip IsActive false→true. Idempotent; emits BusinessProcessActivatedEvent on real transition.</summary>
    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.Activate)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Activate(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ActivateBusinessProcessCommand(ResolveTenantId(), id, ResolveUserId()),
            cancellationToken));

    /// <summary>Flip IsActive true→false. Idempotent; emits BusinessProcessDeactivatedEvent on real transition.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.Activate)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new DeactivateBusinessProcessCommand(ResolveTenantId(), id, ResolveUserId()),
            cancellationToken));

    /// <summary>
    /// Intake-form snapshot pinned by the BP definition. Returns 204 No Content when no intake form
    /// is configured (BPs without intake forms are valid per GOV_BP_010 — caller falls back to direct
    /// programmatic start). 404 when the BP itself does not exist or the pinned snapshot is missing.
    /// </summary>
    [HttpGet("{id:guid}/intake-form")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetIntakeForm(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetIntakeFormSchemaQuery(id, ResolveTenantId()), cancellationToken);

        // ToApiResponse maps a successful null payload to 200 + null body — explicit 204 is the
        // contracted shape for "no intake form pinned" so FE can branch without parsing a body.
        if (result.IsSuccess && result.Value is null) return NoContent();
        return ToApiResponse(result);
    }

    /// <summary>
    /// P8-FU1: state-form field schema for a single state-form map. Returns 200 with the FormTemplate
    /// snapshot ({FormTemplatePublicId, VersionNumber, SchemaJson}) when the map + snapshot exist.
    /// 404 when the BP, the map, or the pinned snapshot is missing (or cross-tenant). FE uses this to
    /// render the state-form panel + pair with prefill-context for editable fields.
    /// </summary>
    [HttpGet("{bpId:guid}/state-form-maps/{stateFormMapId:guid}/schema")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetStateFormSchema(
        Guid bpId,
        Guid stateFormMapId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetStateFormSchemaQuery(bpId, stateFormMapId, ResolveTenantId()), cancellationToken));

    /// <summary>
    /// Start a BP Run — creates a <c>WorkflowInstance</c> bound to (entityType, entityId) and, when this BP
    /// pins an intake form, the intake <c>FormSubmission</c>, atomically. 422 when the BP is inactive or
    /// the intake form is required but data is missing; 404 when the BP or its WorkflowDefinition is missing.
    /// </summary>
    [HttpPost("{id:guid}/runs")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> StartRun(
        Guid id,
        [FromBody] StartBusinessProcessRunRequest body,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new StartBusinessProcessRunCommand(
                id, ResolveTenantId(), body.EntityType, body.EntityId,
                body.IntakeFormData, body.ConsentGiven, ResolveUserId(),
                MapSignature(body.Signature)),
            cancellationToken));

    /// <summary>
    /// Execute a workflow transition on a BP-Run. Discriminated response: returns <c>{requiresForms:true}</c>
    /// when a form-gated transition has no bundled form, otherwise <c>{success:true, newState…, stateForms…}</c>.
    /// Atomic when a transition form is bundled (form submit + transition exec under one TransactionScope).
    /// </summary>
    [HttpPost("runs/{workflowInstanceId:guid}/transitions")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [Authorize(Policy = PermCodes.Workflow.Task.Process)]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ExecuteTransition(
        Guid workflowInstanceId,
        [FromBody] ExecuteBusinessProcessTransitionRequest body,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ExecuteBusinessProcessTransitionCommand(
                workflowInstanceId, body.BpPublicId, body.ActionName,
                body.TransitionFormTemplateId, body.TransitionFormData, body.ConsentGiven,
                body.Comment, ResolveTenantId(), ResolveUserId(),
                Signature: MapSignature(body.Signature)),
            cancellationToken));

    /// <summary>
    /// Submit a state-scoped form against a BP-Run's current WorkflowState. Single op — no transition exec.
    /// 422 when the FormTemplate is not configured for this state on this BP; 404 on cross-tenant lookup.
    /// </summary>
    [HttpPost("runs/{workflowInstanceId:guid}/state-forms/{stateId:long}")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> SubmitStateForm(
        Guid workflowInstanceId,
        long stateId,
        [FromBody] SubmitBusinessProcessStateFormRequest body,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new SubmitBusinessProcessStateFormCommand(
                workflowInstanceId, stateId, body.BpPublicId, body.FormTemplateId,
                body.Data, body.ConsentGiven, ResolveTenantId(), ResolveUserId(),
                MapSignature(body.Signature)),
            cancellationToken));

    /// <summary>
    /// BP MVP P7-FU2 — lists all non-deleted BPs that reference the given FormTemplate (as intake,
    /// state-form, or transition-form). FE uses this pre-flight before deactivating/deleting the template.
    /// Empty list = safe to proceed. Cross-tenant callers always receive empty list (D-cross-3).
    /// Route accepts FormTemplate PublicId (Guid) — resolves to internal id via IFormTemplateQueryClient.
    /// </summary>
    [HttpGet("references/form-template/{formTemplatePublicId:guid}")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetFormTemplateReferences(
        Guid formTemplatePublicId,
        [FromServices] IBusinessProcessQueryClient bpQueryClient,
        [FromServices] IFormTemplateQueryClient formTemplateQueryClient,
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveTenantId();
        var internalId = await formTemplateQueryClient.TryResolveTemplateInternalIdAsync(
            formTemplatePublicId, tenantId, cancellationToken);

        // If the FormTemplate doesn't exist or is cross-tenant, return empty list (no existence leak, D-cross-3)
        if (internalId is null)
            return ToApiResponse(Result.Ok(Array.Empty<BusinessProcessReferenceDto>() as IReadOnlyList<BusinessProcessReferenceDto>));

        return ToApiResponse(Result.Ok(
            await bpQueryClient.ListBpsReferencingFormTemplateAsync(internalId.Value, tenantId, cancellationToken)));
    }

    /// <summary>
    /// BP MVP P7-FU3 — lists all non-deleted BPs that reference the given WorkflowDefinition.
    /// FE uses this pre-flight before deactivating/deleting the workflow definition.
    /// Empty list = safe to proceed. Cross-tenant callers always receive empty list (D-cross-3).
    /// Route accepts WorkflowDefinition PublicId (Guid) — resolves to internal id via IWorkflowModuleClient.
    /// </summary>
    [HttpGet("references/workflow-definition/{workflowDefinitionPublicId:guid}")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetWorkflowDefinitionReferences(
        Guid workflowDefinitionPublicId,
        [FromServices] IBusinessProcessQueryClient bpQueryClient,
        [FromServices] IWorkflowModuleClient workflowModuleClient,
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveTenantId();
        var internalId = await workflowModuleClient.TryResolveDefinitionInternalIdAsync(
            workflowDefinitionPublicId, tenantId, cancellationToken);

        // If the WorkflowDefinition doesn't exist or is cross-tenant, return empty list (D-cross-3)
        if (internalId is null)
            return ToApiResponse(Result.Ok(Array.Empty<BusinessProcessReferenceDto>() as IReadOnlyList<BusinessProcessReferenceDto>));

        return ToApiResponse(Result.Ok(
            await bpQueryClient.ListBpsReferencingWorkflowDefinitionAsync(internalId.Value, tenantId, cancellationToken)));
    }

    /// <summary>
    /// BP MVP P7-FU1 — atomically replace all state-form and transition-form bindings for a BP.
    /// Accepts full lists (not patches): existing bindings are cleared and replaced in one transaction (D-B-1).
    /// displayOrder is derived server-side from array index (D-B-2).
    /// If-Match header with base64 RowVersion is required for optimistic concurrency (D-B-3); returns 412 on stale.
    /// </summary>
    [HttpPut("{id:guid}/form-maps")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(412)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ReplaceFormMaps(
        Guid id,
        [FromBody] ReplaceFormMapsRequest body,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ReplaceFormMapsCommand(
                id,
                ResolveTenantId(),
                ResolveUserId(),
                Request.Headers.IfMatch.ToString(),
                body.StateForms,
                body.TransitionForms),
            cancellationToken));

    /// <summary>
    /// BP MVP P6 — resolve the prefill context for a target form rendered inside a BP-Run. Walks each
    /// target field's <c>PrefillSource</c> snapshot, looks up the source value from a prior submission
    /// in the same run, and returns <c>{fieldKey: value}</c>. 200 + dict on success (empty dict valid
    /// when nothing resolves); 404 when the WorkflowInstance is unknown or cross-tenant (D8).
    /// </summary>
    [HttpGet("runs/{workflowInstanceId:guid}/forms/{formTemplatePublicId:guid}/prefill-context")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetPrefillContext(
        Guid workflowInstanceId,
        Guid formTemplatePublicId,
        [FromQuery] int version,
        [FromServices] IFormPrefillContextClient client,
        CancellationToken cancellationToken = default)
    {
        // Snapshot versions are 1-based monotonic (FormTemplate.Version starts at 1) — reject 0/negative
        // pre-resolver to avoid a misleading 200+empty (resolver would 404 on the snapshot lookup but
        // map that to "no prefill resolved" success per D8 split).
        if (version < 1)
            return ToApiResponse(Result.Fail<IReadOnlyDictionary<string, object?>>(
                new ValidationError("version phải >= 1.")));

        var dict = await client.GetPrefillContextAsync(
            workflowInstanceId, formTemplatePublicId, version, ResolveTenantId(), cancellationToken);

        if (dict is null)
            return ToApiResponse(Result.Fail<IReadOnlyDictionary<string, object?>>(
                new NotFoundError($"BusinessProcess Run {workflowInstanceId} không tìm thấy.")));

        return ToApiResponse(Result.Ok(dict));
    }

    /// <summary>
    /// Read-only: latest submitted form data for a BP-Run (intake + each state form), assembled with
    /// snapshot labels so the run detail page can show what was already entered. Gated on BP.View —
    /// the operational page's own permission — so it never reaches the Forms-admin submission endpoints.
    /// Returns an empty list when the run has no submissions (or is unknown/cross-tenant — not leaked).
    /// </summary>
    [HttpGet("runs/{workflowInstanceId:guid}/submissions")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetRunSubmissions(
        Guid workflowInstanceId,
        CancellationToken cancellationToken = default)
        => ToApiResponse(await mediator.Send(
            new GetBpRunSubmissionsQuery(workflowInstanceId, ResolveTenantId()), cancellationToken));

    /// <summary>
    /// Decode the wire-format (base64) signature bundle into the transport-neutral carrier passed to
    /// the application layer. Returns null when no signature was supplied (no DigitalSignature field).
    /// Malformed base64 surfaces as a 4xx (ArgumentException → BadRequest) rather than a 500.
    /// </summary>
    private static BpRunSignaturePayload? MapSignature(BpRunSignaturePayloadRequest? wire) =>
        wire is null
            ? null
            : new BpRunSignaturePayload(
                DecodeBase64(wire.SignatureBase64, nameof(wire.SignatureBase64)),
                wire.SignerCertBase64 is null ? null : DecodeBase64(wire.SignerCertBase64, nameof(wire.SignerCertBase64)),
                wire.Format,
                wire.HashAlg,
                wire.Encode,
                wire.ProviderKey);

    private static byte[] DecodeBase64(string value, string field)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException($"Trường chữ ký '{field}' không phải chuỗi base64 hợp lệ.", field, ex);
        }
    }
}

/// <summary>POST body for Start BP Run. <c>IntakeFormData</c> is required when the BP pins an intake form.</summary>
public sealed record StartBusinessProcessRunRequest(
    string EntityType,
    Guid EntityId,
    Dictionary<string, object?>? IntakeFormData,
    bool ConsentGiven,
    BpRunSignaturePayloadRequest? Signature = null);

/// <summary>POST body for Execute Transition. <c>TransitionFormTemplateId</c> + <c>TransitionFormData</c> are paired.</summary>
public sealed record ExecuteBusinessProcessTransitionRequest(
    Guid BpPublicId,
    string ActionName,
    long? TransitionFormTemplateId,
    Dictionary<string, object?>? TransitionFormData,
    bool ConsentGiven,
    string? Comment,
    BpRunSignaturePayloadRequest? Signature = null);

/// <summary>POST body for Submit State Form.</summary>
public sealed record SubmitBusinessProcessStateFormRequest(
    Guid BpPublicId,
    long FormTemplateId,
    Dictionary<string, object?> Data,
    bool ConsentGiven,
    BpRunSignaturePayloadRequest? Signature = null);

/// <summary>
/// Wire-format signature proof bundle for BP-Run forms — signature and cert bytes base64-encoded
/// for JSON transport, decoded to <see cref="BpRunSignaturePayload"/> in the controller. Mirrors
/// the standalone-form wire shape; TenantId / signed-content are resolved/recomputed server-side.
/// </summary>
public sealed record BpRunSignaturePayloadRequest(
    string SignatureBase64,
    string? SignerCertBase64 = null,
    string Format = "Raw",
    string HashAlg = "SHA256",
    string Encode = "RSA",
    string? ProviderKey = null);

/// <summary>
/// POST body for Create. WorkflowDefinitionPublicId and IntakeFormTemplatePublicId are Guid AKs (FE-visible).
/// Handler resolves them to bigint internal FKs via IWorkflowModuleClient + IFormTemplateQueryClient.
/// </summary>
public sealed record CreateBusinessProcessRequest(
    string Code,
    string Name,
    string? Description,
    Guid WorkflowDefinitionPublicId,
    Guid? IntakeFormTemplatePublicId,
    int? IntakeFormTemplateVersion);

/// <summary>PUT body for Update — metadata only. Form-map mutations live in a later slice.</summary>
public sealed record UpdateBusinessProcessRequest(string? Name, string? Description);

/// <summary>
/// PUT body for ReplaceFormMaps. Full lists only — partial updates are not supported.
/// StateForms / TransitionForms use long WorkflowStateId / WorkflowTransitionId (T1 entities, no Guid AK).
/// FormTemplatePublicId is Guid (T2 AK) resolved to bigint in the handler.
/// </summary>
public sealed record ReplaceFormMapsRequest(
    IReadOnlyList<StateFormBindingDto> StateForms,
    IReadOnlyList<TransitionFormBindingDto> TransitionForms);
