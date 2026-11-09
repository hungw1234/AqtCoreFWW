using System.Text.Json;
using AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentAssertions;

namespace AqtCoreFW.Workflow.Application.Tests.Commands.SaveDefinitionGraph;

/// <summary>
/// Phase 02 contract-shape tests for the WorkflowTransitionAssignment payload added to
/// SaveDefinitionGraph. TDD red-bar: these tests reference types/fields that DO NOT
/// yet exist; they must compile (and then pass) only AFTER Phase 02 implementation lands.
///
/// Plan: plans/260523-1241-extend-save-graph-with-transition-assignment/phase-02-*
/// Q-ratification anchors: Q2 replace-all / Q6 strict-at-Save / Q7 expose AllowAssignorOverride.
///
/// Coverage:
///   C1  GraphTransitionInput.Assignment is OPTIONAL (default null) — backwards-compatible.
///   C2  AssignmentInput round-trip preserves CombineMode + AllowAssignorOverride + Clauses.
///   C3  AssignmentClauseInput round-trip preserves Mode + RefsJson.
///   C4  NewTransitionSpec.Assignment mirror exists + accepts spec records.
///   C5  WorkflowTransitionDto.Assignment is OPTIONAL projection field.
///   C6  JSON binding accepts string-enum form per host JsonStringEnumConverter policy.
/// </summary>
public sealed class SaveDefinitionGraphAssignmentContractTests
{
    // C1: backwards-compatibility — every existing call site that omits Assignment must still build a valid input.
    [Fact]
    public void C1_GraphTransitionInput_AssignmentField_DefaultsToNull()
    {
        var t = new GraphTransitionInput(
            FromStateName: "Draft",
            ToStateName: "InReview",
            ActionName: "Submit",
            ActionLabelVi: "Nộp",
            ActionLabelEn: "Submit",
            ConditionsJson: null,
            SortOrder: 1);

        t.Assignment.Should().BeNull();
    }

    // C2: AssignmentInput shape — Q7 (AllowAssignorOverride opt-in default false) + Q2 (null clauses list = empty rejected by validator, not contract).
    [Fact]
    public void C2_AssignmentInput_RoundTrip_PreservesCombineModeAndOverrideAndClauses()
    {
        var clauses = new List<AssignmentClauseInput>
        {
            new(AssignmentClauseMode.ByPermission, """["Workflow.Task.Manage"]"""),
            new(AssignmentClauseMode.Initiator, null)
        };
        var a = new AssignmentInput(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: true,
            Clauses: clauses);

        a.CombineMode.Should().Be(AssignmentCombineMode.Union);
        a.AllowAssignorOverride.Should().BeTrue();
        a.Clauses.Should().HaveCount(2);
        a.Clauses[0].Mode.Should().Be(AssignmentClauseMode.ByPermission);
        a.Clauses[1].RefsJson.Should().BeNull();
    }

    // C3: AssignmentClauseInput shape — refs-less modes (Initiator) carry RefsJson=null; ref-bearing modes (ByPermission) carry JSON.
    [Fact]
    public void C3_AssignmentClauseInput_AllowsNullRefsJsonForRefLessModes()
    {
        var refless = new AssignmentClauseInput(AssignmentClauseMode.Initiator, null);
        var refful = new AssignmentClauseInput(
            AssignmentClauseMode.ByPermission,
            """["Workflow.Task.Manage"]""");

        refless.RefsJson.Should().BeNull();
        refful.RefsJson.Should().Be("""["Workflow.Task.Manage"]""");
    }

    // C4: NewTransitionSpec mirror — handler → repo transport carries the same shape via AssignmentSpec/AssignmentClauseSpec.
    [Fact]
    public void C4_NewTransitionSpec_AssignmentSpec_MirrorsInputShape()
    {
        var spec = new NewTransitionSpec(
            FromStateName: "Draft",
            ToStateName: "InReview",
            ActionName: "Submit",
            ActionLabelVi: "Nộp",
            ActionLabelEn: "Submit",
            ConditionsJson: null,
            SortOrder: 1,
            IsRecall: false)
        {
            Assignment = new AssignmentSpec(
                CombineMode: AssignmentCombineMode.Union,
                AllowAssignorOverride: false,
                Clauses: new[]
                {
                    new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["Workflow.Task.Manage"]""")
                })
        };

        spec.Assignment.Should().NotBeNull();
        spec.Assignment!.CombineMode.Should().Be(AssignmentCombineMode.Union);
        spec.Assignment.Clauses.Should().ContainSingle();
    }

    // C5: WorkflowTransitionDto.Assignment is OPTIONAL — null when no assignment row exists; populated when present.
    [Fact]
    public void C5_WorkflowTransitionDto_AssignmentField_IsOptional()
    {
        var noAsg = new WorkflowTransitionDto(
            Id: 21, FromStateId: 40, ToStateId: 38,
            ActionName: "Submit", ActionLabelVi: "Nộp", ActionLabelEn: "Submit",
            ConditionsJson: null, SortOrder: 1, IsRecall: false);

        noAsg.Assignment.Should().BeNull();

        var withAsg = new WorkflowTransitionDto(
            Id: 21, FromStateId: 40, ToStateId: 38,
            ActionName: "Submit", ActionLabelVi: "Nộp", ActionLabelEn: "Submit",
            ConditionsJson: null, SortOrder: 1, IsRecall: false)
        {
            Assignment = new WorkflowTransitionAssignmentDto(
                CombineMode: AssignmentCombineMode.Union,
                AllowAssignorOverride: false,
                Clauses: new[]
                {
                    new WorkflowTransitionAssignmentClauseDto(AssignmentClauseMode.Initiator, null)
                })
        };

        withAsg.Assignment.Should().NotBeNull();
        withAsg.Assignment!.Clauses.Should().ContainSingle();
    }

    // C6: JSON binding sanity per host JsonStringEnumConverter — string-enum form deserializes correctly.
    [Fact]
    public void C6_JsonBinding_AcceptsStringEnumForm()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        const string json = """
        {
          "combineMode": "Union",
          "allowAssignorOverride": false,
          "clauses": [
            { "mode": "ByPermission", "refsJson": "[\"Workflow.Task.Manage\"]" }
          ]
        }
        """;

        var asg = JsonSerializer.Deserialize<AssignmentInput>(json, options);

        asg.Should().NotBeNull();
        asg!.CombineMode.Should().Be(AssignmentCombineMode.Union);
        asg.AllowAssignorOverride.Should().BeFalse();
        asg.Clauses.Should().ContainSingle();
        asg.Clauses[0].Mode.Should().Be(AssignmentClauseMode.ByPermission);
        asg.Clauses[0].RefsJson.Should().Be("""["Workflow.Task.Manage"]""");
    }
}
