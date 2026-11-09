using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Application.DTOs;

/// <summary>Read DTO for <see cref="AqtCoreFW.Audit.Domain.Entities.ClassificationRule"/>.</summary>
public sealed record ClassificationRuleDto(
    DataClassificationLevel Level,
    bool MustEncryptAtRest,
    bool AllowExport,
    int? MaxRetentionDays,
    string UpdatedBy,
    DateTime UpdatedAt,
    byte[] RowVersion);
