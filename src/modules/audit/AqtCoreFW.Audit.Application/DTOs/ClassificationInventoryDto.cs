namespace AqtCoreFW.Audit.Application.DTOs;

/// <summary>Full data classification inventory report returned by GetClassificationInventoryQuery.</summary>
public sealed record ClassificationInventoryDto(
    DateTime ScannedAt,
    int TotalCount,
    int RestrictedCount,
    int ConfidentialCount,
    IReadOnlyList<ClassifiedFieldDto> Entries);
