using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Rules.Domain.Entities;

/// <summary>
/// Test case for validating rule behaviour against known inputs/outputs.
/// LastRunResult records the engine's actual output on the most recent run.
/// T1 mutable entity: AuditableEntity (non-generic, long PK).
/// RuleSetId: long (FK to RuleSet.Id internal PK).
/// </summary>
public sealed class RuleTestCase : AuditableEntity
{
    /// <summary>FK to RuleSet.Id (long, T2 internal PK).</summary>
    public long RuleSetId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    /// <summary>JSON object — input passed to the rule engine during test run.</summary>
    public string InputData { get; private set; } = "{}";
    /// <summary>JSON object — expected engine output to compare against.</summary>
    public string ExpectedOutput { get; private set; } = "{}";
    public string? LastRunResult { get; private set; }
    public DateTime? LastRunAt { get; private set; }

    private RuleTestCase() { } // EF Core

    /// <summary>Factory — creates a new test case for a RuleSet.</summary>
    public static RuleTestCase Create(
        long ruleSetId,
        string name,
        string inputData,
        string expectedOutput,
        Guid createdBy)
    {
        var tc = new RuleTestCase
        {
            RuleSetId = ruleSetId,
            Name = name,
            InputData = inputData,
            ExpectedOutput = expectedOutput
        };
        tc.SetAuditCreate(createdBy);
        return tc;
    }

    /// <summary>Persists the outcome of a test run (pass/fail JSON or error string).</summary>
    public void RecordResult(string result, DateTime timestamp)
    {
        LastRunResult = result;
        LastRunAt = timestamp;
        MarkUpdated();
    }
}
