namespace AqtCoreFW.MasterData.Application.DTOs;

/// <summary>
/// Read DTO for WorkflowStateTaxonomy. RowVersion included for optimistic concurrency
/// (Update commands require it in request body). Category serialized as string for FE readability.
/// </summary>
public sealed record WorkflowStateTaxonomyDto(
    Guid Id,
    string Code,
    string NameVi,
    string NameEn,
    string? I18nKey,
    string? Icon,
    string? Color,
    string Category,
    int SortOrder,
    bool IsActive,
    string? DeprecatedReason,
    byte[] RowVersion);

/// <summary>
/// Read DTO for WorkflowActionTaxonomy. No Category, no Color (plan entity spec).
/// </summary>
public sealed record WorkflowActionTaxonomyDto(
    Guid Id,
    string Code,
    string NameVi,
    string NameEn,
    string? I18nKey,
    string? Icon,
    int SortOrder,
    bool IsActive,
    string? DeprecatedReason,
    byte[] RowVersion);
