namespace AqtCoreFW.Rules.Domain.Enums;

/// <summary>What the engine should do when an individual rule fails.</summary>
public enum FailureAction
{
    /// <summary>Throw an exception and stop evaluation.</summary>
    Throw,
    /// <summary>Skip the rule and continue.</summary>
    Skip,
    /// <summary>Use configured default value and continue.</summary>
    Default
}
