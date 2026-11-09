using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.Rules.Domain.Enums;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Rules.Application.Commands.UpdateRule;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateRuleCommand(
    Guid RuleSetId,
    Guid RuleId,
    string? Name,
    string? Expression,
    int? Priority,
    bool? Enabled,
    FailureAction? FailureAction) : ICommand<RuleDto>;
