namespace AqtCoreFW.Rules.Domain.Enums;

/// <summary>Lifecycle status for a RuleSet aggregate.</summary>
public enum RuleSetStatus
{
    /// <summary>Being designed — not evaluated in production.</summary>
    Draft,
    /// <summary>Live and evaluated against inputs.</summary>
    Active,
    /// <summary>Superseded — kept for audit history only.</summary>
    Deprecated
}
