using AqtCoreFW.AssetManagement.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.AssetManagement.Application.Queries.GetAssetManagement;

public sealed record GetAssetManagementQuery(long Id, Guid TenantId) : IQuery<AssetManagementDto>;
