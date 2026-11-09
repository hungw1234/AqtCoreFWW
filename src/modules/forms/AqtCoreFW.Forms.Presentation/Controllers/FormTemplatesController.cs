using System.ComponentModel.DataAnnotations;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Forms.Application.Commands.AddFormField;
using AqtCoreFW.Forms.Application.Commands.CreateFormTemplate;
using AqtCoreFW.Forms.Application.Commands.DeactivateFormField;
using AqtCoreFW.Forms.Application.Commands.DeactivateFormTemplate;
using AqtCoreFW.Forms.Application.Commands.DuplicateFormTemplate;
using AqtCoreFW.Forms.Application.Commands.PublishFormTemplate;
using AqtCoreFW.Forms.Application.Commands.RepublishFormTemplate;
using AqtCoreFW.Forms.Application.Commands.SetFormTemplatePublicAccess;
using AqtCoreFW.Forms.Application.Commands.UpdateFormTemplate;
using AqtCoreFW.Forms.Application.Commands.UpdateFormField;
using AqtCoreFW.Forms.Application.Commands.BulkReorderFields;
using AqtCoreFW.Forms.Application.Commands.ReorderFormField;
using AqtCoreFW.Forms.Application.Queries.GetFormFieldOptions;
using AqtCoreFW.Forms.Application.Queries.GetFormTemplate;
using AqtCoreFW.Forms.Application.Queries.GetFormTemplates;
using AqtCoreFW.Forms.Application.Commands.AddSignerRole;
using AqtCoreFW.Forms.Application.Queries.EvaluateFormulas;
using AqtCoreFW.Forms.Application.Queries.GetFormTemplateSignerRoles;
using AqtCoreFW.Forms.Application.Queries.GetFormTemplateVersions;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Forms.Presentation.Controllers;

