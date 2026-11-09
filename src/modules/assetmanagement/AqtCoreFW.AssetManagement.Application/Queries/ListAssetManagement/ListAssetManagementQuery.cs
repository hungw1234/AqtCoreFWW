using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.AssetManagement.Application.DTOs;

namespace AqtCoreFW.AssetManagement.Application.Queries.ListAssetManagement;

public sealed record ListAssetManagementQuery(Guid TenantId) : IQuery<IReadOnlyList<AssetManagementDto>>;
