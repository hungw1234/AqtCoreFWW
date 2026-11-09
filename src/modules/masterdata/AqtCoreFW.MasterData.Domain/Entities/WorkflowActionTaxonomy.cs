using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.MasterData.Domain.Entities;

/// <summary>
/// Masterdata vocabulary for workflow ACTION codes (soft-FK, copy semantics).
/// Authoring aid only — runtime engine never joins against this table.
/// No Category field (actions are flat); no Color (phase-00 plan §Entity line 165).
/// Classification: Public (V3 — QĐ742).
/// </summary>
public sealed class WorkflowActionTaxonomy : AuditableEntity, IPublicIdentified
{
    /// <summary>Public-facing AK (controllers route by Guid). AMEND-A13 re-tier T1→T2.</summary>
    public Guid PublicId { get; private set; }

    public string Code { get; private set; } = default!;
    public string NameVi { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    public string? I18nKey { get; private set; }
    public string? Icon { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public string? DeprecatedReason { get; private set; }

    /// <summary>Optimistic concurrency token — prevents last-write-wins on concurrent admin edits (R3).</summary>
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowActionTaxonomy() { } // EF Core

    public static WorkflowActionTaxonomy Create(
        IGuidGenerator gen,
        string code,
        string nameVi,
        string nameEn,
        int sortOrder = 0,
        string? i18nKey = null,
        string? icon = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameVi);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameEn);

        return new WorkflowActionTaxonomy
        {
            PublicId = gen.NewIdRandom(),
            Code = code,
            NameVi = nameVi,
            NameEn = nameEn,
            SortOrder = sortOrder,
            I18nKey = i18nKey,
            Icon = icon,
            IsActive = true,
            DeprecatedReason = null,
            ClassificationLevel = ClassificationLevel.Public, // V3
        };
    }

    public void Deprecate(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        IsActive = false;
        DeprecatedReason = reason;
        MarkUpdated();
    }

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
        int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameVi);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameEn);

        NameVi = nameVi;
        NameEn = nameEn;
        I18nKey = i18nKey;
        Icon = icon;
        SortOrder = sortOrder;
        MarkUpdated();
    }
}
