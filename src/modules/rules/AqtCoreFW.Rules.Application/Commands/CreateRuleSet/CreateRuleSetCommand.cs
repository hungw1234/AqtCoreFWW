using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Rules.Application.Commands.CreateRuleSet;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateRuleSetCommand(
    Guid TenantId,
    string Name,
    string? Description,
    string? Category,
    string? TargetEntityType) : ICommand<RuleSetDto>;
