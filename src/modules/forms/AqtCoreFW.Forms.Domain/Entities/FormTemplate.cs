using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;
using System.Text.RegularExpressions;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// FormTemplate aggregate root — admin-defined form schema.
/// StorageMode (Json|Table) is immutable after first submission.
/// Code is a unique slug per tenant used for SQL view naming.
/// Status lifecycle: Draft → [Materializing →] Active → Inactive.
/// T2 entity: AuditableEntity (long Id internal) + IPublicIdentified (Guid PublicId AK external).
/// Retiered T1→T2 per Phase-07 — IAggregateRoot + RowVersion + lifecycle = T2 profile.
/// </summary>
public sealed class FormTemplate : AuditableEntity, IAggregateRoot, IPublicIdentified, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<FormField> _fields = [];
    private readonly List<FormTemplateSignerRole> _signerRoles = [];

    /// <summary>Public alternate key for external addressing (API URLs, DTOs). Set by factory via IGuidGenerator.NewIdMonotonic().</summary>
    public Guid PublicId { get; private set; }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NameVi { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public StorageMode StorageMode { get; private set; }
    public FormTemplateStatus Status { get; private set; }

    /// <summary>M-02: Optimistic concurrency token — prevents silent overwrites on concurrent edits.</summary>
    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>PDPL Art. 11: whether this template requires explicit consent before submission.</summary>
    public bool RequiresConsent { get; private set; }

    /// <summary>Consent text shown to user (Vietnamese). Null = use system default.</summary>
    public string? ConsentText { get; private set; }

    /// <summary>
    /// Whether this template is reachable through the anonymous public endpoints (by-Code
    /// resolution). Bound into the FormTemplates RLS FILTER carve-out: an anonymous (no-tenant)
    /// caller only sees a row when IsPublic=1 AND the server sets the public_forms_bypass flag.
    /// Defaults false — new templates are private until explicitly opted in.
    /// </summary>
    public bool IsPublic { get; private set; }

    /// <summary>Optional workflow definition public id for automated approval. Null = manual approve/reject.</summary>
    public Guid? ApprovalWorkflowDefinitionId { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public IReadOnlyCollection<FormField> Fields => _fields.AsReadOnly();
    public IReadOnlyCollection<FormTemplateSignerRole> SignerRoles => _signerRoles.AsReadOnly();

    private FormTemplate() { } // EF Core

    /// <summary>
    /// Factory method — creates a Draft template with no fields yet.
    /// <paramref name="gen"/> must be non-null; injects PublicId (T2 Guid AK) via IGuidGenerator.NewIdMonotonic().
    /// </summary>
    public static FormTemplate Create(
        IGuidGenerator gen,
        Guid tenantId,
        string name,
        string nameVi,
        string code,
        StorageMode storageMode,
        Guid createdBy)
    {
        ArgumentNullException.ThrowIfNull(gen);
        ValidateCode(code);

        var template = new FormTemplate
        {
            PublicId = gen.NewIdMonotonic(),
            TenantId = tenantId,
            Name = name,
            NameVi = nameVi,
            Code = code,
            StorageMode = storageMode,
            Status = FormTemplateStatus.Draft,
            Version = 1
        };
        template.SetAuditCreate(createdBy);
        return template;
    }

    /// <summary>Change the template code (slug). Only allowed in Draft status.</summary>
    public void SetCode(string code, Guid updatedBy)
    {
        if (Status != FormTemplateStatus.Draft)
            throw new InvalidFormSchemaException("GOV_FRM_010: Chỉ có thể đổi Code khi biểu mẫu ở trạng thái Draft.");

        ValidateCode(code);
        Code = code;
        SetAuditUpdate(updatedBy);
    }

    /// <summary>
    /// Add a field to the template.
    /// Allowed in Draft or Active status. Active adds increment Version.
    /// Set parentFieldId to add a child field inside a TableField.
    /// </summary>
    public FormField AddField(
        string fieldKey,
        FormFieldType type,
        string labelVi,
        string labelEn,
        int displayOrder,
        bool required,
        string? validationRulesJson,
        Guid updatedBy,
        string? dataSourceJson = null,
        string? formulaJson = null,
        long? parentFieldId = null,
        string? visibilityRulesJson = null,
        bool isPii = false,
        string? prefillSourceJson = null)
    {
        if (Status != FormTemplateStatus.Draft && Status != FormTemplateStatus.Active)
            throw new InvalidFormSchemaException(
                "GOV_FRM_010: Chỉ có thể thêm field khi biểu mẫu ở trạng thái Draft hoặc Active.");

        if (_fields.Any(f => f.FieldKey == fieldKey && f.IsActive))
            throw new InvalidFormSchemaException($"GOV_FRM_012: FieldKey '{fieldKey}' đã tồn tại.");

        // Validate parent field reference if provided
        if (parentFieldId.HasValue)
        {
            var parent = _fields.FirstOrDefault(f => f.Id == parentFieldId.Value && f.IsActive);
            if (parent is null)
                throw new InvalidFormSchemaException(
                    $"GOV_FRM_016: ParentFieldId '{parentFieldId}' không tồn tại hoặc đã bị vô hiệu hóa.");
            if (parent.Type != FormFieldType.TableField)
                throw new InvalidFormSchemaException(
                    "GOV_FRM_016: ParentFieldId phải trỏ đến một field kiểu TableField.");
        }

        var field = FormField.Create(
            Id, fieldKey, type, labelVi, labelEn, displayOrder,
            required, Version, validationRulesJson,
            parentFieldId: parentFieldId,
            dataSourceJson: dataSourceJson, formulaJson: formulaJson,
            isPii: isPii,
            visibilityRulesJson: visibilityRulesJson,
            prefillSourceJson: prefillSourceJson);

        _fields.Add(field);

        if (Status == FormTemplateStatus.Active)
            Version++;

        SetAuditUpdate(updatedBy);
        return field;
    }

    /// <summary>
    /// Add a signer role slot to the template.
    /// Allowed in Draft or Active status. Adding on an Active template bumps Version (same rule
    /// as AddField — signer roles are part of the generated-document definition).
    /// RoleKey must be non-empty and unique within the template.
    /// </summary>
    public FormTemplateSignerRole AddSignerRole(
        string roleKey,
        string roleLabelVi,
        int displayOrder,
        bool isRequired,
        string? placementJson,
        Guid updatedBy)
    {
        if (Status != FormTemplateStatus.Draft && Status != FormTemplateStatus.Active)
            throw new InvalidFormSchemaException(
                "GOV_FRM_010: Chỉ có thể thêm signer role khi biểu mẫu ở trạng thái Draft hoặc Active.");

        if (string.IsNullOrWhiteSpace(roleKey))
            throw new InvalidFormSchemaException("GOV_FRM_030: RoleKey không được để trống.");

        if (_signerRoles.Any(r => string.Equals(r.RoleKey, roleKey.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidFormSchemaException($"GOV_FRM_031: RoleKey '{roleKey}' đã tồn tại trong biểu mẫu.");

        var role = FormTemplateSignerRole.Create(Id, roleKey, roleLabelVi, displayOrder, isRequired, placementJson);
        _signerRoles.Add(role);

        if (Status == FormTemplateStatus.Active)
            Version++;

        SetAuditUpdate(updatedBy);
        return role;
    }

    /// <summary>Soft-delete a field. Column preserved in Table mode.</summary>
    public void DeactivateField(long fieldId, Guid updatedBy)
    {
        var field = _fields.FirstOrDefault(f => f.Id == fieldId)
            ?? throw new InvalidFormSchemaException("GOV_FRM_015: Field không tồn tại.");

        // Cascade v1: reject the delete if a surviving field still depends on this one (spike §4 —
        // reject, do not silent-strip). No-op when no field carries a cascade dependency on it.
        CascadeDependencyValidator.EnsureNotDependedOn(
            _fields.Where(f => f.IsActive).ToList(), field.FieldKey);

        field.Deactivate();
        SetAuditUpdate(updatedBy);
    }

    /// <summary>
    /// Publish template.
    /// Json mode: Draft → Active immediately.
    /// Table mode: Draft → Materializing (background DDL will set Active).
    /// </summary>
    public void Publish(Guid updatedBy)
    {
        if (Status != FormTemplateStatus.Draft)
            throw new InvalidFormSchemaException(
                "GOV_FRM_010: Chỉ publish được biểu mẫu ở trạng thái Draft.");

        if (!_fields.Any(f => f.IsActive))
            throw new InvalidFormSchemaException(
                "GOV_FRM_003: Biểu mẫu phải có ít nhất một field trước khi publish.");

        // Cascade v1: reject a bad cross-field dependency graph (cycle / self / dangling) at publish
        // so it never reaches the browser (spike §4). No-op for templates with no dependencies[].
        var activeFields = _fields.Where(f => f.IsActive).ToList();
        CascadeDependencyValidator.Validate(activeFields);

        // H2: a public form must not carry a Vault-authed external DS (anonymous surface would issue
        // an authed outbound fetch on behalf of an unauthenticated caller). No-op for private forms.
        CascadeDependencyValidator.EnsureNoAuthedExternalOnPublic(activeFields, IsPublic);

        Status = StorageMode == StorageMode.Table
            ? FormTemplateStatus.Materializing
            : FormTemplateStatus.Active;

        SetAuditUpdate(updatedBy);
    }

    /// <summary>
    /// Re-publish an already-Active template: bumps Version and signals the caller to emit
    /// a fresh FormTemplateVersion snapshot capturing current schema state.
    /// Closes the drift introduced by <see cref="AddField"/> on Active templates which bumps
    /// Version silently without snapshotting (BP MVP P1-FU).
    /// Semantic note: <c>VersionNumber</c> is a snapshot label (monotonic counter), not a
    /// distinct-schema-state count — two consecutive Republish calls with zero edits still
    /// produce distinct VersionNumbers.
    /// </summary>
    public void Republish(Guid updatedBy)
    {
        if (Status != FormTemplateStatus.Active)
            throw new InvalidFormSchemaException(
                "GOV_FRM_010: Chỉ republish được biểu mẫu ở trạng thái Active.");

        if (!_fields.Any(f => f.IsActive))
            throw new InvalidFormSchemaException(
                "GOV_FRM_003: Biểu mẫu phải có ít nhất một field trước khi republish.");

        // Cascade v1: same graph guard as Publish — an Active template gains fields via AddField
        // then Republish, so the cascade graph must be re-validated on every version bump.
        var activeFields = _fields.Where(f => f.IsActive).ToList();
        CascadeDependencyValidator.Validate(activeFields);

        // H2: re-enforce the public/authed-external guard on republish — an authed-external field can
        // be added to an Active public template via AddField then Republish.
        CascadeDependencyValidator.EnsureNoAuthedExternalOnPublic(activeFields, IsPublic);

        Version++;
        SetAuditUpdate(updatedBy);
    }

    /// <summary>Called by FormMaterializationService after DDL succeeds.</summary>
    public void MarkActive(Guid updatedBy)
    {
        if (Status != FormTemplateStatus.Materializing)
            throw new InvalidFormSchemaException(
                "GOV_FRM_013: Chỉ chuyển Active từ trạng thái Materializing.");

        Status = FormTemplateStatus.Active;
        SetAuditUpdate(updatedBy);
    }

    /// <summary>Update template name and description — only allowed in Draft/Active status.</summary>
    public void UpdateInfo(string name, string nameVi, Guid updatedBy)
    {
        Name = name;
        NameVi = nameVi;
        SetAuditUpdate(updatedBy);
    }

    /// <summary>Deactivate template — no longer accepts submissions.</summary>
    public void Deactivate(Guid updatedBy)
    {
        Status = FormTemplateStatus.Inactive;
        SetAuditUpdate(updatedBy);
    }

    /// <summary>Configure PDPL consent requirement for this template.</summary>
    public void SetConsentConfig(bool requiresConsent, string? consentText, Guid updatedBy)
    {
        RequiresConsent = requiresConsent;
        ConsentText = consentText;
        SetAuditUpdate(updatedBy);
    }

    /// <summary>
    /// Opt this template into (or out of) anonymous public accessibility. Drives the RLS
    /// public-read carve-out; server-controlled, never set from client input.
    /// </summary>
    public void SetPublicAccess(bool isPublic, Guid updatedBy)
    {
        IsPublic = isPublic;
        SetAuditUpdate(updatedBy);
    }

    /// <summary>Set optional workflow definition for automated approval.</summary>
    public void SetApprovalWorkflow(Guid? workflowDefinitionId, Guid updatedBy)
    {
        ApprovalWorkflowDefinitionId = workflowDefinitionId;
        SetAuditUpdate(updatedBy);
    }

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Code validation: alphanumeric, underscore, hyphen; starts with letter/underscore; max 100.</summary>
    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidFormSchemaException("GOV_FRM_014: Code không được để trống.");

        if (!Regex.IsMatch(code, @"^[a-zA-Z_][a-zA-Z0-9_\-]{0,99}$"))
            throw new InvalidFormSchemaException(
                "GOV_FRM_014: Code phải bắt đầu bằng chữ cái hoặc '_', chỉ chứa chữ cái, số, '_', '-', tối đa 100 ký tự.");
    }
}
