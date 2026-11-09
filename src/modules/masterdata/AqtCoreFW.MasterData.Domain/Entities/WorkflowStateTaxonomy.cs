using AqtCoreFW.MasterData.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.MasterData.Domain.Entities;

/// <summary>
/// Masterdata vocabulary for workflow STATE codes (soft-FK, copy semantics).
/// Authoring aid only — runtime workflow engine never joins against this table.
/// Global-only MVP (no TenantId); standardized gov vocabulary.
/// Classification: Public (V3 — QĐ742).
/// </summary>
public sealed class WorkflowStateTaxonomy : AuditableEntity, IPublicIdentified
{
    /// <summary>Public-facing AK (controllers route by Guid). AMEND-A13 re-tier T1→T2.</summary>
    public Guid PublicId { get; private set; }

    public string Code { get; private set; } = default!;
    public string NameVi { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public string? I18nKey { get; private set; }
    public string? Icon { get; private set; }
    public string? Color { get; private set; }
    public WorkflowStateCategory Category { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public string? DeprecatedReason { get; private set; }

    /// <summary>Optimistic concurrency token — prevents last-write-wins on concurrent admin edits (R3).</summary>
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowStateTaxonomy() { } // EF Core

    public static WorkflowStateTaxonomy Create(
        IGuidGenerator gen,
        string code,
        string nameVi,
        string nameEn,
        WorkflowStateCategory category,
        int sortOrder = 0,
        string? i18nKey = null,
        string? icon = null,
        string? color = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameVi);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameEn);

        return new WorkflowStateTaxonomy
        {
            PublicId = gen.NewIdRandom(),
            Code = code,
            NameVi = nameVi,
            NameEn = nameEn,
            Category = category,
            SortOrder = sortOrder,
            I18nKey = i18nKey,
            Icon = icon,
            Color = color,
            IsActive = true,
            DeprecatedReason = null,
            ClassificationLevel = ClassificationLevel.Public, // V3 — gov public vocabulary
        };
    }

    /// <summary>Soft-deprecate (admin-facing). Row remains in DB; queries can opt-in via includeInactive.</summary>
    public void Deprecate(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        IsActive = false;
        DeprecatedReason = reason;
        MarkUpdated();
    }

    /// <summary>Reactivate a previously deprecated row. Caller must check no-collision with another active same-Code row.</summary>
    public void Restore()
    {
        IsActive = true;
        DeprecatedReason = null;
        MarkUpdated();
    }

    public void Update(
        string nameVi,
        string nameEn,
        string? i18nKey,
        string? icon,
        string? color,
        WorkflowStateCategory category,
        int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameVi);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameEn);

        NameVi = nameVi;
        NameEn = nameEn;
        I18nKey = i18nKey;
        Icon = icon;
        Color = color;
        Category = category;
        SortOrder = sortOrder;
        MarkUpdated();
    }
}
