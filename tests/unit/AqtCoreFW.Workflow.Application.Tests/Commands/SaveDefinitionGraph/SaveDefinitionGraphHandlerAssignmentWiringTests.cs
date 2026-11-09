using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentAssertions;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands.SaveDefinitionGraph;

/// <summary>
/// Phase 05 handler wiring tests (plan 260523-1241) — verify the handler propagates
/// <see cref="GraphTransitionInput.Assignment"/> through the validator → repo pipeline.
///
/// Uses the REAL <see cref="WorkflowGraphValidator"/> (pure-fn, zero deps) per phase doc — less
/// mock surface + actually exercises R2/R3/R4 + canonical writeback. Repo is mocked.
///
/// Facts:
///   H-A1: Happy path — valid Initiator assignment → 200 OK, repo called with spec.Assignment != null.
///   H-A2: Missing assignment on non-recall non-final transition → 422 GOV_WFL_019 (R2), repo NOT called.
///   H-A3: NotSupported clause mode (PreviousHandler) → 422 (R4), repo NOT called.
///   H-A4: ByPermission lowercase "workflow.task.process" → repo receives canonical "WORKFLOW.TASK.PROCESS".
///   H-A5: Recall transition with Assignment=null → 200 OK (R2 exempts recall).
///
/// S5 META-L (c): precondition asserts (validator called / repo call count) BEFORE behavior asserts.
/// </summary>
public sealed class SaveDefinitionGraphHandlerAssignmentWiringTests
{
    private readonly IWorkflowDefinitionRepository _repo = Substitute.For<IWorkflowDefinitionRepository>();
    // RT2-15 Phase 02: validator requires IIdentityModuleClient. Stub returns empty resolved set
    // (non-ByGroup clauses never trigger the Q2 hop; existing H-A1..H-A5 tests unaffected).
    // RT2-15 Phase 04: ByOrgUnit validation is structural-only; no IOrgUnitModuleClient in validator.
    private readonly IWorkflowGraphValidator _validator = new WorkflowGraphValidator(
        MakeNoopIdentityClient());
    private readonly SaveDefinitionGraphCommandHandler _handler;

