using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SystemParams.Application.DTOs;

namespace AqtCoreFW.SystemParams.Application.Queries;

/// <summary>Get single system parameter by key (excluding feature: prefix).</summary>
public sealed record GetSystemParamByKeyQuery(string Key, Guid TenantId) : IQuery<SystemParamDto>;
