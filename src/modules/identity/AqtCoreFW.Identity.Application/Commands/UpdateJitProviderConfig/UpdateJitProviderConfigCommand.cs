using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.UpdateJitProviderConfig;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateJitProviderConfigCommand(
    long Id,
    string DisplayName,
    bool JitEnabled,
    string DefaultRoleName,
    bool RequireApproval,
    string? ClaimMappingJson,
    Guid? DefaultTenantId,
    string? AllowedDomainsJson,
    int MaxProvisionsPerHour,
    Guid ActorId) : ICommand;
