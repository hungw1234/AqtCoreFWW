using AqtCoreFW.MasterData.Domain.Entities;

namespace AqtCoreFW.MasterData.Application.DTOs;

/// <summary>Read DTO for Dictionary.</summary>
public sealed record DictionaryDto(
    Guid Id,
    string Code,
    string Name,
    string NameVi,
    string? Description,
    DictionaryStatus Status,
    int CurrentVersion,
    Guid TenantId,
    bool IsSystemDefined);

/// <summary>Read DTO for DictionaryItem (flat — tree built client-side from ParentId).</summary>
public sealed record DictionaryItemDto(
    Guid Id,
    Guid DictionaryId,
    string Code,
    string Name,
    string NameVi,
    Guid? ParentId,
    int SortOrder,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive);

/// <summary>Read DTO for ExternalMapping.</summary>
public sealed record ExternalMappingDto(
    Guid Id,
    string InternalCode,
    string ExternalSystem,
    string ExternalCode,
    MappingDirection Direction,
    Guid? DictionaryId,
    bool IsActive,
    DateTime ValidFrom,
    DateTime? ValidTo,
    Guid TenantId);
