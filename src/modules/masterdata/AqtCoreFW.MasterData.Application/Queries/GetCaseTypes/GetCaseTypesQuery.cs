using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.MasterData.Application.Queries.GetCaseTypes;

/// <summary>Query to fetch active case types for a tenant (includes global case types where TenantId is null).</summary>
public sealed record GetCaseTypesQuery(Guid? TenantId) : IQuery<IReadOnlyList<MasterDataItemDto>>;

/// <summary>Lightweight DTO for master data items (CaseType, JobTitle).</summary>
public sealed record MasterDataItemDto(string Code, string NameVi, string NameEn);
