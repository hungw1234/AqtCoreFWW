using AqtCoreFW.Audit.Domain.Enums;

namespace AqtCoreFW.Audit.Application.DTOs;

/// <summary>
/// Read model for CompliancePolicy.
/// Id is long (T1 bigint PK — CompliancePolicy has no PublicId AK).
/// </summary>
public sealed record CompliancePolicyDto(
    long Id,
    string Name,
    ComplianceCategory Category,
    string Rules,
    PolicyEnforcement Enforcement,
    bool IsEnabled,
    DateTimeOffset CreatedAt);
