using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.MasterData.Domain.Entities;

/// <summary>
/// Maps old 63-province codes to new 34-province codes (post QD 19/2025/QD-TTg, effective 01/07/2025).
/// Many-to-one: multiple old provinces map to one new province when merged.
/// Identity mapping (OldCode == NewCode) for provinces that remain unchanged.
/// </summary>
public class ProvinceLegacyMapping : Entity
{
    /// <summary>Old province code (from 63-province system, e.g., "02" for Ha Giang).</summary>
    public string OldCode { get; private set; } = default!;

    /// <summary>New province code (from 34-province system, e.g., "04" for merged Cao Bang).</summary>
    public string NewCode { get; private set; } = default!;

    /// <summary>Date when the old province merged into the new one (QD 19/2025: 01/07/2025).</summary>
    public DateTimeOffset MergedAt { get; private set; }

    /// <summary>Optional notes (e.g., "Merged per QD 19/2025").</summary>
    public string? Notes { get; private set; }

    private ProvinceLegacyMapping() { }

    public static ProvinceLegacyMapping Create(
        string oldCode,
        string newCode,
        DateTimeOffset mergedAt,
        string? notes = null) =>
        new()
        {
            OldCode = oldCode,
            NewCode = newCode,
            MergedAt = mergedAt,
            Notes = notes
        };

    /// <summary>True if this is an identity mapping (province unchanged in reform).</summary>
    public bool IsIdentity => OldCode == NewCode;
}
