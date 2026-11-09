using System.Reflection;
using System.Text.RegularExpressions;
using AqtCoreFW.MasterData.Domain.Constants;
using FluentAssertions;

namespace AqtCoreFW.MasterData.Tests.Constants;

/// <summary>
/// TC-MD-C001: WorkflowTaxonomyCodes baseline vocabulary — 10 states + 9 actions (plan §Seed Data,
/// phase-01 added ReturnedForRevision for legacy Cases parity).
/// Guards seeder idempotency (codes must not drift from constants) and validator regex contract.
/// </summary>
public sealed class WorkflowTaxonomyCodesTests
{
    // Plan §Seed Data — authoritative list. Tests assert constants exactly match; any drift breaks seeder.
    private static readonly string[] ExpectedStateCodes =
    [
        "Draft", "Submitted", "UnderReview", "Assigned",
        "Approved", "Rejected", "Returned", "ReturnedForRevision", "Closed", "Cancelled",
    ];

    private static readonly string[] ExpectedActionCodes =
    [
        "Submit", "Assign", "Approve", "Reject",
        "Return", "Close", "Cancel", "Reopen", "Reassign",
    ];

    /// <summary>Mirrors BE CreateWorkflowStateTaxonomyCommandValidator regex (phase-00 §Validation).</summary>
    private static readonly Regex CodeRegex = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    // --- WorkflowStates ---

    [Fact]
    public void WorkflowStates_HasExactlyTenCodes()
    {
        var codes = GetStringConstants(typeof(WorkflowTaxonomyCodes.WorkflowStates));

        codes.Should().HaveCount(10);
    }

    [Fact]
    public void WorkflowStates_MatchesPlanSeedData()
    {
        var codes = GetStringConstants(typeof(WorkflowTaxonomyCodes.WorkflowStates));

        codes.Should().BeEquivalentTo(ExpectedStateCodes);
    }

    [Fact]
    public void WorkflowStates_AllCodesMatchRegex()
    {
        var codes = GetStringConstants(typeof(WorkflowTaxonomyCodes.WorkflowStates));

        codes.Should().OnlyContain(c => CodeRegex.IsMatch(c));
    }

    [Fact]
    public void WorkflowStates_AllCodesUnique()
    {
        var codes = GetStringConstants(typeof(WorkflowTaxonomyCodes.WorkflowStates));

        codes.Should().OnlyHaveUniqueItems();
    }

    // --- WorkflowActions ---

    [Fact]
    public void WorkflowActions_HasExactlyNineCodes()
    {
        var codes = GetStringConstants(typeof(WorkflowTaxonomyCodes.WorkflowActions));

        codes.Should().HaveCount(9);
    }

    [Fact]
    public void WorkflowActions_MatchesPlanSeedData()
    {
        var codes = GetStringConstants(typeof(WorkflowTaxonomyCodes.WorkflowActions));

        codes.Should().BeEquivalentTo(ExpectedActionCodes);
    }

    [Fact]
    public void WorkflowActions_AllCodesMatchRegex()
    {
        var codes = GetStringConstants(typeof(WorkflowTaxonomyCodes.WorkflowActions));

        codes.Should().OnlyContain(c => CodeRegex.IsMatch(c));
    }

    [Fact]
    public void WorkflowActions_AllCodesUnique()
    {
        var codes = GetStringConstants(typeof(WorkflowTaxonomyCodes.WorkflowActions));

        codes.Should().OnlyHaveUniqueItems();
    }

    /// <summary>Reflects public const string fields — resilient to order + allows adding new codes without test edit (within count bound).</summary>
    private static List<string> GetStringConstants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
}
