using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Maps tenant + entity type → workflow definition.
/// Specificity auto-calculated: SystemDefault(0) &lt; Tenant(1) &lt; TenantAndEntity(2) &lt; TenantEntitySubType(3).
/// Resolution picks the highest-specificity active rule within effective date window.
/// Priority breaks ties at the same specificity level.
/// T1 entity: non-generic AuditableEntity (long Id DB-assigned).
///   WorkflowDefinitionId: long (FK to T2 WorkflowDefinition.Id).
///   TenantId: Guid (FK exception).
/// </summary>
public sealed class WorkflowAssignmentRule : AuditableEntity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public long WorkflowDefinitionId { get; private set; }

    /// <summary>Business entity type (e.g. "Generic", "Application"). Null = system-wide default.</summary>
    public string? EntityType { get; private set; }

    /// <summary>Sub-type within EntityType (e.g. "ConstructionPermit"). Null = applies to all sub-types.</summary>
    public string? EntitySubType { get; private set; }

    /// <summary>Auto-calculated from EntityType/EntitySubType nullness. Higher = wins resolution.</summary>
    public AssignmentRuleSpecificity Specificity { get; private set; }

    /// <summary>Tie-breaker when multiple rules share the same Specificity. Higher wins.</summary>
    public int Priority { get; private set; }

    public bool IsActive { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveUntil { get; private set; }

    private WorkflowAssignmentRule() { } // EF Core

    /// <summary>
    /// Create a new assignment rule. Specificity is derived automatically:
    /// - No EntityType → Tenant
    /// - EntityType only → TenantAndEntity
    /// - EntityType + EntitySubType → TenantEntitySubType
    /// </summary>
    public static WorkflowAssignmentRule Create(
        Guid tenantId,
        long workflowDefinitionId,
        string? entityType,
        string? entitySubType,
        int priority,
        string? description,
        Guid createdBy,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveUntil = null)
    {
        var rule = new WorkflowAssignmentRule
        {
            TenantId = tenantId,
            WorkflowDefinitionId = workflowDefinitionId,
            EntityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType.Trim(),
            EntitySubType = string.IsNullOrWhiteSpace(entitySubType) ? null : entitySubType.Trim(),
            Priority = priority,
            Description = description,
            IsActive = true,
            EffectiveFrom = effectiveFrom ?? DateTimeOffset.UtcNow,
            EffectiveUntil = effectiveUntil
        };

        rule.Specificity = CalculateSpecificity(rule.EntityType, rule.EntitySubType);
        rule.SetAuditCreate(createdBy);
        return rule;
    }

    /// <summary>Soft-deactivate without deleting — preserves audit history.</summary>
    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Derives specificity level from entity scope.
    /// EntitySubType requires EntityType to be meaningful.
    /// </summary>
    private static AssignmentRuleSpecificity CalculateSpecificity(string? entityType, string? entitySubType) =>
        (entityType, entitySubType) switch
        {
            (null, _) => AssignmentRuleSpecificity.Tenant,
            (not null, null) => AssignmentRuleSpecificity.TenantAndEntity,
            (not null, not null) => AssignmentRuleSpecificity.TenantEntitySubType
        };
}