/// <summary>
/// Form templates REST API — CRUD + publish lifecycle + field management.
/// Routes use {publicId:guid} — FormTemplate is T2 (Guid PublicId AK) since Phase-07 retier.
/// GET /api/v1/forms/templates/{publicId} responses are eligible for OutputCache (30min).
/// Tenant ID and user identity resolved from JWT claims, not request body.
/// </summary>
[Route("api/v1/forms/templates")]
[Authorize]
public sealed class FormTemplatesController(ISender mediator) : ApiControllerBase
{
    /// <summary>List non-inactive form templates (paged) for a tenant.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery][StringLength(200)] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new GetFormTemplatesQuery(ResolveTenantId(), search, page, pageSize), cancellationToken));
    }

    /// <summary>Get a single form template with all fields by public Guid ID.</summary>
    [HttpGet("{publicId:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(
        Guid publicId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetFormTemplateQuery(publicId, ResolveTenantId()), cancellationToken));

    /// <summary>
    /// Batch-evaluate every active Formula field of the template against caller-supplied
    /// draft values — read-only realtime compute, no manage permission required (any
    /// authenticated user filling a form may compute). Single formula sandbox path,
    /// same <c>IFormulaEvaluationService</c> submit-time uses.
    /// </summary>
    [HttpPost("{publicId:guid}/evaluate-formulas")]
    [EnableRateLimiting("authenticated")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> EvaluateFormulas(
        Guid publicId,
        [FromBody] EvaluateFormulasRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new EvaluateFormulasQuery(publicId, ResolveTenantId(), request.Values), cancellationToken));

    /// <summary>Create a new form template in Draft status (no fields yet).</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateFormTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new CreateFormTemplateCommand(
                ResolveTenantId(), request.Name, request.NameVi,
                request.Code, request.StorageMode, ResolveUserId(),
                request.RequiresConsent, request.ConsentText,
                request.ApprovalWorkflowDefinitionId),
            cancellationToken));

    /// <summary>Update a form template's name and description.</summary>
    [HttpPut("{publicId:guid}")]
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(
        Guid publicId,
        [FromBody] UpdateFormTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateFormTemplateCommand(
                publicId, ResolveTenantId(), request.Name, request.NameVi, ResolveUserId(),
                request.RequiresConsent, request.ConsentText,
                request.ApprovalWorkflowDefinitionId,
                UpdateWorkflow: request.ApprovalWorkflowDefinitionId.HasValue || request.ClearApprovalWorkflow),
            cancellationToken));

    /// <summary>Deactivate (soft-delete) a form template.</summary>
    [HttpDelete("{publicId:guid}")]
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(
        Guid publicId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new DeactivateFormTemplateCommand(publicId, ResolveTenantId(), ResolveUserId()), cancellationToken));

    /// <summary>Publish a Draft template — makes it Active (Json) or Materializing (Table).</summary>
    [HttpPost("{publicId:guid}/publish")]
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Publish(
        Guid publicId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new PublishFormTemplateCommand(publicId, ResolveTenantId(), ResolveUserId()),
            cancellationToken));

    /// <summary>
    /// Re-publish an Active template — emits a fresh FormTemplateVersion snapshot at v_n+1 capturing
    /// current schema state. Closes the drift introduced by AddField on Active templates (BP MVP P1-FU).
    /// </summary>
    [HttpPost("{publicId:guid}/republish")]
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Republish(
        Guid publicId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new RepublishFormTemplateCommand(publicId, ResolveTenantId(), ResolveUserId()),
            cancellationToken));

    /// <summary>
    /// Toggle a template's anonymous public reachability (IsPublic flag). Enabling opts the
    /// form into the RLS public-read carve-out so anonymous by-Code lookups resolve it.
    /// Server-controlled — the flag is never accepted from anonymous/public submit payloads.
    /// </summary>
    [HttpPatch("{publicId:guid}/public-access")]
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SetPublicAccess(
        Guid publicId,
        [FromBody] SetPublicAccessRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new SetFormTemplatePublicAccessCommand(
                publicId, ResolveTenantId(), request.IsPublic, ResolveUserId()),
            cancellationToken));

    /// <summary>Clone a template into a new Draft with the same fields (new IDs). Submissions not copied.</summary>
    [HttpPost("{publicId:guid}/duplicate")]
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Duplicate(
        Guid publicId,
        [FromBody] DuplicateFormTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new DuplicateFormTemplateCommand(publicId, ResolveTenantId(), request.Name, request.Code, ResolveUserId()),
            cancellationToken));

    /// <summary>Add a field to a Draft or Active template.</summary>
    [HttpPost("{publicId:guid}/fields")]
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> AddField(
        Guid publicId,
        [FromBody] AddFormFieldRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new AddFormFieldCommand(
                publicId, ResolveTenantId(),
                request.FieldKey, request.Type,
                request.LabelVi, request.LabelEn,
                request.DisplayOrder, request.Required,
                request.ValidationRulesJson,
                request.Options?.Select(o => new AddFormFieldOptionDto(
                    o.Value, o.LabelVi, o.LabelEn, o.DisplayOrder)).ToList(),
                ResolveUserId(),
                request.DataSourceJson,
                request.FormulaJson,
                request.ParentFieldId),
            cancellationToken));

    /// <summary>Add a signer role slot to a Draft or Active template.</summary>
    [HttpPost("{publicId:guid}/signer-roles")]
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> AddSignerRole(
        Guid publicId,
        [FromBody] AddSignerRoleRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new AddSignerRoleCommand(
                publicId, ResolveTenantId(),
                request.RoleKey, request.RoleLabelVi,
                request.DisplayOrder, request.IsRequired,
                ResolveUserId(),
                request.PlacementJson),
            cancellationToken));

    /// <summary>List all signer roles on a template, ordered by DisplayOrder.</summary>
    [HttpGet("{publicId:guid}/signer-roles")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ListSignerRoles(
        Guid publicId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetFormTemplateSignerRolesQuery(publicId, ResolveTenantId()), cancellationToken));

    /// <summary>
    /// Get selectable options for a reference or dropdown field (EnumRef/InternalRef/ExternalRef/Dropdown).
    /// Cascade v1: pass a dependent field's cascade-parent values as <c>?p[filterParamName]=value</c>
    /// (e.g. <c>?p[provinceCode]=HN</c>) to filter the option-set. Omitting <c>p</c> returns the full,
    /// unfiltered list (backward-compatible). Class-scope [Authorize] applies — authed callers only.
    /// </summary>
    [HttpGet("{publicId:guid}/fields/{fieldId:long}/options")] // arch:accept-long T1 FormField: child of T2 FormTemplate aggregate, addressed via parent PublicId
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetFieldOptions(
        Guid publicId,
        long fieldId,
        [FromQuery] Dictionary<string, string>? p = null,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetFormFieldOptionsQuery(
                fieldId, ResolveTenantId(),
                p is { Count: > 0 } ? p : null),
            cancellationToken));

    /// <summary>Deactivate (soft-delete) a field. Column preserved in Table mode.</summary>
    [HttpDelete("{publicId:guid}/fields/{fieldId:long}")] // arch:accept-long T1 FormField: child of T2 FormTemplate aggregate, addressed via parent PublicId
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeactivateField(
        Guid publicId,
        long fieldId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new DeactivateFormFieldCommand(publicId, fieldId, ResolveTenantId(), ResolveUserId()),
            cancellationToken));

    /// <summary>Update mutable properties of a field (labels, required, validation, etc.). Draft templates only.</summary>
    [HttpPut("{publicId:guid}/fields/{fieldId:long}")] // arch:accept-long T1 FormField: child of T2 FormTemplate aggregate, addressed via parent PublicId
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> UpdateField(
        Guid publicId,
        long fieldId,
        [FromBody] UpdateFieldRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateFormFieldCommand(
                publicId, fieldId, ResolveTenantId(),
                request.LabelVi, request.LabelEn,
                request.Required, request.ValidationRulesJson,
                request.DataSourceJson, request.FormulaJson,
                request.IsPii, ResolveUserId(),
                request.VisibilityRulesJson, request.RequiredIfJson),
            cancellationToken));

    /// <summary>Update the display order of a single field.</summary>
    [HttpPatch("{publicId:guid}/fields/{fieldId:long}/order")] // arch:accept-long T1 FormField: child of T2 FormTemplate aggregate, addressed via parent PublicId
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ReorderField(
        Guid publicId,
        long fieldId,
        [FromBody] ReorderFormFieldRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ReorderFormFieldCommand(publicId, fieldId, ResolveTenantId(), request.NewOrder),
            cancellationToken));

    /// <summary>
    /// List all published snapshots of a template — newest version first. BP MVP P1.
    /// Contract: 404 if template missing; 200 with empty array if template exists but has no snapshots yet
    /// (e.g. Draft never published).
    /// </summary>
    [HttpGet("{publicId:guid}/versions")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ListVersions(
        Guid publicId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ListFormTemplateVersionsQuery(publicId, ResolveTenantId()), cancellationToken));

    /// <summary>Get a single published snapshot (full SchemaJson). BP MVP P1.</summary>
    [HttpGet("{publicId:guid}/versions/{versionNumber:int}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetVersion(
        Guid publicId,
        int versionNumber,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetFormTemplateVersionQuery(publicId, versionNumber, ResolveTenantId()), cancellationToken));

    /// <summary>Bulk reorder all fields — swaps/cascades display order atomically.</summary>
    [HttpPut("{publicId:guid}/fields/reorder")]
    [Authorize(Policy = PermCodes.Forms.Template.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> BulkReorderFields(
        Guid publicId,
        [FromBody] BulkReorderFieldsRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new BulkReorderFieldsCommand(
                publicId, ResolveTenantId(),
                request.Items.Select(i => new FieldOrderItem(i.FieldId, i.NewOrder)).ToList()),
            cancellationToken));
}

