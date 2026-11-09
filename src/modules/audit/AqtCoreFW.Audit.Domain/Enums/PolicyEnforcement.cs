namespace AqtCoreFW.Audit.Domain.Enums;

/// <summary>How a compliance policy is enforced when triggered.</summary>
public enum PolicyEnforcement
{
    /// <summary>Log the violation without blocking the operation.</summary>
    Audit = 0,

    /// <summary>Block the operation and log the violation.</summary>
    Block = 1
}
