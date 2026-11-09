using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SystemParams.Application.DTOs;

namespace AqtCoreFW.SystemParams.Application.Commands;

/// <summary>Update system parameter value (blocks feature: prefix).</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateSystemParamCommand(
    string Key,
    string Value,
    Guid TenantId) : ICommand<UpdateSystemParamResult>;
