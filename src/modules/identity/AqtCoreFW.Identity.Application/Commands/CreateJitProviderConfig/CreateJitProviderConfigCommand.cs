using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.CreateJitProviderConfig;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateJitProviderConfigCommand(
    string Scheme,
    string DisplayName,
    ExternalIdentityProvider ProviderType,
    bool JitEnabled,
    string DefaultRoleName,
    bool RequireApproval,
    string? ClaimMappingJson,
    Guid? DefaultTenantId,
    string? AllowedDomainsJson,
    int MaxProvisionsPerHour,
    Guid ActorId) : ICommand<long>;
