using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Validation;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Validation;

/// <summary>
/// Unit tests for the parallel-branch rules RB1-RB4 on <see cref="WorkflowGraphValidator.ValidateForSaveAsync"/>.
/// RB rules are pure and independent of R1-R4, so the graphs here omit transitions and the assertions
/// filter to RB-prefixed violations (R1 may fire on transition-less non-final states — irrelevant here).
/// </summary>
public sealed class WorkflowGraphValidatorBranchRulesTests
{
    private static IIdentityModuleClient NoopIdentityClient()
    {
        var client = Substitute.For<IIdentityModuleClient>();
        client.ResolveActiveUserGroupsByCodesAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()));
        return client;
    }

    private readonly IWorkflowGraphValidator _validator = new WorkflowGraphValidator(NoopIdentityClient());

    private const long DefId = 1L;

    // Fork(initial) → TaskA/TaskB (children) → Done(final, = continuation/join).
    private static IReadOnlyList<WorkflowState> Graph() =>
    [
        WorkflowState.Create(DefId, "Fork", "Fork", "Fork", isInitial: true, isFinal: false, "#FFF", 1),
        WorkflowState.Create(DefId, "TaskA", "TaskA", "TaskA", isInitial: false, isFinal: false, "#FFF", 2),
        WorkflowState.Create(DefId, "TaskB", "TaskB", "TaskB", isInitial: false, isFinal: false, "#FFF", 3),
        WorkflowState.Create(DefId, "Done", "Done", "Done", isInitial: false, isFinal: true, "#FFF", 4),
    ];

    private static NewBranchSpec ValidBranch(
        ParallelBranchType type = ParallelBranchType.And,
        int? requiredCount = null,
        string branching = "Fork",
        string continuation = "Done") =>
        new(branching, continuation, type, requiredCount, AllowCancelOnAnyReject: false, TimeoutMinutes: 0,
            PositionX: null, PositionY: null,
            Children:
            [
                new NewBranchChildSpec("TaskA", "task-a", "Việc A", null, null, 1,
                    [new NewBranchResolutionSpec(BranchResolutionType.Approved, "Done")]),
                new NewBranchChildSpec("TaskB", "task-b", "Việc B", null, null, 2, []),
            ]);

    private Task<WorkflowGraphValidationReport> Validate(params NewBranchSpec[] branches) =>
        _validator.ValidateForSaveAsync(Graph(), [], branches, Guid.Empty, CancellationToken.None);

    [Fact]
    public async Task RB1_UnknownResolutionResultingState_Violation()
    {
        var branch = ValidBranch() with
        {
            Children =
            [
                new NewBranchChildSpec("TaskA", "task-a", "Việc A", null, null, 1,
                    [new NewBranchResolutionSpec(BranchResolutionType.Approved, "Ghost")]),
            ],
        };

        var report = await Validate(branch);

        report.Violations.Should().Contain(v => v.Rule == "RB1" && v.Detail.Contains("Ghost"));
    }

    [Fact]
    public async Task RB1_UnknownChildTargetState_Violation()
    {
        var branch = ValidBranch() with
        {
            Children = [new NewBranchChildSpec("Nope", "x", "X", null, null, 1, [])],
        };

        var report = await Validate(branch);

        report.Violations.Should().Contain(v => v.Rule == "RB1" && v.Detail.Contains("Nope"));
    }

    [Fact]
    public async Task RB1_UnknownContinuationState_Violation()
    {
        var report = await Validate(ValidBranch(continuation: "Nowhere"));

        report.Violations.Should().Contain(v => v.Rule == "RB1" && v.Detail.Contains("Nowhere"));
    }

    [Fact]
    public async Task RB2_NOfM_WithoutRequiredCount_Violation()
    {
        var report = await Validate(ValidBranch(ParallelBranchType.NOfM, requiredCount: null));

        report.Violations.Should().Contain(v => v.Rule == "RB2");
    }

    [Fact]
    public async Task RB2_NOfM_RequiredCountExceedsChildCount_Violation()
    {
        // 2 children, RequiredCount 3 → impossible.
        var report = await Validate(ValidBranch(ParallelBranchType.NOfM, requiredCount: 3));

        report.Violations.Should().Contain(v => v.Rule == "RB2" && v.Detail.Contains("greater than"));
    }

    [Fact]
    public async Task RB3_TwoBranchesOnSameBranchingState_Violation()
    {
        var report = await Validate(ValidBranch(), ValidBranch());

        report.Violations.Should().Contain(v => v.Rule == "RB3");
    }

    [Fact]
    public async Task RB4_BranchingEqualsContinuation_Violation()
    {
        var report = await Validate(ValidBranch(continuation: "Fork"));

        report.Violations.Should().Contain(v => v.Rule == "RB4");
    }

    [Fact]
    public async Task RB4_NoChildren_Violation()
    {
        var branch = ValidBranch() with { Children = [] };

        var report = await Validate(branch);

        report.Violations.Should().Contain(v => v.Rule == "RB4");
    }

    [Fact]
    public async Task ValidBranch_NoBranchRuleViolation()
    {
        var report = await Validate(ValidBranch());

        report.Violations.Should().NotContain(v => v.Rule.StartsWith("RB"),
            "a structurally valid branch must not raise any RB violation");
    }

    [Fact]
    public async Task ValidNOfMBranch_NoBranchRuleViolation()
    {
        var report = await Validate(ValidBranch(ParallelBranchType.NOfM, requiredCount: 2));

        report.Violations.Should().NotContain(v => v.Rule.StartsWith("RB"));
    }
}
