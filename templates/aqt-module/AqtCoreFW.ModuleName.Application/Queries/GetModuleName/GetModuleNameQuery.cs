using AqtCoreFW.ModuleName.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.ModuleName.Application.Queries.GetModuleName;

public sealed record GetModuleNameQuery(long Id, Guid TenantId) : IQuery<ModuleNameDto>;
