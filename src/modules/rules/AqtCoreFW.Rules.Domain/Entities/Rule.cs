using AqtCoreFW.Rules.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Rules.Domain.Entities;

/// <summary>
/// Single evaluation rule within a RuleSet.
/// Expression stored as JSON — evaluated by IRuleEvaluator at runtime.
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid AK).
/// RuleSetId: long (FK to RuleSet.Id internal PK).
/// </summary>
public sealed class Rule : AuditableEntity, IPublicIdentified
{
    public Guid PublicId { get; private set; }
    /// <summary>FK to RuleSet.Id (long, T2 internal PK).</summary>
    public long RuleSetId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    /// <summary>JSON-serialised condition expression.</summary>
    public string Expression { get; private set; } = string.Empty;
    public int Priority { get; private set; }
    public bool Enabled { get; private set; } = true;
    public string? ErrorMessage { get; private set; }
    public FailureAction FailureAction { get; private set; } = FailureAction.Skip;

    private Rule() { } // EF Core

    /// <summary>Factory — creates a new Rule within a RuleSet.</summary>
    public static Rule Create(
        IGuidGenerator gen,
        long ruleSetId,
        string name,
        string expression,
        Guid createdBy,
        int priority = 0,
        bool enabled = true,
        string? errorMessage = null,
        FailureAction failureAction = FailureAction.Skip)
    {
        var rule = new Rule
        {
            PublicId = gen.NewIdMonotonic(),
            RuleSetId = ruleSetId,
            Name = name,
            Expression = expression,
            Priority = priority,
            Enabled = enabled,
            ErrorMessage = errorMessage,
            FailureAction = failureAction
        };
        rule.SetAuditCreate(createdBy);
        return rule;
    }

    /// <summary>Updates mutable rule fields.</summary>
    public void Update(
        string? name,
        string? expression,
        int? priority,
        bool? enabled,
        FailureAction? failureAction,
        Guid modifiedBy)
    {
        if (name is not null) Name = name;
        if (expression is not null) Expression = expression;
        if (priority.HasValue) Priority = priority.Value;
        if (enabled.HasValue) Enabled = enabled.Value;
        if (failureAction.HasValue) FailureAction = failureAction.Value;
        SetAuditUpdate(modifiedBy);
    }
}
