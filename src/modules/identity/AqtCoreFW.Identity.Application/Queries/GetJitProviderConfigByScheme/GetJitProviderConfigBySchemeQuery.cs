using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.GetJitProviderConfigByScheme;

public sealed record GetJitProviderConfigBySchemeQuery(string Scheme) : IQuery<JitProviderConfigDto>;