    private static IIdentityModuleClient MakeNoopIdentityClient()
    {
        var client = Substitute.For<IIdentityModuleClient>();
        client.ResolveActiveUserGroupsByCodesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()));
        return client;
    }

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid DefinitionId = Guid.NewGuid();

    public SaveDefinitionGraphHandlerAssignmentWiringTests()
    {
        _handler = new SaveDefinitionGraphCommandHandler(_repo, _validator);
    }

    private static WorkflowDefinition MakeDefinition()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(DefinitionId);
        return WorkflowDefinition.Create(gen, "Test Def", "desc", TenantId, ActorId, "test-key", "Case");
    }

    private static GraphStateInput Draft() =>
        new("Draft", "Nháp", "Draft", IsInitial: true, IsFinal: false, "#FFF", 1);

    private static GraphStateInput Approved() =>
        new("Approved", "Đã duyệt", "Approved", IsInitial: false, IsFinal: true, "#0F0", 2);

    private static GraphTransitionInput SubmitWith(AssignmentInput? assignment) =>
        new("Draft", "Approved", "Submit", "Gửi", "Submit",
            ConditionsJson: null, SortOrder: 1, IsRecall: false)
        {
            Assignment = assignment,
        };

    private static AssignmentInput Initiator() =>
        new(AssignmentCombineMode.Union, AllowAssignorOverride: false,
            Clauses: new[] { new AssignmentClauseInput(AssignmentClauseMode.Initiator, RefsJson: null) });

    private void ArrangeRepoLoad(WorkflowDefinition def)
    {
        _repo.GetWithStatesAndTransitionsAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(def));
        _repo.ReplaceGraphAsync(
                Arg.Any<WorkflowDefinition>(),
                Arg.Any<List<WorkflowState>>(),
                Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
                Arg.Any<IReadOnlyList<NewBranchSpec>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    // ── H-A1: Happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task H_A1_HappyPath_ValidInitiatorAssignment_PropagatesSpecToRepo()
    {
        ArrangeRepoLoad(MakeDefinition());

        var command = new SaveDefinitionGraphCommand(
            DefinitionId, TenantId, ActorId,
            States: new[] { Draft(), Approved() },
            Transitions: new[] { SubmitWith(Initiator()) });

        var result = await _handler.Handle(command, CancellationToken.None);

        await _repo.Received(1).ReplaceGraphAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<List<WorkflowState>>(),
            Arg.Is<IReadOnlyList<NewTransitionSpec>>(s =>
                s.Count == 1 &&
                s[0].Assignment != null &&
                s[0].Assignment!.CombineMode == AssignmentCombineMode.Union &&
                s[0].Assignment!.AllowAssignorOverride == false &&
                s[0].Assignment!.Clauses.Count == 1 &&
                s[0].Assignment!.Clauses[0].Mode == AssignmentClauseMode.Initiator),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        result.IsSuccess.Should().BeTrue("real validator passes on valid Initiator clause");
    }

    // ── H-A2: R2 missing assignment ──────────────────────────────────────────

    [Fact]
    public async Task H_A2_MissingAssignmentOnNonRecallTransition_Returns422_RepoNotCalled()
    {
        ArrangeRepoLoad(MakeDefinition());

        var command = new SaveDefinitionGraphCommand(
            DefinitionId, TenantId, ActorId,
            States: new[] { Draft(), Approved() },
            Transitions: new[] { SubmitWith(null) });

        var result = await _handler.Handle(command, CancellationToken.None);

        await _repo.Received(0).ReplaceGraphAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<List<WorkflowState>>(),
            Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Metadata.ContainsKey("WorkflowErrorCode") &&
            e.Metadata["WorkflowErrorCode"] as string == WorkflowErrorCodes.GraphValidationFailed);
    }

    // ── H-A3: R4 NotSupported clause (PreviousHandler) — RETIRED 2026-05-24 ──
    //
    // RT2-15 Phase 01 shipped PreviousHandler producer; the mode is no longer in the
    // _notSupportedModes set, so this assertion (422 + repo NOT called) no longer holds.
    // Positive handler-wiring coverage for mode 2 lands in Phase 05 cross-mode sweep +
    // the integration suite RT2_15_PreviousHandlerProducerTests.

    // ── H-A4: R3 canonical writeback to repo ─────────────────────────────────

    [Fact]
    public async Task H_A4_ByPermissionLowercase_RepoReceivesCanonicalUppercase()
    {
        ArrangeRepoLoad(MakeDefinition());

        var assignment = new AssignmentInput(
            AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[]
            {
                new AssignmentClauseInput(
                    AssignmentClauseMode.ByPermission,
                    RefsJson: """["workflow.task.process"]"""),
            });

        var command = new SaveDefinitionGraphCommand(
            DefinitionId, TenantId, ActorId,
            States: new[] { Draft(), Approved() },
            Transitions: new[] { SubmitWith(assignment) });

        var result = await _handler.Handle(command, CancellationToken.None);

        await _repo.Received(1).ReplaceGraphAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<List<WorkflowState>>(),
            Arg.Is<IReadOnlyList<NewTransitionSpec>>(s =>
                s.Count == 1 &&
                s[0].Assignment != null &&
                s[0].Assignment!.Clauses.Count == 1 &&
                s[0].Assignment!.Clauses[0].RefsJson == """["WORKFLOW.TASK.PROCESS"]"""),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        result.IsSuccess.Should().BeTrue("real validator canonicalizes spec.RefsJson IN PLACE before repo call");
    }

    // ── H-A6: M1 fold from Phase 05 review — null Clauses array null-guard ──
    //
    // Phase 05 review M1: FE could send {"assignment":{"combineMode":0,"clauses":null}}
    // (typo / missing ??[] / older client). System.Text.Json deserialises that to
    // AssignmentInput with Clauses=null despite non-nullable signature (CLR runtime
    // permits null reference for IReadOnlyList<T>). Without guard, handler NREs in
    // MapAssignment.foreach. Test: handler returns a Result (not exception); fold
    // converts null → empty list, validator then sees no clauses (R3/R4 no-op),
    // R2 is satisfied by Assignment != null (M2 empty-clause semantic still
    // carry-fwd as separate product decision).

    [Fact]
    public async Task H_A6_AssignmentWithNullClausesArray_DoesNotThrowNRE()
    {
        ArrangeRepoLoad(MakeDefinition());

        // Force null Clauses bypassing non-nullable signature (mirror System.Text.Json behaviour).
        var assignment = new AssignmentInput(
            AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: null!);

        var command = new SaveDefinitionGraphCommand(
            DefinitionId, TenantId, ActorId,
            States: new[] { Draft(), Approved() },
            Transitions: new[] { SubmitWith(assignment) });

        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().NotThrowAsync<NullReferenceException>(
            "MapAssignment must null-guard Clauses (M1 fold from Phase 05 review)");
    }

    // ── H-A7: T3-WF-RECALL-ASSIGNMENT-Q Option A — silent-strip semantic ────
    //
    // Product Q ratified 2026-05-23: when a transition carries IsRecall=true AND a
    // non-null Assignment payload, the handler SILENTLY STRIPS the Assignment (sets
    // spec.Assignment = null) before passing to the repo. Rationale:
    //   - R2-R4 already exempt recall in the validator (assignment is dead data for recall)
    //   - FE Collapse stays visible (no breaking UX), but BE is server-authoritative
    //   - Avoids dead-data persistence in WorkflowTransitionAssignments table for recall rows
    //
    // Test verifies the strip happens at the spec-build site (handler -> repo boundary),
    // independent of validator behaviour.

    [Fact]
    public async Task H_A7_RecallTransitionWithNonNullAssignment_SilentlyStripsToNullBeforeRepo()
    {
        ArrangeRepoLoad(MakeDefinition());

        // R1: Draft needs ≥1 non-recall outgoing → keep Submit happy.
        var recallWithAssignment = new GraphTransitionInput(
            "Approved", "Draft", "Recall", "Thu hồi", "Recall",
            ConditionsJson: null, SortOrder: 2, IsRecall: true)
        {
            Assignment = Initiator(), // Non-null input → should be stripped server-side
        };

        var command = new SaveDefinitionGraphCommand(
            DefinitionId, TenantId, ActorId,
            States: new[] { Draft(), Approved() },
            Transitions: new[] { SubmitWith(Initiator()), recallWithAssignment });

        var result = await _handler.Handle(command, CancellationToken.None);

        // Repo MUST receive Recall spec with Assignment == null despite non-null input.
        await _repo.Received(1).ReplaceGraphAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<List<WorkflowState>>(),
            Arg.Is<IReadOnlyList<NewTransitionSpec>>(s =>
                s.Count == 2 &&
                s.Any(x => x.ActionName == "Submit" && x.Assignment != null) &&
                s.Any(x => x.ActionName == "Recall" && x.IsRecall == true && x.Assignment == null)),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        result.IsSuccess.Should().BeTrue("silent-strip is not a validation failure");
    }

    // ── H-A5: recall null-allowed ────────────────────────────────────────────

    [Fact]
    public async Task H_A5_RecallTransitionWithNullAssignment_Returns200_R2Exempt()
    {
        ArrangeRepoLoad(MakeDefinition());

        // R1: Draft needs ≥1 non-recall outgoing → keep Submit happy; add a Recall transition with null Assignment.
        var recall = new GraphTransitionInput(
            "Approved", "Draft", "Recall", "Thu hồi", "Recall",
            ConditionsJson: null, SortOrder: 2, IsRecall: true)
        {
            Assignment = null,
        };

        var command = new SaveDefinitionGraphCommand(
            DefinitionId, TenantId, ActorId,
            States: new[] { Draft(), Approved() },
            Transitions: new[] { SubmitWith(Initiator()), recall });

        var result = await _handler.Handle(command, CancellationToken.None);

        await _repo.Received(1).ReplaceGraphAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<List<WorkflowState>>(),
            Arg.Is<IReadOnlyList<NewTransitionSpec>>(s =>
                s.Count == 2 &&
                s.Any(x => x.ActionName == "Submit" && x.Assignment != null) &&
                s.Any(x => x.ActionName == "Recall" && x.IsRecall == true && x.Assignment == null)),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        result.IsSuccess.Should().BeTrue("recall transitions are exempt from R2 (no human-actionable assignment needed)");
    }
}
