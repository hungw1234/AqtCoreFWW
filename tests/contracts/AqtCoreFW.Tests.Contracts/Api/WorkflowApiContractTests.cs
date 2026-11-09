using System.Text.Json;
using AqtCoreFW.Workflow.Application.DTOs;

namespace AqtCoreFW.Tests.Contracts.Api;

/// <summary>
/// Contract tests for Workflow module DTOs — TC-CON-API-012.
/// Phase-4b: WorkflowStateDto.Id is long (T1 bigint PK);
///           WorkflowTransitionDto.Id/FromStateId/ToStateId are long (T1 bigint PKs).
/// </summary>
public sealed class WorkflowApiContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void WorkflowDefinitionDto_JsonShape_StatesAndTransitionsPresent()
    {
        var state = new WorkflowStateDto(
            1L, "Draft", "Nháp", "Draft",
            true, false, "#gray", 1, null, null);

        var transition = new WorkflowTransitionDto(
            1L, 1L, 2L,
            "Submit", "Gửi", "Submit", null, 1, false);

        var dto = new WorkflowDefinitionDto(
            Guid.NewGuid(), "Case Workflow", "Standard case flow",
            true, Guid.NewGuid(), DateTimeOffset.UtcNow,
            "case-workflow", 1, true, "Case",
            [state], [transition], []);

        var json = JsonSerializer.Serialize(dto);
        var root = JsonDocument.Parse(json).RootElement;

        root.TryGetProperty("Id", out _).Should().BeTrue();
        root.TryGetProperty("Name", out _).Should().BeTrue();
        root.TryGetProperty("States", out var states).Should().BeTrue();
        states.ValueKind.Should().Be(JsonValueKind.Array);
        states.GetArrayLength().Should().Be(1);
        root.TryGetProperty("Transitions", out var trans).Should().BeTrue();
        trans.ValueKind.Should().Be(JsonValueKind.Array);
        trans.GetArrayLength().Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void WorkflowDefinitionDto_RoundTrip_Lossless()
    {
        var dto = new WorkflowDefinitionDto(
            Guid.NewGuid(), "Approval Flow", "Multi-step approval",
            true, Guid.NewGuid(), DateTimeOffset.UtcNow,
            "approval-flow", 1, true, "Case",
            [], [], []);

        var json = JsonSerializer.Serialize(dto);
        var d = JsonSerializer.Deserialize<WorkflowDefinitionDto>(json);

        d.Should().NotBeNull();
        d!.Name.Should().Be(dto.Name);
        d.IsActive.Should().Be(dto.IsActive);
        d.States.Should().BeEmpty();
        d.Transitions.Should().BeEmpty();
    }
}
