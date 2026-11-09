using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.GetFormVersionDiff;

/// <summary>
/// Compares fields between two versions of a template.
/// TemplatePublicId = FormTemplate.PublicId (T2 Guid AK — external addressing).
/// Uses AddedInVersion per field — fields added after fromVersion but not after toVersion are "added",
/// fields active at fromVersion but missing at toVersion are "removed".
/// </summary>
public sealed record GetFormVersionDiffQuery(
    Guid TemplatePublicId,
    Guid TenantId,
    int FromVersion,
    int ToVersion) : IQuery<FormVersionDiffDto>;

public sealed record FormVersionDiffDto(
    int FromVersion,
    int ToVersion,
    IReadOnlyList<DiffFieldDto> Added,
    IReadOnlyList<DiffFieldDto> Removed,
    IReadOnlyList<DiffFieldDto> Modified);

public sealed record DiffFieldDto(string FieldKey, string LabelVi, string LabelEn, int AddedInVersion, bool IsActive);
