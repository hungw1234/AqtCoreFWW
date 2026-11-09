using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.ManageAbacRule;

/// <summary>Update an existing ABAC attribute rule.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record UpdateAbacRuleCommand(
    // arch:suppress R_T2FK Rule: T1 internal-only entity identity.AttributeRule (regex collision with T2 rules.Rule)
    long RuleId,
    string Resource,
    string Action,
    string AttributeKey,
    string AttributeValue,
    string Effect,
    Guid? TenantId) : ICommand;
