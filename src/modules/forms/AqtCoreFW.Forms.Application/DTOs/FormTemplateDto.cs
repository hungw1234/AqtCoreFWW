namespace AqtCoreFW.Forms.Application.DTOs;

/// <summary>
/// Read-side projection of a FormTemplate for API responses.
/// Id is FormTemplate.PublicId (Guid AK) — T2 external addressing key.
/// FormTemplate retiered T1→T2 per Phase-07 (IAggregateRoot + RowVersion + lifecycle profile).
/// </summary>
public sealed record FormTemplateDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string NameVi,
    string Code,
    string StorageMode,
    string Status,
    int Version,
    DateTimeOffset CreatedAt,
    IReadOnlyList<FormFieldDto> Fields,
    bool RequiresConsent = false,
    string? ConsentText = null,
    Guid? ApprovalWorkflowDefinitionId = null,
    // Anonymous public reachability flag (RLS public-read carve-out). Trailing optional
    // so existing positional callers are unaffected; only the single-template GET projects it.
    bool IsPublic = false);
