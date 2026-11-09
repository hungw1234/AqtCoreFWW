namespace AqtCoreFW.Contracts.Application.DTOs;

public sealed record ContractDto(
    long Id,
    Guid TenantId,
    string ContractNumber,
    string Title,
    string CounterpartyName,
    DateTime StartDate,
    DateTime EndDate,
    string Status,
    string? Description,
    DateTime CreatedAt);
