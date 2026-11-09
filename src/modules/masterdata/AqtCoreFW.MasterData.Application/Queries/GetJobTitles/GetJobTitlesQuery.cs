using AqtCoreFW.MasterData.Application.Queries.GetCaseTypes;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.MasterData.Application.Queries.GetJobTitles;

/// <summary>Query to fetch active job titles for a tenant (includes global job titles where TenantId is null).</summary>
public sealed record GetJobTitlesQuery(Guid? TenantId) : IQuery<IReadOnlyList<MasterDataItemDto>>;