// Request body records — no entity types (GOV_SEC_004 mass-assignment prevention)
// TenantId, CreatedBy, UpdatedBy removed — resolved from JWT via base class
public sealed record CreateFormTemplateRequest(
    string Name,
    string NameVi,
    string Code,
    StorageMode StorageMode,
    bool RequiresConsent = false,
    string? ConsentText = null,
    Guid? ApprovalWorkflowDefinitionId = null);

public sealed record UpdateFormTemplateRequest(
    string Name,
    string NameVi,
    bool? RequiresConsent = null,
    string? ConsentText = null,
    Guid? ApprovalWorkflowDefinitionId = null,
    bool ClearApprovalWorkflow = false);

/// <summary>Body for PATCH .../public-access — the desired anonymous-reachability state.</summary>
public sealed record SetPublicAccessRequest(bool IsPublic);

public sealed record AddSignerRoleRequest(
    string RoleKey,
    string RoleLabelVi,
    int DisplayOrder,
    bool IsRequired,
    string? PlacementJson = null);

public sealed record AddFormFieldRequest(
    string FieldKey,
    FormFieldType Type,
    string LabelVi,
    string LabelEn,
    int DisplayOrder,
    bool Required,
    string? ValidationRulesJson,
    List<AddFormFieldOptionRequest>? Options,
    string? DataSourceJson = null,
    string? FormulaJson = null,
    /// <summary>Set to the parent TableField's Id when adding a child column inside a TableField.</summary>
    long? ParentFieldId = null);

public sealed record AddFormFieldOptionRequest(
    string Value,
    string LabelVi,
    string LabelEn,
    int DisplayOrder);

public sealed record DeactivateFormFieldRequest();

public sealed record ReorderFormFieldRequest(int NewOrder);

public sealed record BulkReorderFieldsRequest(
    List<BulkReorderFieldItemRequest> Items);

/// <summary>
/// FieldId = long (T1 FormField.Id internal bigint PK — no Guid AK).
/// Used as opaque round-trip handle. FE consumes via orval <c>number | string</c> permissive union;
/// no parsing/introspection performed FE-side.
/// Re-tier T1→T2 if FE introspection becomes required.
/// </summary>
public sealed record BulkReorderFieldItemRequest(long FieldId, int NewOrder);

/// <summary>Body for PUT .../fields/{fieldId} — only provided fields are updated (patch semantics).</summary>
public sealed record UpdateFieldRequest(
    string? LabelVi = null,
    string? LabelEn = null,
    bool? Required = null,
    string? ValidationRulesJson = null,
    string? DataSourceJson = null,
    string? FormulaJson = null,
    bool? IsPii = null,
    string? VisibilityRulesJson = null,
    string? RequiredIfJson = null);

public sealed record DuplicateFormTemplateRequest(
    string Name,
    string Code);

public sealed record PublishFormTemplateRequest();

/// <summary>Body for POST .../evaluate-formulas — draft values keyed by FieldKey. No FormulaJson
/// accepted from the client; the server loads expressions from the persisted template.</summary>
public sealed record EvaluateFormulasRequest(Dictionary<string, object?> Values);
