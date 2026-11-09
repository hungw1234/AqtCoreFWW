namespace AqtCoreFW.Workflow.Domain.Enums;

/// <summary>
/// 4-level specificity for workflow assignment rules.
/// Higher value = more specific = wins resolution.
/// </summary>
public enum AssignmentRuleSpecificity
{
    SystemDefault = 0,
    Tenant = 1,
    TenantAndEntity = 2,
    TenantEntitySubType = 3
}
