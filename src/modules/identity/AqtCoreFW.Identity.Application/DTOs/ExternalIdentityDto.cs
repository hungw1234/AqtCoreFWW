using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Application.DTOs;

/// <summary>Read DTO for ExternalIdentity.</summary>
public sealed record ExternalIdentityDto(
    long Id,
    Guid UserId,
    ExternalIdentityProvider Provider,
    string ExternalId,
    string? DisplayName,
    string? Email,
    DateTime LinkedAt,
    DateTime? LastSyncAt,
    bool IsActive);
