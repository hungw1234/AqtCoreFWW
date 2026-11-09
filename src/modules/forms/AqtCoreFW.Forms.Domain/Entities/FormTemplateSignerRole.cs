using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// Defines a named signer role slot on a FormTemplate for multi-signer workflows.
/// RoleKey is a stable slug used to correlate which signer filled which role across
/// FormSubmissionSignerEvidence rows.
/// T1 entity: non-generic Entity (long Id DB-assigned).
/// </summary>
public sealed class FormTemplateSignerRole : Entity
{
    public long FormTemplateId { get; private set; }

    /// <summary>Stable slug identifying this role (e.g. "tham-dinh-vien", "lanh-dao"). Max 128 chars. Unique per template.</summary>
    public string RoleKey { get; private set; } = string.Empty;

    /// <summary>Vietnamese display label for this role shown in PDF and UI.</summary>
    public string RoleLabelVi { get; private set; } = string.Empty;

    /// <summary>Render order for the signer block in generated documents and UI. Lower = first.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>Whether this role must sign before a submission is considered complete.</summary>
    public bool IsRequired { get; private set; }

    /// <summary>
    /// Optional JSON describing anchor/coordinate placement for the signature stamp on the generated PDF.
    /// Null = no automatic anchor placement (signer uses free placement).
    /// Schema is provider-specific; stored opaque at the domain layer.
    /// nvarchar(max) — see EF configuration.
    /// </summary>
    public string? PlacementJson { get; private set; }

    private FormTemplateSignerRole() { } // EF Core

    public static FormTemplateSignerRole Create(
        long formTemplateId,
        string roleKey,
        string roleLabelVi,
        int displayOrder,
        bool isRequired,
        string? placementJson)
    {
        if (string.IsNullOrWhiteSpace(roleKey))
            throw new ArgumentException("RoleKey không được để trống.", nameof(roleKey));

        if (roleKey.Length > 128)
            throw new ArgumentException("RoleKey không được vượt quá 128 ký tự.", nameof(roleKey));

        return new FormTemplateSignerRole
        {
            FormTemplateId = formTemplateId,
            RoleKey = roleKey.Trim(),
            RoleLabelVi = roleLabelVi,
            DisplayOrder = displayOrder,
            IsRequired = isRequired,
            PlacementJson = placementJson
        };
    }
}
