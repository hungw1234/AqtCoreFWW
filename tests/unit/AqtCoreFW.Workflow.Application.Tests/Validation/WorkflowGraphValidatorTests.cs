using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Errors;
using AqtCoreFW.Workflow.Domain.Validation;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Validation;

/// <summary>
/// TDD unit tests for WorkflowGraphValidator (Phase 5c).
/// V1-V10: pure-function rules R1-R4.
/// V11: dual-metadata regression for GraphValidationFailed factory (4-B-A INVARIANT).
/// RT2-15 Phase 02: validator now injects IIdentityModuleClient (scoped). Tests stub the
/// client to return empty resolved sets (non-ByGroup tests do not trigger the Q2 hop).
/// </summary>
public sealed class WorkflowGraphValidatorTests
{
    // Stub: returns empty set (no ByGroup codes resolve) — non-ByGroup tests unaffected.
    private static IIdentityModuleClient MakeNoopIdentityClient()
    {
        var client = Substitute.For<IIdentityModuleClient>();
        client.ResolveActiveUserGroupsByCodesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()));
        return client;
    }

    /// <summary>Stub factory: returns controlled active group codes for Q2 validation.</summary>
    private static IIdentityModuleClient MakeIdentityClientWithGroups(params string[] activeGroupCodes)
    {
        var activeSet = new HashSet<string>(activeGroupCodes, StringComparer.OrdinalIgnoreCase);
        var client = Substitute.For<IIdentityModuleClient>();
        client.ResolveActiveUserGroupsByCodesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(args =>
              {
                  var input = (IReadOnlyCollection<string>)args[0];
                  var result = new HashSet<string>(
                      input.Where(code => activeSet.Contains(code)),
                      StringComparer.OrdinalIgnoreCase);
                  return Task.FromResult<IReadOnlySet<string>>(result);
              });
        return client;
    }

    private readonly IWorkflowGraphValidator _validator = new WorkflowGraphValidator(MakeNoopIdentityClient());

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Build a WorkflowState with an explicit Id (non-zero for persisted / 0 for new).</summary>
    private static WorkflowState MakeState(long id, long definitionId, bool isInitial = false, bool isFinal = false)
    {
        var state = WorkflowState.Create(definitionId, $"State_{id}", "Vi", "En", isInitial, isFinal, "#FFF", (int)id);
        SetPrivateId(state, id);
        return state;
    }

    private static WorkflowTransition MakeTransition(long id, long defId, long fromId, long toId, bool isRecall = false)
    {
        var t = WorkflowTransition.Create(defId, fromId, toId, $"Action_{id}", "Vi", "En", null, (int)id, isRecall);
        SetPrivateId(t, id);
        return t;
    }

    // WorkflowState / WorkflowTransition use private set; use reflection to set Id for tests.
    private static void SetPrivateId<T>(T entity, long id) where T : AqtCoreFW.SharedKernel.Domain.Entity
    {
        var prop = typeof(AqtCoreFW.SharedKernel.Domain.Entity).GetProperty("Id",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        prop!.SetValue(entity, id);
    }

    private static WorkflowTransitionAssignment MakeAssignment(long transitionId,
        AssignmentCombineMode combine = AssignmentCombineMode.Union)
    {
        var a = WorkflowTransitionAssignment.Create(transitionId, combine);
        return a;
    }

    private static OutgoingAssignment Outgoing(WorkflowTransition t, WorkflowTransitionAssignment? a)
        => new(t, a);

    // ── V1: R1 reachability — dead-end non-final state → error ───────────────

    [Fact]
    public async Task ValidateForActivate_DeadEndNonFinalState_ReturnsR1Error()
    {
        // Arrange: State A (initial) → [submit] → State B (non-final, no outgoing human transition)
        // State B has only AutoTimeout transition (IsRecall=false, but AutoTransitionOnTimeoutId set).
        // In ValidateForActivate: only !IsRecall transitions from non-final states count for R1.
        // A dead-end non-final state is one where ALL outgoing transitions are IsRecall=true
        // OR there are no outgoing transitions at all (for non-final states).

        var defId = 1L;
        // States
        var stateA = MakeState(10, defId, isInitial: true);
        var stateB = MakeState(20, defId); // non-final, no outgoing human transitions

        // Transitions: stateA → stateB (human)
        var t1 = MakeTransition(100, defId, fromId: 10, toId: 20);

        // Assignments for the outgoing of stateA (non-final): t1 has assignment
        var assignT1 = MakeAssignment(100);
        assignT1.AddClause(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""");

        var assignmentByTransitionId = new Dictionary<long, OutgoingAssignment?>
        {
            // stateA's outgoing: t1 (human, leads to stateB)
            [100] = Outgoing(t1, assignT1),
            // stateB has NO outgoing human transitions → empty (dead end)
        };

        // Act
        var report = await _validator.ValidateForActivateAsync(
            states: [stateA, stateB],
            transitions: [t1],
            assignmentByTransitionId: assignmentByTransitionId,
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        // Assert
        report.IsValid.Should().BeFalse("stateB is a dead-end non-final state");
        report.Violations.Should().Contain(v =>
            v.Rule == "R1" && v.StateName == stateB.Name,
            "R1 violation must name the dead-end state");
    }

    // ── V2: R1 reachability — fully connected → valid ────────────────────────

    [Fact]
    public async Task ValidateForActivate_FullyConnectedGraph_R1Passes()
    {
        var defId = 1L;
        var stateA = MakeState(10, defId, isInitial: true);
        var stateB = MakeState(20, defId);
        var stateC = MakeState(30, defId, isFinal: true);

        var t1 = MakeTransition(100, defId, 10, 20); // A → B (human)
        var t2 = MakeTransition(200, defId, 20, 30); // B → C-final (human)

        var assignT1 = MakeAssignment(100);
        assignT1.AddClause(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""");
        var assignT2 = MakeAssignment(200);
        assignT2.AddClause(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""");

        var assignmentByTransitionId = new Dictionary<long, OutgoingAssignment?>
        {
            [100] = Outgoing(t1, assignT1), // stateA's outgoing
            [200] = Outgoing(t2, assignT2), // stateB's outgoing
        };

        var report = await _validator.ValidateForActivateAsync(
            states: [stateA, stateB, stateC],
            transitions: [t1, t2],
            assignmentByTransitionId: assignmentByTransitionId,
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        report.IsValid.Should().BeTrue("fully connected graph with valid assignments should pass");
        report.Violations.Should().BeEmpty();
    }

    // ── V3: R1 save path — IsRecall transitions excluded from reachability ───

    [Fact]
    public async Task ValidateForSave_OnlyRecallOutgoing_NonFinalState_R1Fails()
    {
        // State B has only a recall transition outgoing — human-actionable count = 0 → R1 fail.
        // Both states use distinct names to avoid duplicate-key collision in the validator.
        var defId = 1L;
        var stateA = WorkflowState.Create(defId, "StateA", "Vi", "En", isInitial: true, isFinal: false, "#FFF", 1);
        var stateB = WorkflowState.Create(defId, "StateB", "Vi", "En", isInitial: false, isFinal: false, "#FFF", 2);

        // Only a recall transition from A to B — no human-actionable outgoing for either state.
        var specs = new List<NewTransitionSpec>
        {
            new("StateA", "StateB", "Recall_A_to_B", "Vi", "En", null, 1, IsRecall: true),
        };

        var report = await _validator.ValidateForSaveAsync(
            states: [stateA, stateB],
            transitionSpecs: specs,
            branchSpecs: [],
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        // stateA has only a recall outgoing → dead-end. stateB has no outgoing → dead-end.
        report.IsValid.Should().BeFalse("non-final states with only recall outgoing are dead-ends");
        report.Violations.Should().Contain(v => v.Rule == "R1");
    }

    // ── V4: R2 — non-final state with no assignment → error ─────────────────

    [Fact]
    public async Task ValidateForActivate_NonFinalStateWithNullAssignment_ReturnsR2Error()
    {
        var defId = 1L;
        var stateA = MakeState(10, defId, isInitial: true);
        var stateB = MakeState(20, defId, isFinal: true);

        var t1 = MakeTransition(100, defId, 10, 20);

        // stateA → stateB, but t1 has NO assignment (null)
        var assignmentByTransitionId = new Dictionary<long, OutgoingAssignment?>
        {
            [100] = Outgoing(t1, null), // null assignment → R2 violation
        };

        var report = await _validator.ValidateForActivateAsync(
            states: [stateA, stateB],
            transitions: [t1],
            assignmentByTransitionId: assignmentByTransitionId,
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        report.IsValid.Should().BeFalse();
        report.Violations.Should().Contain(v =>
            v.Rule == "R2" && v.TransitionActionName == t1.ActionName,
            "R2 violation must name the transition missing assignment");
    }

    // ── V5: R3 ByPermission — mixed case / trimmed code → accepted + canonical writeback ──

    [Fact]
    public async Task ValidateForActivate_ByPermissionMixedCase_IsAcceptedAndCanonicalizes()
    {
        // "  workflow.task.process  " (lower-case + whitespace) should:
        // (a) NOT produce an R3 violation (OrdinalIgnoreCase + Trim lookup).
        // (b) Rewrite clause.RefsJson to canonical form ["WORKFLOW.TASK.PROCESS"].
        // PermCodes.Workflow.Task.Process = "WORKFLOW.TASK.PROCESS" (uppercase — per PermCodes.cs convention).
        var defId = 1L;
        var stateA = MakeState(10, defId, isInitial: true);
        var stateB = MakeState(20, defId, isFinal: true);
        var t1 = MakeTransition(100, defId, 10, 20);

        var assign = MakeAssignment(100);
        var clause = assign.AddClause(AssignmentClauseMode.ByPermission, """["  workflow.task.process  "]""");

        var assignmentByTransitionId = new Dictionary<long, OutgoingAssignment?>
        {
            [100] = Outgoing(t1, assign),
        };

        // Act
        var report = await _validator.ValidateForActivateAsync(
            states: [stateA, stateB],
            transitions: [t1],
            assignmentByTransitionId: assignmentByTransitionId,
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        // Assert (a): no R3 violation.
        report.Violations.Should().NotContain(v => v.Rule == "R3",
            "OrdinalIgnoreCase + Trim lookup should accept lower-case / padded code");

        // Assert (b): clause.RefsJson canonicalized to ["WORKFLOW.TASK.PROCESS"].
        clause.RefsJson.Should().Be("""["WORKFLOW.TASK.PROCESS"]""",
            "validator must rewrite RefsJson to canonical (trimmed + upper-cased) JSON array");
    }

    // ── V5b: R3 ByPermission — already canonical → no mutation ──────────────

    [Fact]
    public async Task ValidateForActivate_ByPermissionAlreadyCanonical_NoMutation()
    {
        // Already canonical form "WORKFLOW.TASK.PROCESS" should:
        // (a) NOT produce an R3 violation.
        // (b) NOT mutate clause.RefsJson (canonical JSON matches stored JSON — SetCanonicalRefsJson not called).
        var defId = 1L;
        var stateA = MakeState(10, defId, isInitial: true);
        var stateB = MakeState(20, defId, isFinal: true);
        var t1 = MakeTransition(100, defId, 10, 20);

        const string canonicalJson = """["WORKFLOW.TASK.PROCESS"]""";

        var assign = MakeAssignment(100);
        var clause = assign.AddClause(AssignmentClauseMode.ByPermission, canonicalJson);

        var assignmentByTransitionId = new Dictionary<long, OutgoingAssignment?>
        {
            [100] = Outgoing(t1, assign),
        };

        // Act
        var report = await _validator.ValidateForActivateAsync(
            states: [stateA, stateB],
            transitions: [t1],
            assignmentByTransitionId: assignmentByTransitionId,
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        // Assert (a): no R3 violation.
        report.Violations.Should().NotContain(v => v.Rule == "R3",
            "canonical code is valid and should not trigger R3");

        // Assert (b): RefsJson is byte-equal to the original — no unnecessary rewrite.
        clause.RefsJson.Should().Be(canonicalJson,
            "already-canonical RefsJson should not be mutated (SetCanonicalRefsJson not called when JSON matches)");
    }

    // ── V6: R3 ByPermission — unknown code → error ───────────────────────────

    [Fact]
    public async Task ValidateForActivate_ByPermissionUnknownCode_ReturnsR3Error()
    {
        var defId = 1L;
        var stateA = MakeState(10, defId, isInitial: true);
        var stateB = MakeState(20, defId, isFinal: true);
        var t1 = MakeTransition(100, defId, 10, 20);

        var assign = MakeAssignment(100);
        assign.AddClause(AssignmentClauseMode.ByPermission, """["COMPLETELY.UNKNOWN.CODE"]""");

        var assignmentByTransitionId = new Dictionary<long, OutgoingAssignment?>
        {
            [100] = Outgoing(t1, assign),
        };

        var report = await _validator.ValidateForActivateAsync(
            states: [stateA, stateB],
            transitions: [t1],
            assignmentByTransitionId: assignmentByTransitionId,
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        report.IsValid.Should().BeFalse();
        report.Violations.Should().Contain(v =>
            v.Rule == "R3" && v.Detail.Contains("COMPLETELY.UNKNOWN.CODE"),
            "R3 error must identify the invalid permission code");
    }

    // ── V7: R3 valid ByPermission code → no error ────────────────────────────

    [Fact]
    public async Task ValidateForActivate_ValidByPermissionCode_NoR3Error()
    {
        var defId = 1L;
        var stateA = MakeState(10, defId, isInitial: true);
        var stateB = MakeState(20, defId, isFinal: true);
        var t1 = MakeTransition(100, defId, 10, 20);

        var assign = MakeAssignment(100);
        assign.AddClause(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""");

        var assignmentByTransitionId = new Dictionary<long, OutgoingAssignment?>
        {
            [100] = Outgoing(t1, assign),
        };

        var report = await _validator.ValidateForActivateAsync(
            states: [stateA, stateB],
            transitions: [t1],
            assignmentByTransitionId: assignmentByTransitionId,
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        report.Violations.Should().NotContain(v => v.Rule == "R3",
            "WORKFLOW.TASK.PROCESS is a valid PermCode");
    }

    // ── V8: R4 NotSupported clause mode → error ──────────────────────────────
    //
    // RT2-15 Phase 01 (2026-05-24): PreviousHandler shipped — positive Save/Activate
    // assertions for mode 2 land in Phase 05 cross-mode validator sweep.

    // ── V9: R4 Intersect combine mode → error ────────────────────────────────

    [Fact]
    public async Task ValidateForActivate_IntersectCombineMode_ReturnsR4Error()
    {
        var defId = 1L;
        var stateA = MakeState(10, defId, isInitial: true);
        var stateB = MakeState(20, defId, isFinal: true);
        var t1 = MakeTransition(100, defId, 10, 20);

        var assign = MakeAssignment(100, AssignmentCombineMode.Intersect);
        assign.AddClause(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""");

        var assignmentByTransitionId = new Dictionary<long, OutgoingAssignment?>
        {
            [100] = Outgoing(t1, assign),
        };

        var report = await _validator.ValidateForActivateAsync(
            states: [stateA, stateB],
            transitions: [t1],
            assignmentByTransitionId: assignmentByTransitionId,
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        report.IsValid.Should().BeFalse();
        report.Violations.Should().Contain(v =>
            v.Rule == "R4" && v.Detail.Contains("Intersect"),
            "R4 must flag Intersect as NotSupported");
    }

    // ── V10: R2+R3+R4 happy path — all valid → no violations ─────────────────

    [Fact]
    public async Task ValidateForActivate_AllRulesPass_NoViolations()
    {
        var defId = 1L;
        var stateInit = MakeState(10, defId, isInitial: true);
        var stateMiddle = MakeState(20, defId);
        var stateFinal = MakeState(30, defId, isFinal: true);

        var t1 = MakeTransition(100, defId, 10, 20); // init → middle
        var t2 = MakeTransition(200, defId, 20, 30); // middle → final

        var assignT1 = MakeAssignment(100, AssignmentCombineMode.Union);
        assignT1.AddClause(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""");
        var assignT2 = MakeAssignment(200, AssignmentCombineMode.Union);
        assignT2.AddClause(AssignmentClauseMode.Initiator, null); // Initiator is supported

        var assignmentByTransitionId = new Dictionary<long, OutgoingAssignment?>
        {
            [100] = Outgoing(t1, assignT1),
            [200] = Outgoing(t2, assignT2),
        };

        var report = await _validator.ValidateForActivateAsync(
            states: [stateInit, stateMiddle, stateFinal],
            transitions: [t1, t2],
            assignmentByTransitionId: assignmentByTransitionId,
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        report.IsValid.Should().BeTrue("all rules pass for a well-configured graph");
        report.Violations.Should().BeEmpty();
    }

    // ── V11: dual-metadata regression — GraphValidationFailed factory ─────────

    [Fact]
    public void GraphValidationFailed_Error_HasBothMetadataKeys_As_GOV_WFL_019()
    {
        // 4-B-A INVARIANT: ValidationError base ctor sets ErrorCode=GOV_SYS_002; factory MUST
        // overwrite with GOV_WFL_019. Both "ErrorCode" AND "WorkflowErrorCode" must be set.
        var violations = new List<GraphRuleViolation>
        {
            new("R1", "StateA", null, "Dead-end state")
        };

        var error = WorkflowErrors.GraphValidationFailed(violations);

        error.Metadata.Should().ContainKey("ErrorCode")
            .WhoseValue.Should().Be(WorkflowErrorCodes.GraphValidationFailed,
                "4-B-A: ErrorCode must be overwritten from GOV_SYS_002 to GOV_WFL_019");

        error.Metadata.Should().ContainKey("WorkflowErrorCode")
            .WhoseValue.Should().Be(WorkflowErrorCodes.GraphValidationFailed,
                "4-B-A: WorkflowErrorCode provenance must also be set");

        error.Metadata.Should().ContainKey("GraphErrors",
            "GraphErrors internal key must remain present for log/debug inspection");

        // ── PublicMetadata wire-projection assertions ──
        // Verifies the DEFAULT-DENY OPT-IN envelope contract: only "PublicMetadata" surfaces at
        // the HTTP boundary (ApiResponse.Fail strips all other FluentResults metadata keys).

        // Type assertion first: catches the IReadOnlyDictionary<string,object> vs <string,object?>
        // invariant-TValue trap — wrong type silently produces null metadata on the wire.
        error.Metadata.Should().ContainKey("PublicMetadata")
            .WhoseValue.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>(
                "PublicMetadata must be IReadOnlyDictionary<string,object?> (nullable value); " +
                "IReadOnlyDictionary is invariant in TValue — wrong nullability silently fails " +
                "the pattern match in ApiResponse.Fail and produces null on the wire");

        var publicMeta = (IReadOnlyDictionary<string, object?>)error.Metadata["PublicMetadata"]!;
        publicMeta.Should().ContainKey("violations",
            "violations list must be the public wire projection of GraphRuleViolations");

        var violationsList = publicMeta["violations"] as System.Collections.IEnumerable;
        violationsList.Should().NotBeNull("violations must be an enumerable list");
        violationsList!.Cast<object>().Should().HaveCount(violations.Count,
            "violations projection must contain exactly the input violations");
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // Cross-mode R4-lift matrix sweep.
    // Every clause mode lifted across the deferred-mode arc (PreviousHandler / ByRole / ByGroup /
    // ByOrgUnit) MUST NOT raise an R4 "not supported" violation on EITHER entrypoint (Save +
    // Activate). This is a contract/regression guard: if a future change re-populates
    // _notSupportedModes with any of these, the matrix breaks loudly. It asserts the R4-lift
    // ONLY — mode-specific behaviors (canonicalization, ByGroup Q2 existence, malformed JSON)
    // stay covered by their dedicated cases. ByGroup uses the noop identity client, so an
    // unresolved code surfaces as R3 (not R4); the assertion targets R4 precisely.
    // ════════════════════════════════════════════════════════════════════════════════════════

    public static TheoryData<AssignmentClauseMode, string?> LiftedClauseModes() => new()
    {
        { AssignmentClauseMode.PreviousHandler, null },          // ref-less, runtime-resolved
        { AssignmentClauseMode.ByRole, """["ROLE_REVIEWER"]""" },
        { AssignmentClauseMode.ByGroup, """["GROUP_X"]""" },
        { AssignmentClauseMode.ByOrgUnit, """["ORG_X"]""" },
    };

    [Theory]
    [MemberData(nameof(LiftedClauseModes))]
    public async Task ValidateForSave_LiftedClauseMode_NoR4NotSupportedViolation(
        AssignmentClauseMode mode, string? refsJson)
    {
        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { new AssignmentClauseSpec(mode, refsJson) });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await _validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.Violations.Should().NotContain(v => v.Rule == "R4",
            $"clause mode '{mode}' is a supported producer — R4 must not flag it at Save");
    }

    [Theory]
    [MemberData(nameof(LiftedClauseModes))]
    public async Task ValidateForActivate_LiftedClauseMode_NoR4NotSupportedViolation(
        AssignmentClauseMode mode, string? refsJson)
    {
        var defId = 1L;
        var stateA = MakeState(10, defId, isInitial: true);
        var stateB = MakeState(20, defId, isFinal: true);
        var t1 = MakeTransition(100, defId, 10, 20);

        var assign = MakeAssignment(100);
        assign.AddClause(mode, refsJson);

        var assignmentByTransitionId = new Dictionary<long, OutgoingAssignment?>
        {
            [100] = Outgoing(t1, assign),
        };

        var report = await _validator.ValidateForActivateAsync(
            states: [stateA, stateB],
            transitions: [t1],
            assignmentByTransitionId: assignmentByTransitionId,
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        report.Violations.Should().NotContain(v => v.Rule == "R4",
            $"clause mode '{mode}' is a supported producer — R4 must not flag it at Activate");
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // Phase 03 (plan 260523-1241) — Save-time R2/R3/R4 against NewTransitionSpec.Assignment.
    // Q ratifications honored: Q1 strict-at-Save / Q2 replace-all / Q6 R4 strict-at-Save.
    // 260522-E necessary-vs-sufficient: each rule has SUFFICIENT (happy) + NECESSARY (failure) fact.
    // ════════════════════════════════════════════════════════════════════════════════════════

    // ── Save-time helpers ───────────────────────────────────────────────────

    /// <summary>Build a 2-state graph (Draft initial → Approved final) for Save-time tests.</summary>
    private static (WorkflowState[] States, NewTransitionSpec submit) MakeMinimalGraph(AssignmentSpec? submitAssignment)
    {
        var defId = 1L;
        var draft = WorkflowState.Create(defId, "Draft", "Nháp", "Draft", isInitial: true, isFinal: false, "#FFF", 1);
        var approved = WorkflowState.Create(defId, "Approved", "Duyệt", "Approved", isInitial: false, isFinal: true, "#FFF", 2);

        var submit = new NewTransitionSpec(
            FromStateName: "Draft",
            ToStateName: "Approved",
            ActionName: "Submit",
            ActionLabelVi: "Nộp",
            ActionLabelEn: "Submit",
            ConditionsJson: null,
            SortOrder: 1,
            IsRecall: false)
        {
            Assignment = submitAssignment
        };

        return ([draft, approved], submit);
    }

    // ── S-R2-Missing — spec.Assignment is null → R2 violation (NECESSARY proof of R2 at Save-time) ──

    [Fact]
    public async Task ValidateForSave_SpecWithoutAssignment_OnNonFinalStateOutgoing_ReturnsR2Violation()
    {
        var (states, submit) = MakeMinimalGraph(submitAssignment: null);

        var report = await _validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeFalse("R2: human-actionable transition out of non-final state requires assignment");
        report.Violations.Should().Contain(v =>
            v.Rule == "R2" && v.TransitionActionName == "Submit",
            "R2 violation must name the bare transition");
    }

    // ── S-R2-Happy — spec WITH valid Assignment → no R2 (SUFFICIENT proof of R2 at Save-time) ──

    [Fact]
    public async Task ValidateForSave_SpecWithValidAssignment_NoR2Violation()
    {
        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""") });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await _validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeTrue("R2: valid assignment present");
        report.Violations.Should().NotContain(v => v.Rule == "R2");
    }

    // ── S-R3-CanonicalLowercase — lowercase ByPermission canonicalizes to uppercase in spec ──

    [Fact]
    public async Task ValidateForSave_LowercasePermissionCode_CanonicalizesInSpecToUppercase()
    {
        var clause = new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["workflow.task.process"]""");
        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { clause });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await _validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeTrue("lowercase code is still recognized — case-insensitive lookup");
        report.Violations.Should().NotContain(v => v.Rule == "R3");
        clause.RefsJson.Should().Be("""["WORKFLOW.TASK.PROCESS"]""",
            "R3 must rewrite spec.RefsJson canonical-form (uppercased) for repo Phase 3 to persist");
    }

    // ── S-R3-UnknownCode — unrecognized PermCode → R3 violation ─────────────

    [Fact]
    public async Task ValidateForSave_UnknownPermissionCode_ReturnsR3Violation()
    {
        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["bogus.unknown.code"]""") });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await _validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeFalse();
        report.Violations.Should().Contain(v =>
            v.Rule == "R3" && v.Detail.Contains("bogus.unknown.code"),
            "R3 must name the unknown PermCode in the violation detail");
    }

    // ── S-R4-Intersect — CombineMode.Intersect rejected at Save (Q6 strict) ──

    [Fact]
    public async Task ValidateForSave_AssignmentCombineModeIntersect_ReturnsR4Violation()
    {
        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Intersect,
            AllowAssignorOverride: false,
            Clauses: new[] { new AssignmentClauseSpec(AssignmentClauseMode.Initiator, null) });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await _validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeFalse();
        report.Violations.Should().Contain(v =>
            v.Rule == "R4" && v.Detail.Contains("Intersect"),
            "R4 strict-at-Save: Intersect is NotSupported (Q6 ratified)");
    }

    // ── S-R4-PreviousHandler — clause mode shipped 2026-05-24 (RT2-15 Phase 01) ──
    //
    // PreviousHandler removed from _notSupportedModes; positive Save assertion for mode 2
    // lands in Phase 05 cross-mode validator sweep. ByGroup / ByOrgUnit assertions still
    // apply (Phases 02 + 04 will lift them in turn).

    // ── S-R4-ByRole — clause mode shipped 2026-05-24 (RT2-15 Phase 03 — B1 partial) ──
    //
    // ByRole removed from _notSupportedModes. Read-side claim/inbox extension tracked in
    // T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION (P0 CRITICAL) — until shipped, ByRole-pooled
    // tasks are unclaimable. Production deploys MUST NOT seed ByRole assignments until
    // read-side ships.

    [Fact]
    public async Task ValidateForSave_ByRoleClause_GreenBar()
    {
        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { new AssignmentClauseSpec(AssignmentClauseMode.ByRole, """["ROLE_REVIEWER"]""") });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await _validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeTrue("ByRole lifted from _notSupportedModes in Phase 03");
        report.Violations.Should().NotContain(v => v.Rule == "R4");
    }

    [Fact]
    public async Task ValidateForActivate_ByRoleClause_NoR4Violation()
    {
        var defId = 1L;
        var stateA = MakeState(10, defId, isInitial: true);
        var stateB = MakeState(20, defId, isFinal: true);
        var t1 = MakeTransition(100, defId, 10, 20);

        var assign = MakeAssignment(100);
        assign.AddClause(AssignmentClauseMode.ByRole, """["ROLE_REVIEWER"]""");

        var assignmentByTransitionId = new Dictionary<long, OutgoingAssignment?>
        {
            [100] = Outgoing(t1, assign),
        };

        var report = await _validator.ValidateForActivateAsync(
            states: [stateA, stateB],
            transitions: [t1],
            assignmentByTransitionId: assignmentByTransitionId,
            tenantId: Guid.Empty,
            ct: CancellationToken.None);

        report.Violations.Should().NotContain(v => v.Rule == "R4" && v.Detail.Contains("ByRole"),
            "ByRole MUST NOT fire R4 after Phase 03 lift");
    }

    [Fact]
    public async Task ValidateForSave_ByRoleMalformedJson_ReturnsR3Violation()
    {
        // M2 fold (code-review 260524-2106): ByRole malformed JSON MUST emit R3 violation
        // — fail-loud symmetry with ByPermission `CanonicalizeByPermissionCodes` JsonException branch.
        // Silent-swallow asymmetry would let garbage RefsJson persist with empty canonical writeback.
        const string malformed = "[\"ROLE_A\""; // truncated array
        var clause = new AssignmentClauseSpec(AssignmentClauseMode.ByRole, malformed);
        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { clause });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await _validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeFalse();
        report.Violations.Should().Contain(v =>
            v.Rule == "R3" && v.Detail.Contains("ByRole RefsJson is not valid JSON"),
            "R3 violation must name the malformed ByRole RefsJson (mirror ByPermission semantic)");
    }

    [Fact]
    public async Task ValidateForSave_ByRoleLowercaseAndPadded_CanonicalizesToTrimmedUppercase()
    {
        // Mirror ByPermission canonicalization semantic: Trim + ToUpperInvariant Save-time.
        // Phase 03 (B1): canonicalization runs purely text-based (no PermCodes.All lookup —
        // role names are tenant-defined, not enumerable from a static set).
        var clause = new AssignmentClauseSpec(AssignmentClauseMode.ByRole, """["  role_reviewer  ", "ROLE_APPROVER"]""");
        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { clause });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await _validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeTrue("ByRole canonicalization should not produce violations");
        clause.RefsJson.Should().Be("""["ROLE_REVIEWER","ROLE_APPROVER"]""",
            "ByRole RefsJson must be canonicalized to Trim+ToUpperInvariant (mirror ByPermission)");
    }

    // ── S-Sufficient — Initiator clause (ref-less) on valid graph → green-bar (SUFFICIENT for R2+R3+R4) ──

    [Fact]
    public async Task ValidateForSave_InitiatorClauseRefLess_GreenBar()
    {
        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { new AssignmentClauseSpec(AssignmentClauseMode.Initiator, null) });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await _validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeTrue("Initiator is MVP-supported + ref-less; full happy-path Save");
        report.Violations.Should().BeEmpty();
    }

    // ── S-Necessary — 2 transitions, only 1 has Assignment → R2 fires only on the bare one ──

    [Fact]
    public async Task ValidateForSave_TwoTransitions_OnlyOneHasAssignment_R2OnlyOnBare()
    {
        // Graph: Draft → InReview → Approved. Submit has Assignment, Approve is bare.
        var defId = 1L;
        var draft = WorkflowState.Create(defId, "Draft", "Nháp", "Draft", isInitial: true, isFinal: false, "#FFF", 1);
        var inReview = WorkflowState.Create(defId, "InReview", "Xem xét", "InReview", isInitial: false, isFinal: false, "#FFF", 2);
        var approved = WorkflowState.Create(defId, "Approved", "Đã duyệt", "Approved", isInitial: false, isFinal: true, "#FFF", 3);

        var withAsg = new AssignmentSpec(
            AssignmentCombineMode.Union, false,
            new[] { new AssignmentClauseSpec(AssignmentClauseMode.Initiator, null) });

        var submit = new NewTransitionSpec("Draft", "InReview", "Submit", "Nộp", "Submit", null, 1, false)
        {
            Assignment = withAsg
        };
        var approve = new NewTransitionSpec("InReview", "Approved", "Approve", "Duyệt", "Approve", null, 2, false)
        {
            Assignment = null // ← bare; R2 must fire HERE only
        };

        var report = await _validator.ValidateForSaveAsync([draft, inReview, approved], [submit, approve], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeFalse();
        report.Violations.Should().ContainSingle(v => v.Rule == "R2",
            "R2 fires exactly once — only the bare transition is in violation");
        report.Violations.Should().Contain(v =>
            v.Rule == "R2" && v.TransitionActionName == "Approve",
            "R2 must name the BARE transition (Approve), not the configured one (Submit)");
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // Phase 02 (RT2-15) — ByGroup Q2 Save-validation unit tests
    // Q2 semantic: ResolveActiveUserGroupsByCodesAsync returns set of active group codes.
    // Unknown codes surface in violation metadata["UnknownGroupCodes"].
    // 260522-E anti-tautology: metadata must partition correctly (known vs unknown).
    // ════════════════════════════════════════════════════════════════════════════════════════

    // ── U1 — Happy path: all known group codes → no violation ───────────────────

    [Fact]
    public async Task U1_ByGroup_AllKnownCodes_NoViolation()
    {
        var validator = new WorkflowGraphValidator(
            MakeIdentityClientWithGroups("GROUP_X", "GROUP_Y"));

        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { new AssignmentClauseSpec(AssignmentClauseMode.ByGroup, """["GROUP_X", "GROUP_Y"]""") });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeTrue("all group codes are known (active)");
        report.Violations.Should().NotContain(v =>
            v.Rule == "R3",
            "no R3 violation should exist when all codes are known");
    }

    // ── U2 — Inactive group treated as unknown → violation with metadata ────────

    [Fact]
    public async Task U2_ByGroup_InactiveTreatedAsUnknown_ProducesViolation()
    {
        // Stub returns empty set (inactive group filtered out by Q2 semantic)
        var validator = new WorkflowGraphValidator(
            MakeIdentityClientWithGroups(/* no active groups */));

        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { new AssignmentClauseSpec(AssignmentClauseMode.ByGroup, """["GROUP_B_INACTIVE"]""") });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeFalse("inactive group is treated as unknown");
        report.Violations.Should().ContainSingle(v =>
            v.Rule == "R3",
            "exactly one R3 violation for unknown group codes");

        var violation = report.Violations.First(v => v.Rule == "R3");
        violation.Metadata.Should().NotBeNull("violation must carry metadata");
        violation.Metadata.Should().ContainKey("UnknownGroupCodes",
            "metadata must contain UnknownGroupCodes key");

        var unknownCodes = violation.Metadata["UnknownGroupCodes"];
        unknownCodes.Should().NotBeNull("UnknownGroupCodes must be set");
        // Metadata value is IReadOnlyList<string>; cast and assert
        if (unknownCodes is IReadOnlyList<string> codeList)
        {
            codeList.Should().ContainSingle(c => c == "GROUP_B_INACTIVE",
                "UnknownGroupCodes must contain exactly the inactive group code");
        }
        else
        {
            unknownCodes.ToString().Should().Contain("GROUP_B_INACTIVE",
                "UnknownGroupCodes must contain the inactive group code");
        }
    }

    // ── U3 — Fully unknown group code → violation ────────────────────────────

    [Fact]
    public async Task U3_ByGroup_FullyUnknown_ProducesViolation()
    {
        var validator = new WorkflowGraphValidator(
            MakeIdentityClientWithGroups(/* no groups at all */));

        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { new AssignmentClauseSpec(AssignmentClauseMode.ByGroup, """["DOES_NOT_EXIST"]""") });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeFalse("unknown group code must produce violation");
        var violation = report.Violations.FirstOrDefault(v => v.Rule == "R3");
        violation.Should().NotBeNull("R3 violation must exist for unknown group codes");
        violation.Metadata.Should().ContainKey("UnknownGroupCodes");

        var unknownCodes = violation.Metadata["UnknownGroupCodes"];
        if (unknownCodes is IReadOnlyList<string> codeList)
        {
            codeList.Should().ContainSingle(c => c == "DOES_NOT_EXIST");
        }
        else
        {
            unknownCodes.ToString().Should().Contain("DOES_NOT_EXIST");
        }
    }

    // ── U4 — Mixed known + unknown: only unknown in metadata (260522-E anti-tautology) ──

    [Fact]
    public async Task U4_ByGroup_MixedKnownUnknown_OnlyUnknownInMetadata()
    {
        // Stub: only GROUP_TEST_A is active; TYPO_X and TYPO_Y are unknown
        var validator = new WorkflowGraphValidator(
            MakeIdentityClientWithGroups("GROUP_TEST_A"));

        var asg = new AssignmentSpec(
            CombineMode: AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: new[] { new AssignmentClauseSpec(
                AssignmentClauseMode.ByGroup,
                """["GROUP_TEST_A", "TYPO_X", "TYPO_Y"]""") });
        var (states, submit) = MakeMinimalGraph(submitAssignment: asg);

        var report = await validator.ValidateForSaveAsync(states, [submit], [], Guid.Empty, CancellationToken.None);

        report.IsValid.Should().BeFalse("unknown codes present; validation fails");
        var violation = report.Violations.FirstOrDefault(v => v.Rule == "R3");
        violation.Should().NotBeNull("R3 violation must exist for unknown codes");

        // 260522-E anti-tautology: metadata must contain ONLY unknown codes, not known ones
        violation.Metadata.Should().ContainKey("UnknownGroupCodes");
        var unknownList = violation.Metadata["UnknownGroupCodes"];

        // Cast to IReadOnlyList<string> and verify contents
        if (unknownList is IReadOnlyList<string> codeList)
        {
            // Assert exact set: TYPO_X, TYPO_Y but NOT GROUP_TEST_A
            codeList.Should().HaveCount(2, "exactly 2 unknown codes");
            codeList.Should().Contain("TYPO_X", "unknown code TYPO_X must be in metadata");
            codeList.Should().Contain("TYPO_Y", "unknown code TYPO_Y must be in metadata");
            codeList.Should().NotContain("GROUP_TEST_A",
                "260522-E: known code GROUP_TEST_A MUST NOT appear in UnknownGroupCodes metadata (anti-tautology lock)");
        }
        else
        {
            // Fallback to string representation if not directly cast able
            var unknownStr = unknownList.ToString();
            unknownStr.Should().Contain("TYPO_X", "unknown code TYPO_X must be in metadata");
            unknownStr.Should().Contain("TYPO_Y", "unknown code TYPO_Y must be in metadata");
            unknownStr.Should().NotContain("GROUP_TEST_A",
                "260522-E: known code GROUP_TEST_A MUST NOT appear in UnknownGroupCodes metadata (anti-tautology lock)");
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Branch-child assignment guard (Save RB5) + branch-aware Activate (R1 exemptions + RB5 pool).
    // ════════════════════════════════════════════════════════════════════════════

    private static WorkflowState[] BranchGraphStates() =>
    [
        WorkflowState.Create(1L, "Draft", "Nháp", "Draft", isInitial: true, isFinal: false, "#FFF", 1),
        WorkflowState.Create(1L, "Review", "Thẩm định", "Review", isInitial: false, isFinal: false, "#FFF", 2),
        WorkflowState.Create(1L, "Approved", "Duyệt", "Approved", isInitial: false, isFinal: true, "#FFF", 3),
    ];

    private static NewBranchSpec BranchSpecWithChildAssignment(AssignmentSpec? childAssignment) =>
        new(
            BranchingStateName: "Draft",
            ContinuationStateName: "Approved",
            BranchType: ParallelBranchType.And,
            RequiredCount: null,
            AllowCancelOnAnyReject: false,
            TimeoutMinutes: 0,
            PositionX: null,
            PositionY: null,
            Children:
            [
                new NewBranchChildSpec(
                    TargetStateName: "Review",
                    Name: "legal-review",
                    DisplayNameVi: "Thẩm định",
                    DepartmentId: null,
                    DefaultAssigneeId: null,
                    SortOrder: 0,
                    Resolutions: [])
                {
                    Assignment = childAssignment,
                },
            ]);

    // ── Save RB5: a non-claimable mode (ByGroup) on a branch-child pool → violation ──
    [Fact]
    public async Task ValidateForSave_BranchChildByGroupClause_ReturnsRB5Violation()
    {
        var asg = new AssignmentSpec(AssignmentCombineMode.Union, false,
            [new AssignmentClauseSpec(AssignmentClauseMode.ByGroup, """["GROUP_TEST_A"]""")]);

        var report = await _validator.ValidateForSaveAsync(
            BranchGraphStates(), [], [BranchSpecWithChildAssignment(asg)], Guid.Empty, ct: CancellationToken.None);

        report.Violations.Should().Contain(v => v.Rule == "RB5" && v.Detail.Contains("ByGroup"),
            "branch-child pool is restricted to read-side-claimable modes (only ByPermission/ByRole)");
    }

    // ── Save RB5: allowed mode (ByPermission) canonicalizes in place, no RB5 ──
    [Fact]
    public async Task ValidateForSave_BranchChildByPermissionClause_CanonicalizesAndNoRB5()
    {
        var clause = new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["workflow.task.process"]""");
        var asg = new AssignmentSpec(AssignmentCombineMode.Union, false, [clause]);

        var report = await _validator.ValidateForSaveAsync(
            BranchGraphStates(), [], [BranchSpecWithChildAssignment(asg)], Guid.Empty, ct: CancellationToken.None);

        report.Violations.Should().NotContain(v => v.Rule == "RB5",
            "ByPermission is an allowed branch-child pool mode");
        clause.RefsJson.Should().Be("""["WORKFLOW.TASK.PROCESS"]""",
            "branch-child ByPermission must canonicalize (Trim+Upper) so the resolver reads canonical-at-rest");
    }

    // ── Save R1 branch-aware: a valid parallel topology passes end-to-end (regression lock) ──
    [Fact]
    public async Task ValidateForSave_ValidParallelTopology_IsValid()
    {
        // A fork-only branching state + fork-only child-target must NOT trip Save R1 — else the parallel
        // def the Activate validator now accepts could never be authored through Save (Activate moot).
        var asg = new AssignmentSpec(AssignmentCombineMode.Union, false,
            [new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")]);

        var report = await _validator.ValidateForSaveAsync(
            BranchGraphStates(), [], [BranchSpecWithChildAssignment(asg)], Guid.Empty, ct: CancellationToken.None);

        report.IsValid.Should().BeTrue(
            "fork-only branching + child-target states are R1-exempt at Save (symmetric with Activate)");
    }

    // ── Save R1 (RT10 mirror): a child-target that is ALSO a normal dead-end ToState STILL fires R1 ──
    [Fact]
    public async Task ValidateForSave_ChildTargetAlsoNormalDeadEnd_StillFiresR1()
    {
        // Locks the Save-side RT10 half-clause: a child-target reachable via a normal transition with no
        // outgoing is a real dead-end — the fork-child exemption must NOT mask it (mirror of the Activate test).
        var draft = WorkflowState.Create(1L, "Draft", "Nháp", "Draft", isInitial: true, isFinal: false, "#FFF", 1);
        var review = WorkflowState.Create(1L, "Review", "Thẩm định", "Review", isInitial: false, isFinal: false, "#FFF", 2);
        var approved = WorkflowState.Create(1L, "Approved", "Duyệt", "Approved", isInitial: false, isFinal: true, "#FFF", 3);

        var perm = new AssignmentSpec(AssignmentCombineMode.Union, false,
            [new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")]);
        // Normal transition Draft → Review makes Review a non-recall ToState; Review has no outgoing → dead-end.
        var draftToReview = new NewTransitionSpec("Draft", "Review", "Submit", "Nộp", "Submit", null, 1, false)
        {
            Assignment = perm,
        };

        var report = await _validator.ValidateForSaveAsync(
            [draft, review, approved], [draftToReview], [BranchSpecWithChildAssignment(perm)], Guid.Empty, ct: CancellationToken.None);

        report.Violations.Should().Contain(v => v.Rule == "R1" && v.StateName == "Review",
            "a child-target reachable via normal flow with no outgoing must not be masked by the fork-child exemption (RT10)");
    }

    // ── Save RB5: a child WITHOUT an assignment is allowed (draft authoring) ──
    [Fact]
    public async Task ValidateForSave_BranchChildNoAssignment_NoRB5Violation()
    {
        var report = await _validator.ValidateForSaveAsync(
            BranchGraphStates(), [], [BranchSpecWithChildAssignment(childAssignment: null)], Guid.Empty, ct: CancellationToken.None);

        report.Violations.Should().NotContain(v => v.Rule == "RB5",
            "a child without a pool is allowed at Save (draft); Activate enforces presence");
    }

    // ── Activate-side branch helpers ──
    private static WorkflowBranchChildAssignment ChildPool(AssignmentClauseMode mode, string? refsJson)
    {
        var a = WorkflowBranchChildAssignment.Create(branchChildId: 0);
        a.AddClause(mode, refsJson);
        return a;
    }

    private static WorkflowBranchChild MakeBranchChild(long id, long branchId, long targetStateId, WorkflowBranchChildAssignment? pool)
    {
        var c = WorkflowBranchChild.Create(branchId, targetStateId, $"child_{id}", "Vi", null, 0);
        SetPrivateId(c, id);
        if (pool is not null) c.SetAssignment(pool);
        return c;
    }

    private static WorkflowParallelBranch MakeBranch(long id, long defId, long branchingStateId, long continuationStateId, params WorkflowBranchChild[] children)
    {
        var b = WorkflowParallelBranch.Create(defId, branchingStateId, continuationStateId, ParallelBranchType.And, null, false, 0);
        SetPrivateId(b, id);
        foreach (var c in children) b.AddChild(c);
        return b;
    }

    private static Dictionary<long, OutgoingAssignment?> NoAssignments() => new();

    // ── Activate R1: branching state (fork-only) is exempt when branches are supplied (anti-tautology) ──
    [Fact]
    public async Task ValidateForActivate_BranchingStateNoTransition_ExemptFromR1_OnlyWithBranches()
    {
        var defId = 1L;
        var draft = MakeState(10, defId, isInitial: true);
        var review = MakeState(20, defId);
        var approved = MakeState(30, defId, isFinal: true);
        var branch = MakeBranch(1000, defId, branchingStateId: 10, continuationStateId: 30,
            MakeBranchChild(200, 1000, targetStateId: 20, ChildPool(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")));

        // Anti-tautology: without branch-awareness the branching state looks like a dead-end → R1 fires.
        var withoutBranches = await _validator.ValidateForActivateAsync(
            states: [draft, review, approved], transitions: [], assignmentByTransitionId: NoAssignments(),
            tenantId: Guid.Empty, branches: null, ct: CancellationToken.None);
        withoutBranches.Violations.Should().Contain(v => v.Rule == "R1" && v.StateName == draft.Name);

        // With branches: branching + pure child-target exempt → valid.
        var withBranches = await _validator.ValidateForActivateAsync(
            states: [draft, review, approved], transitions: [], assignmentByTransitionId: NoAssignments(),
            tenantId: Guid.Empty, branches: [branch], ct: CancellationToken.None);
        withBranches.Violations.Should().NotContain(v => v.Rule == "R1",
            "branching + fork-only child-target states are exempt from R1");
        withBranches.IsValid.Should().BeTrue();
    }

    // ── Activate R1 (RT10): a child-target that is ALSO a normal dead-end ToState must STILL fire R1 ──
    [Fact]
    public async Task ValidateForActivate_ChildTargetAlsoNormalDeadEnd_StillFiresR1()
    {
        var defId = 1L;
        var draft = MakeState(10, defId, isInitial: true);
        var review = MakeState(20, defId);   // child-target AND a normal ToState with no outgoing
        var approved = MakeState(30, defId, isFinal: true);
        var t1 = MakeTransition(100, defId, fromId: 10, toId: 20); // normal Draft → Review
        var assignT1 = MakeAssignment(100);
        assignT1.AddClause(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""");
        var asgById = new Dictionary<long, OutgoingAssignment?> { [100] = Outgoing(t1, assignT1) };
        var branch = MakeBranch(1000, defId, branchingStateId: 10, continuationStateId: 30,
            MakeBranchChild(200, 1000, targetStateId: 20, ChildPool(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")));

        var report = await _validator.ValidateForActivateAsync(
            states: [draft, review, approved], transitions: [t1], assignmentByTransitionId: asgById,
            tenantId: Guid.Empty, branches: [branch], ct: CancellationToken.None);

        report.Violations.Should().Contain(v => v.Rule == "R1" && v.StateName == review.Name,
            "a child-target reachable via normal flow with no outgoing is a real dead-end — the fork exemption must not mask it");
    }

    // ── Activate FR3: the continuation state is NOT exempt — still needs an outgoing Close ──
    [Fact]
    public async Task ValidateForActivate_ContinuationStateNotExempt_FiresR1()
    {
        var defId = 1L;
        var draft = MakeState(10, defId, isInitial: true);
        var review = MakeState(20, defId);
        var cont = MakeState(30, defId);   // continuation, non-final, no outgoing
        var branch = MakeBranch(1000, defId, branchingStateId: 10, continuationStateId: 30,
            MakeBranchChild(200, 1000, targetStateId: 20, ChildPool(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")));

        var report = await _validator.ValidateForActivateAsync(
            states: [draft, review, cont], transitions: [], assignmentByTransitionId: NoAssignments(),
            tenantId: Guid.Empty, branches: [branch], ct: CancellationToken.None);

        report.Violations.Should().Contain(v => v.Rule == "R1" && v.StateName == cont.Name,
            "the continuation state is not exempt (FR3) — it must carry an outgoing Close transition");
    }

    // ── Activate RB5: a branch child with no pool cannot activate (anchored to branching state) ──
    [Fact]
    public async Task ValidateForActivate_BranchChildWithoutPool_ReturnsRB5Violation()
    {
        var defId = 1L;
        var draft = MakeState(10, defId, isInitial: true);
        var review = MakeState(20, defId);
        var approved = MakeState(30, defId, isFinal: true);
        var branch = MakeBranch(1000, defId, branchingStateId: 10, continuationStateId: 30,
            MakeBranchChild(200, 1000, targetStateId: 20, pool: null));

        var report = await _validator.ValidateForActivateAsync(
            states: [draft, review, approved], transitions: [], assignmentByTransitionId: NoAssignments(),
            tenantId: Guid.Empty, branches: [branch], ct: CancellationToken.None);

        report.Violations.Should().Contain(v => v.Rule == "RB5" && v.StateName == draft.Name,
            "a child with no pool cannot activate (the fork fan-out would have no candidate to claim it)");
    }

    // ── Activate RB5 (defensive): a non-claimable mode persisted at rest is rejected at Activate ──
    [Fact]
    public async Task ValidateForActivate_BranchChildByOrgUnitPool_ReturnsRB5Violation()
    {
        var defId = 1L;
        var draft = MakeState(10, defId, isInitial: true);
        var review = MakeState(20, defId);
        var approved = MakeState(30, defId, isFinal: true);
        var branch = MakeBranch(1000, defId, branchingStateId: 10, continuationStateId: 30,
            MakeBranchChild(200, 1000, targetStateId: 20, ChildPool(AssignmentClauseMode.ByOrgUnit, """["OU_X"]""")));

        var report = await _validator.ValidateForActivateAsync(
            states: [draft, review, approved], transitions: [], assignmentByTransitionId: NoAssignments(),
            tenantId: Guid.Empty, branches: [branch], ct: CancellationToken.None);

        report.Violations.Should().Contain(v => v.Rule == "RB5" && v.Detail.Contains("ByOrgUnit"),
            "Activate defensively rejects a non-claimable branch-child pool mode persisted at rest");
    }
}
