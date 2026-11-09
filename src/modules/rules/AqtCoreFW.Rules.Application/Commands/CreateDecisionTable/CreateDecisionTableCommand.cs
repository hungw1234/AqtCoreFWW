using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Rules.Application.Commands.CreateDecisionTable;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateDecisionTableCommand(
    Guid RuleSetId,
    Guid TenantId,
    string Name,
    string InputColumns,
    string OutputColumns,
    string Rows) : ICommand<DecisionTableDto>;
