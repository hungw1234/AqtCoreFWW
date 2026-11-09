using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.ManageAbacRule;

/// <summary>Delete an ABAC attribute rule by ID.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
// arch:suppress R_T2FK Rule: T1 internal-only entity identity.AttributeRule (regex collision with T2 rules.Rule)
public sealed record DeleteAbacRuleCommand(long RuleId) : ICommand;
