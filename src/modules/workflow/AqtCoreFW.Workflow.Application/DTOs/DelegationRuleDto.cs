namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// DTO returned for DelegationRule read operations.
/// Phase-4b B4c: T1 entity — Id is long (bigint identity PK, DB-assigned).
/// </summary>
public sealed record DelegationRuleDto(
    long Id,
    Guid DelegatorUserId,
    Guid DelegateUserId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    bool IsActive,
    Guid TenantId,
    DateTimeOffset CreatedAt,
    Guid CreatedBy);
