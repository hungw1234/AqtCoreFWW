using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SystemParams.Application.DTOs;

namespace AqtCoreFW.SystemParams.Application.Queries;

/// <summary>Get all system parameters (excluding feature: prefix) for tenant.</summary>
public sealed record GetAllSystemParamsQuery(Guid TenantId) : IQuery<IReadOnlyList<SystemParamDto>>;
