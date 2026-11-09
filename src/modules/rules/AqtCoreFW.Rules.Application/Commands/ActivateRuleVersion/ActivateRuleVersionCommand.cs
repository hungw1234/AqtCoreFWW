using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Rules.Application.Commands.ActivateRuleVersion;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record ActivateRuleVersionCommand(Guid RuleSetId) : ICommand<RuleSetDto>;
