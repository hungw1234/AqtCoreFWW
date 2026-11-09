using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Application.MfaPolicies;

/// <summary>
/// Read-model DTO for MfaPolicy — returned by GetMfaPoliciesQuery.
/// T2: Guid Id maps from entity.PublicId (external AK). Internal long Id never exposed.
/// </summary>
public sealed record MfaPolicyDto(
    Guid Id,
    string Role,
    bool IsMandatory,
    int GraceDays,
    MfaFactor AllowedFactors,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
