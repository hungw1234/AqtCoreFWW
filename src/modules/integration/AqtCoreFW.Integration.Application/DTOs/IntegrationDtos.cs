namespace AqtCoreFW.Integration.Application.DTOs;

/// <summary>
/// T2 Partner DTO — Id is Guid (PublicId AK, projected via SELECT PublicId AS Id).
/// Retiered T1→T2 per Phase-07: external addressing uses Guid AK instead of long internal PK.
/// CreatedAt/UpdatedAt are DateTime (datetime2(3)) per Entity base — must match SQL column type for Dapper materialization.
/// </summary>
public sealed record PartnerDto(
    Guid Id, Guid TenantId, string Name, string Code,
    string? ContactEmail, string? ContactPhone,
    string? Endpoint, string? AuthScheme, string Status,
    DateTime CreatedAt, DateTime? UpdatedAt);

/// <summary>
/// T2 Contract DTO — Id is Guid (PublicId AK, projected via SELECT PublicId AS Id).
/// PartnerId is Guid (Partner.PublicId AK, projected via JOIN p.PublicId AS PartnerId).
/// CreatedAt/UpdatedAt are DateTime (datetime2(3)) per Entity base — must match SQL column type for Dapper materialization.
/// </summary>
public sealed record ContractDto(
    Guid Id, Guid TenantId, Guid PartnerId, string Title, string? Description,
    DateTime EffectiveDate, DateTime? ExpiryDate, string Status,
    string? DataScopeJson,
    DateTime CreatedAt, DateTime? UpdatedAt);

/// <summary>
/// T3 MessageLog DTO — Id is Guid (MessageId AK, projected via SELECT MessageId AS Id).
/// PartnerId is Guid (Partner.PublicId AK, projected via JOIN p.PublicId AS PartnerId).
/// ContractId is Guid? (Contract.PublicId AK, projected via LEFT JOIN c.PublicId AS ContractId).
/// CreatedAt/UpdatedAt are DateTime (datetime2(3)) per Entity base — must match SQL column type for Dapper materialization.
/// </summary>
public sealed record MessageLogDto(
    Guid Id, Guid TenantId, Guid PartnerId, Guid? ContractId,
    string Direction, string MessageType, string? Payload,
    string Status, string? CorrelationId,
    DateTime SentAt, DateTime? AcknowledgedAt,
    DateTime CreatedAt, DateTime? UpdatedAt);
