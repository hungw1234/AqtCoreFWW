namespace AqtCoreFW.Audit.Domain.Enums;

/// <summary>Result of evaluating an entity against a compliance policy.</summary>
public enum EvaluationResult
{
    Pass = 0,
    Fail = 1,
    Warning = 2
}
