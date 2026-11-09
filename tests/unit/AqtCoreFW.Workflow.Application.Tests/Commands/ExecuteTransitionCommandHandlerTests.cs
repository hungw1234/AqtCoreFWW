using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.ExecuteTransition;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Reflection;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for ExecuteTransitionCommandHandler.
/// Phase-02: authz migrated from role-equality to permission-set membership via
/// IIdentityModuleClient.GetEffectivePermissionCodesAsync. Tests validate the new contract.
/// Phase-4b B4c:
///   - WorkflowDefinition T2: PublicId (Guid AK) used as external key; Id (long) = internal FK.
///   - WorkflowInstance T2: PublicId (Guid AK) used in command InstanceId; Id (long) = internal.
///   - WorkflowState T1: Id (long) used in transitions / state comparisons.
///   - GetWithHistoryAsync(Guid publicId) — stubbed with instance.PublicId.
///   - GetWithStatesAndTransitionsAsync(Guid publicId) — stubbed with definition.PublicId.
///   - GetBranchByStateIdAsync(long definitionInternalId, long stateInternalId).
///   - ExecuteTransitionCommand.InstanceId = Guid (PublicId), TransitionId = long.
/// Phase-3 Slice-5 (corrected D1 semantic):
///   - TWO SEPARATE resolver invocations (D1 mandate):
///     1. Authz-of-T gate: resolves T's OWN assignment rule (outgoing of CURRENT state).
///        Runs ALWAYS — no IsFinal exception (Defect-2 fix).
///     2. WF_NO_HANDLER pre-check: resolves targetState's full outgoing pool.
///        Runs only for non-final targets.
///   - GetOutgoingWithAssignmentsAsync(currentState.Id) → single OA for transition.
///   - GetOutgoingWithAssignmentsAsync(targetState.Id) → full outgoing list for WF_NO_HANDLER.
///   - identityClient IS called for the authz-of-T gate.
/// </summary>
public sealed class ExecuteTransitionCommandHandlerTests
{
    private static readonly Guid DefaultTenantId = Guid.NewGuid();

    private readonly IWorkflowInstanceRepository _instanceRepository;
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly IParallelBranchRepository _branchRepository;
    private readonly IParallelBranchService _branchService;
    private readonly ITransitionAttachmentRepository _attachmentRepository;
    private readonly IIdentityModuleClient _identityClient;
    private readonly IWorkflowHandlerResolver _resolver;
    private readonly ExecuteTransitionCommandHandler _handler;

    public ExecuteTransitionCommandHandlerTests()
    {
        _instanceRepository = Substitute.For<IWorkflowInstanceRepository>();
        _definitionRepository = Substitute.For<IWorkflowDefinitionRepository>();
        _conditionEvaluator = Substitute.For<IConditionEvaluator>();
        _branchRepository = Substitute.For<IParallelBranchRepository>();
        _branchService = Substitute.For<IParallelBranchService>();
        _attachmentRepository = Substitute.For<ITransitionAttachmentRepository>();
        _identityClient = Substitute.For<IIdentityModuleClient>();
        _resolver = Substitute.For<IWorkflowHandlerResolver>();

        // Default: conditions always pass
        _conditionEvaluator.Evaluate(Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, object?>>())
            .Returns(Result.Ok());
        // Default: no parallel branch configured for any state
        _branchRepository.GetBranchByStateIdAsync(
                Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowParallelBranch?)null);
        // Default: actor has empty permission set — tests override when a permission is required
        _identityClient.GetEffectivePermissionCodesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string>());
        // Default: no outgoing assignment rules for any state (tests override via SetupValidScenario)
        _definitionRepository.GetOutgoingWithAssignmentsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>([]));
        // Default: resolver returns empty (tests override for specific scenarios)
        _resolver.ResolveAsync(
                Arg.Any<WorkflowState>(),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Empty);

        _handler = new ExecuteTransitionCommandHandler(
            _instanceRepository, _definitionRepository, _conditionEvaluator,
            _branchRepository, _branchService, _attachmentRepository, _identityClient,
            _resolver, NullLogger<ExecuteTransitionCommandHandler>.Instance,
            TestGuidGenerator.Create(),
            new WorkflowTaskFactory(Substitute.For<IWorkflowTaskRepository>()),
            Substitute.For<IWorkflowTaskRepository>(), // task repo: source-state task completion on transition (no open tasks here)
            Substitute.For<IOrgUnitModuleClient>());   // org-unit codes for authz parity (auto-empty set)
    }

    // --- Builder helpers ---

    private static long _entityIdSeed;

    /// <summary>
    /// Assigns a unique test Id to a T1 entity via reflection.
    /// T1 entities have Id=0 without EF DB-assignment; unique IDs ensure correct
    /// state-lookup and from/to state matching in handler logic.
    /// </summary>
    private static long AssignTestId(AqtCoreFW.SharedKernel.Domain.Entity entity)
    {
        var id = Interlocked.Increment(ref _entityIdSeed);
        typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(entity, id);
        return id;
    }

    /// <summary>
    /// Builds a two-state definition (Draft → Approved) with one transition,
    /// wires both repository mocks, and returns a tuple of all relevant entities.
    /// Assigns unique test IDs to T1 states/transitions so handler state-lookup works.
    ///
    /// Corrected D1 semantic wiring:
    ///   - GetOutgoingWithAssignmentsAsync(draftState.Id) → [OA for the transition] (authz-of-T input)
    ///   - Resolver on draftState → satisfying pool "ANY_PERMISSION" (authz-of-T passes by default)
    ///   - GetOutgoingWithAssignmentsAsync(approvedState.Id) → [] (empty, WF_NO_HANDLER pre-check)
    ///     only wired when approvedIsFinal=false; overridden to non-empty by success tests
    ///   - Resolver on approvedState → satisfying pool "ANY_PERMISSION" for WF_NO_HANDLER pass
    ///     (only wired when approvedIsFinal=false)
    /// </summary>
    private (
        WorkflowInstance instance,
        WorkflowDefinition definition,
        WorkflowState draftState,
        WorkflowState approvedState,
        WorkflowTransition transition)
        SetupValidScenario(
            bool approvedIsFinal = false)
    {
        var gen = TestGuidGenerator.Create();
        var createdBy = Guid.NewGuid();

        // Build definition with two states — gen required (T2 entity)
        var definition = WorkflowDefinition.Create(gen, "Flow", "Desc", DefaultTenantId, createdBy);
        var draftState = WorkflowState.Create(definition.Id, "Draft", "Nháp", "Draft",
            isInitial: true, isFinal: false, "#808080", 1);
        AssignTestId(draftState);
        var approvedState = WorkflowState.Create(definition.Id, "Approved", "Đã duyệt", "Approved",
            isInitial: false, isFinal: approvedIsFinal, "#00FF00", 2);
        AssignTestId(approvedState);
        definition.AddState(draftState);
        definition.AddState(approvedState);

        // Build transition Draft → Approved (uses assigned state IDs)
        var transition = WorkflowTransition.Create(
            definition.Id, draftState.Id, approvedState.Id,
            "Approve", "Phê duyệt", "Approve",
            conditionsJson: null, sortOrder: 1);
        AssignTestId(transition);
        definition.AddTransition(transition);

        // Build instance at Draft state — gen required (T2 entity)
        var instance = WorkflowInstance.Create(
            gen, definition.Id, draftState.Id, "Case",
            Guid.NewGuid(), DefaultTenantId, createdBy);

        // Wire repo mocks
        _instanceRepository.GetWithHistoryAsync(instance.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(instance));
        _definitionRepository.GetWithStatesAndTransitionsByInternalIdAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));
        // Slice 7: ETR now calls UpdateWithStateGuardNoSaveAsync + SaveChangesAsync (not UpdateWithStateGuardAsync).
        // Wire both defaults so all existing success-path tests continue to pass without extra arrangement.
        _instanceRepository.UpdateWithStateGuardNoSaveAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));
        _instanceRepository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(0)));
        // Old variant retained for 3 other callers; wired here for tests that explicitly assert on it
        // (e.g. Handle_StateConflictFromRepository_PropagatesError uses UpdateWithStateGuardAsync).
        _instanceRepository.UpdateWithStateGuardAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        // ── Authz-of-T gate defaults (on currentState = draftState) ──────────────────
        // Wire GetOutgoingWithAssignmentsAsync(draftState.Id) → list containing the transition's OA.
        // OA with null Assignment → resolver returns Empty → tOA-not-found branch (403).
        // For success-path tests, resolver on draftState is stubbed to return a satisfying pool.
        // We pass the actual transition object; Assignment=null (unit test — no EF entity).
        var draftOA = new OutgoingAssignment(transition, null);
        _definitionRepository.GetOutgoingWithAssignmentsAsync(
                draftState.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new[] { draftOA }));

        // Default resolver for authz-of-T: pool with "ANY_PERMISSION" so success-path tests pass.
        // Tests override this via SetResolverAuthzPool / SetResolverAuthzAssignee helpers.
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == draftState.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, "ANY_PERMISSION", AssignmentCombineMode.Union)
            }));

        // Default actor has "ANY_PERMISSION" so authz-of-T passes
        _identityClient.GetEffectivePermissionCodesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string>(["ANY_PERMISSION"]));

        if (!approvedIsFinal)
        {
            // ── WF_NO_HANDLER pre-check defaults (on targetState = approvedState) ──────────────────
            // Wire GetOutgoingWithAssignmentsAsync(approvedState.Id) → non-empty list (prevents 422).
            // Resolver on approvedState returns non-empty pool so WF_NO_HANDLER pre-check passes.
            // Tests that need WF_NO_HANDLER 422 override resolverOnApprovedState to return Empty.
            var fakeNextTransition = WorkflowTransition.Create(
                definition.Id, approvedState.Id, approvedState.Id,
                "FakeNext", "Fake", "Fake", conditionsJson: null, sortOrder: 99);
            AssignTestId(fakeNextTransition);
            var approvedOA = new OutgoingAssignment(fakeNextTransition, null);
            _definitionRepository.GetOutgoingWithAssignmentsAsync(
                    approvedState.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new[] { approvedOA }));

            _resolver.ResolveAsync(
                    Arg.Is<WorkflowState>(s => s.Id == approvedState.Id),
                    Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                    Arg.Any<WorkflowResolutionContext>(),
                    Arg.Any<CancellationToken>())
                .Returns(HandlerResolution.Pool(new List<CandidatePredicate>
                {
                    new(CandidatePrincipalType.Permission, "ANY_PERMISSION", AssignmentCombineMode.Union)
                }));
        }

        return (instance, definition, draftState, approvedState, transition);
    }

    private static ExecuteTransitionCommand BuildCommand(
        Guid instancePublicId, long transitionId, string? comment = null) => new(
        InstanceId: instancePublicId,
        TransitionId: transitionId,
        ActorId: Guid.NewGuid(),
        Comment: comment);

    private void GrantPermission(Guid actorId, params string[] codes) =>
        _identityClient.GetEffectivePermissionCodesAsync(actorId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string>(codes));

    /// <summary>
    /// Configures the resolver to return a pool resolution for authz-of-T gate.
    /// Key: currentState (the state the instance is AT) — authz checks T's OWN rule.
    /// </summary>
    private void SetResolverAuthzPool(WorkflowState currentState, params string[] codes)
    {
        var predicates = codes.Select(c => new CandidatePredicate(
            CandidatePrincipalType.Permission, c, AssignmentCombineMode.Union)).ToList();
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == currentState.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool(predicates));
    }

    /// <summary>
    /// Configures the resolver to return a specific-user (Assignee) for authz-of-T gate.
    /// Key: currentState (the state the instance is AT).
    /// </summary>
    private void SetResolverAuthzAssignee(WorkflowState currentState, Guid userId)
    {
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == currentState.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.ToAssignee(userId));
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidTransition_ReturnsSuccessWithUpdatedDto()
    {
        var (instance, _, _, approvedState, transition) = SetupValidScenario();
        var command = BuildCommand(instance.PublicId, transition.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentStateId.Should().Be(approvedState.Id);
    }

    [Fact]
    public async Task Handle_ValidTransition_MovesToTargetState()
    {
        var (instance, _, _, approvedState, transition) = SetupValidScenario();
        var command = BuildCommand(instance.PublicId, transition.Id);

        await _handler.Handle(command, CancellationToken.None);

        instance.CurrentStateId.Should().Be(approvedState.Id);
    }

    /// <summary>
    /// Slice 7 update: ETR now calls UpdateWithStateGuardNoSaveAsync + SaveChangesAsync
    /// (defer-save split) instead of the old in-place-save UpdateWithStateGuardAsync.
    /// </summary>
    [Fact]
    public async Task Handle_ValidTransition_CallsUpdateWithStateGuard()
    {
        var (instance, _, _, _, transition) = SetupValidScenario();
        var command = BuildCommand(instance.PublicId, transition.Id);

        await _handler.Handle(command, CancellationToken.None);

        // Slice 7: ETR calls the defer-save split variants, not the old in-place-save method.
        await _instanceRepository.Received(1).UpdateWithStateGuardNoSaveAsync(
            instance, Arg.Any<CancellationToken>());
        await _instanceRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FinalState_SetsCompletedAtOnInstance()
    {
        var (instance, _, draftState, _, transition) = SetupValidScenario(approvedIsFinal: true);
        var command = BuildCommand(instance.PublicId, transition.Id);

        // authz-of-T is wired by SetupValidScenario (draftState → ANY_PERMISSION)
        // No WF_NO_HANDLER check for final target (no extra resolver setup needed)

        await _handler.Handle(command, CancellationToken.None);

        instance.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ResolverPool_IdentityClientInvokedForAuthzGate()
    {
        // Slice 5: resolver pool-branch requires identity client for permission check.
        // identityClient.GetEffectivePermissionCodesAsync IS called for the authz-of-T gate.
        // Coarse [Authorize(Policy=PermCodes.Workflow.Task.Process)] remains active (session-1).
        var (instance, _, _, _, transition) = SetupValidScenario();
        var command = BuildCommand(instance.PublicId, transition.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // identityClient IS called — fine-grained authz gate active since Slice 5.
        await _identityClient.ReceivedWithAnyArgs(1)
            .GetEffectivePermissionCodesAsync(default, default);
    }

    // --- Failure paths ---

    [Fact]
    public async Task Handle_InstanceNotFound_ReturnsError()
    {
        var missingId = Guid.NewGuid();
        _instanceRepository.GetWithHistoryAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail($"WorkflowInstance '{missingId}' not found."));

        var command = BuildCommand(missingId, 999L);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_DefinitionNotFound_ReturnsError()
    {
        var (instance, definition, _, _, transition) = SetupValidScenario();
        var missingDefId = Guid.NewGuid();

        _definitionRepository.GetWithStatesAndTransitionsByInternalIdAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Fail($"WorkflowDefinition '{missingDefId}' not found."));

        var command = BuildCommand(instance.PublicId, transition.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_TransitionNotFound_ReturnsInvalidTransitionError()
    {
        var (instance, _, _, _, _) = SetupValidScenario();
        var command = BuildCommand(instance.PublicId, transitionId: 99999L);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("not found in definition"));
    }

    [Fact]
    public async Task Handle_TransitionFromWrongState_ReturnsInvalidTransitionError()
    {
        var (instance, definition, draftState, approvedState, _) = SetupValidScenario();

        var wrongTransition = WorkflowTransition.Create(
            definition.Id, approvedState.Id, draftState.Id,
            "Reopen", "Mở lại", "Reopen", conditionsJson: null, sortOrder: 2);
        AssignTestId(wrongTransition);
        definition.AddTransition(wrongTransition);

        var command = BuildCommand(instance.PublicId, wrongTransition.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("not valid from current state"));
    }

    // ─── Slice-5: Resolver fail-CLOSED authz-of-T gate (cases b–e, i, e2) ───

    /// <summary>
    /// Case (c): resolver for authz-of-T (currentState) returns Pool with one permission code;
    /// actor has that code → 2xx. Resolver IS the gate — actor authorized via permission match.
    /// D1: authz-of-T uses T's OWN rule from CURRENT state's outgoing.
    /// </summary>
    [Fact]
    public async Task Handle_ResolverPool_ActorHasMatchingPermission_Succeeds()
    {
        var (instance, _, draftState, _, transition) = SetupValidScenario();
        var actorId = Guid.NewGuid();
        var command = BuildCommand(instance.PublicId, transition.Id) with { ActorId = actorId };

        // Override authz-of-T resolver (currentState = draftState) with specific permission
        SetResolverAuthzPool(draftState, "CASES.WORKFLOW.APPROVE");
        GrantPermission(actorId, "CASES.WORKFLOW.APPROVE");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Case (e): resolver for authz-of-T returns Pool with one permission code;
    /// actor has DIFFERENT codes → 403. Fail-CLOSED: missing permission = ForbiddenError.
    /// D1: authz-of-T check is on CURRENT state's OA (draftState), not targetState.
    /// </summary>
    [Fact]
    public async Task Handle_ResolverPool_ActorLacksPermission_ReturnsForbidden()
    {
        var (instance, _, draftState, _, transition) = SetupValidScenario();
        var actorId = Guid.NewGuid();
        var command = BuildCommand(instance.PublicId, transition.Id) with { ActorId = actorId };

        // authz-of-T requires APPROVE; actor only has VIEW
        SetResolverAuthzPool(draftState, "CASES.WORKFLOW.APPROVE");
        GrantPermission(actorId, "CASES.VIEW");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
    }

    /// <summary>
    /// Case (d): resolver for authz-of-T returns Assignee = actorId → 2xx.
    /// Single named user authorized via Assignee branch.
    /// D1: authz uses T's own rule from CURRENT state.
    /// </summary>
    [Fact]
    public async Task Handle_ResolverAssignee_ActorIsAssignee_Succeeds()
    {
        var (instance, _, draftState, _, transition) = SetupValidScenario();
        var actorId = Guid.NewGuid();
        var command = BuildCommand(instance.PublicId, transition.Id) with { ActorId = actorId };

        SetResolverAuthzAssignee(draftState, actorId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Case (e): resolver for authz-of-T returns Assignee = differentUserId; actor is NOT assignee → 403.
    /// D1: authz-of-T check is on CURRENT state's OA (draftState).
    /// </summary>
    [Fact]
    public async Task Handle_ResolverAssignee_ActorIsNotAssignee_ReturnsForbidden()
    {
        var (instance, _, draftState, _, transition) = SetupValidScenario();
        var actorId = Guid.NewGuid();
        var differentUser = Guid.NewGuid();
        var command = BuildCommand(instance.PublicId, transition.Id) with { ActorId = actorId };

        SetResolverAuthzAssignee(draftState, differentUser);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
    }

    /// <summary>
    /// Case (b): resolver throws NotSupportedException (deferred mode) for authz-of-T → fail-CLOSED: 403.
    /// Exception text must NOT appear in the error message (no leak).
    /// D1: resolver throw on CURRENT state's resolution → deny (not 500, not allow-through).
    /// </summary>
    [Fact]
    public async Task Handle_ResolverThrows_FailClosed_ReturnsForbidden_NoExceptionLeak()
    {
        var (instance, _, draftState, _, transition) = SetupValidScenario();
        var command = BuildCommand(instance.PublicId, transition.Id);

        // Override authz-of-T resolver on draftState to throw
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == draftState.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Throws(new NotSupportedException("PreviousHandler mode not yet implemented — INTERNAL"));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
        result.Errors.Should().NotContain(e => e.Message.Contains("not yet implemented"));
        result.Errors.Should().NotContain(e => e.Message.Contains("INTERNAL"));
    }

    /// <summary>
    /// Case (a): WF_NO_HANDLER — resolver returns Empty for targetState's full outgoing pool
    /// on a non-final target state → 422 ValidationError.
    ///
    /// D1: authz-of-T MUST PASS FIRST (via draftState mock from SetupValidScenario defaults).
    /// Then the SEPARATE second call on approvedState (targetState) returns Empty → 422.
    /// Fail-CLOSED: misconfigured workflow (no handler for next state) never allows-through.
    /// </summary>
    [Fact]
    public async Task Handle_ResolverEmptyPool_NonFinalTarget_ReturnsValidationError_WF_NO_HANDLER()
    {
        var (instance, _, _, approvedState, transition) = SetupValidScenario(approvedIsFinal: false);
        var command = BuildCommand(instance.PublicId, transition.Id);

        // authz-of-T (draftState) passes via SetupValidScenario defaults (ANY_PERMISSION pool).
        // Override WF_NO_HANDLER resolver (approvedState = targetState) to return Empty → 422.
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == approvedState.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Empty);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors.Should().ContainSingle(
            e => e.Metadata.ContainsKey("WorkflowErrorCode")
              && (string)e.Metadata["WorkflowErrorCode"] == WorkflowErrorCodes.NoHandlerResolved);
    }

    /// <summary>
    /// Case (e2): Defect-2 regression — final target state does NOT skip authz-of-T gate.
    /// Actor fails T's own assignment rule even though approvedState.IsFinal == true → still 403.
    /// Spec line 28: "actor may execute T iff actor satisfies T's assignment rule" (NO final exception).
    /// </summary>
    [Fact]
    public async Task Handle_FinalTarget_ActorFailsAuthzOfT_ReturnsForbidden()
    {
        var (instance, _, draftState, _, transition) = SetupValidScenario(approvedIsFinal: true);
        var actorId = Guid.NewGuid();
        var command = BuildCommand(instance.PublicId, transition.Id) with { ActorId = actorId };

        // Override authz-of-T (draftState) to require a specific permission the actor lacks
        SetResolverAuthzPool(draftState, "CASES.WORKFLOW.APPROVE");
        // Actor has NO permissions → fails authz-of-T → 403 despite final target
        GrantPermission(actorId /* no permissions */);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
        // State must NOT have changed — authz gate prevents ApplyTransition.
        // Slice 7: ETR calls UpdateWithStateGuardNoSaveAsync (not the old method) on the save path.
        await _instanceRepository.DidNotReceive().UpdateWithStateGuardNoSaveAsync(
            Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Case (a-final regression): when authz-of-T PASSES and target IS final → succeeds.
    /// WF_NO_HANDLER pre-check is SKIPPED for final targets (correct).
    /// </summary>
    [Fact]
    public async Task Handle_FinalTarget_AuthzPasses_Succeeds()
    {
        var (instance, _, _, _, transition) = SetupValidScenario(approvedIsFinal: true);
        var command = BuildCommand(instance.PublicId, transition.Id);

        // authz-of-T (draftState) passes via SetupValidScenario defaults (ANY_PERMISSION).
        // WF_NO_HANDLER resolver on approvedState NOT called (final target → skipped).

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        instance.CompletedAt.Should().NotBeNull();
    }

    /// <summary>
    /// Slice 7 update: ETR no longer calls UpdateWithStateGuardAsync directly.
    /// Concurrency failures from the outer-save boundary surface via SaveChangesAsync returning Result.Fail.
    /// </summary>
    [Fact]
    public async Task Handle_StateConflictFromRepository_PropagatesError()
    {
        var (instance, _, _, _, transition) = SetupValidScenario();

        // Slice 7: wire SaveChangesAsync to return failure (concurrency at outer-save boundary).
        _instanceRepository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<int>(WorkflowErrors.StateConflictAtBoundary())));

        var command = BuildCommand(instance.PublicId, transition.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("WorkflowErrorCode")
            && (string)e.Metadata["WorkflowErrorCode"] == WorkflowErrorCodes.StateConflict);
    }

    // ─── Recall transition regression tests (Slice-5 creator-recall fix) ───────────────────────

    /// <summary>
    /// Helper: flip IsRecall=true on a WorkflowTransition entity via reflection.
    /// WorkflowTransition.IsRecall is private set; Create() accepts isRecall param but
    /// SetupValidScenario creates without it. Reflection mirrors existing AssignTestId pattern.
    /// </summary>
    private static void SetIsRecall(WorkflowTransition transition)
    {
        typeof(WorkflowTransition)
            .GetProperty("IsRecall", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(transition, true);
    }

    /// <summary>
    /// Recall by creator: resolver gate and GetOutgoingWithAssignmentsAsync must NOT be called.
    /// Pre-Slice-5 behavior: creator recall bypasses the resolver entirely.
    /// </summary>
    [Fact]
    public async Task Handle_RecallTransition_ByCreator_BypassesResolverGate_Succeeds()
    {
        var (instance, _, _, _, transition) = SetupValidScenario(approvedIsFinal: true);

        // Mark transition as recall AFTER setup (approvedIsFinal=true avoids WF_NO_HANDLER involvement)
        SetIsRecall(transition);

        // Command actor == instance creator — the only actor allowed to recall
        var command = BuildCommand(instance.PublicId, transition.Id) with { ActorId = instance.CreatedBy };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("creator of a recall transition must be permitted to execute it");

        // Resolver must NOT be invoked — recall authz = creator guard; resolver excludes IsRecall by design
        await _resolver.DidNotReceive().ResolveAsync(
            Arg.Any<WorkflowState>(),
            Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
            Arg.Any<WorkflowResolutionContext>(),
            Arg.Any<CancellationToken>());

        // GetOutgoingWithAssignmentsAsync must NOT be called for the recall path
        await _definitionRepository.DidNotReceive().GetOutgoingWithAssignmentsAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Recall by non-creator: the creator guard at :95-98 returns ForbiddenError.
    /// Resolver and GetOutgoingWithAssignmentsAsync must NOT be called — the guard fires first.
    /// </summary>
    [Fact]
    public async Task Handle_RecallTransition_ByNonCreator_ReturnsForbidden_FromCreatorGuard()
    {
        var (instance, _, _, _, transition) = SetupValidScenario(approvedIsFinal: true);
        SetIsRecall(transition);

        // Actor is NOT the creator
        var nonCreator = Guid.NewGuid();
        var command = BuildCommand(instance.PublicId, transition.Id) with { ActorId = nonCreator };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue("non-creator must be denied on a recall transition");
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("WorkflowErrorCode")
            && (string)e.Metadata["WorkflowErrorCode"] == WorkflowErrorCodes.TransitionForbidden);

        // Resolver and outgoing-assignment query must NOT be reached — creator guard fires before them
        await _resolver.DidNotReceive().ResolveAsync(
            Arg.Any<WorkflowState>(),
            Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
            Arg.Any<WorkflowResolutionContext>(),
            Arg.Any<CancellationToken>());
        await _definitionRepository.DidNotReceive().GetOutgoingWithAssignmentsAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    // ─── Slice-6: BranchFanOutException → GOV_WFL_015 mapping (H1 fix) ───────────────────────────

    /// <summary>
    /// H1 fix: locks the BranchFanOutException → GOV_WFL_015 422 surface.
    ///
    /// When <c>IParallelBranchService.InitiateBranchingAsync</c> throws, the handler
    /// wraps it in a private sentinel (<c>BranchFanOutException</c>), catches it,
    /// rolls back the own transaction, and returns <c>Result.Fail(WorkflowErrors.BranchFanOutFailed(...))</c>.
    ///
    /// This test asserts via metadata — NOT message text — so a constant-rename or factory
    /// change cannot silently pass (S5 META-L c: tests-to-impl lock must verify spec, not path).
    ///
    /// Pre-conditions verified:
    ///   - Mode A (no ambient tx): own tx IS opened so rollback can be observed.
    ///   - targetState is non-final: branch-staging block executes (<c>if (!targetState.IsFinal)</c>).
    ///   - <c>GetBranchByStateIdAsync</c> returns non-null: handler reaches <c>InitiateBranchingAsync</c>.
    /// </summary>
    [Fact]
    public async Task Handle_BranchFanOut_InitiateBranchingThrows_MapsToGOV_WFL_015()
    {
        // ── Arrange ──────────────────────────────────────────────────────────────────
        var (instance, definition, _, approvedState, transition) =
            SetupValidScenario(approvedIsFinal: false); // non-final → branch-staging block executes

        // Wire own-tx (Mode A): no ambient → handler opens txMock → rollback observable.
        // Default NSubstitute bool returns false, so HasAmbientTransaction() already returns false.
        // We wire BeginTransactionAsync explicitly so ownsTransaction != null and RollbackAsync fires.
        var txMock = Substitute.For<IWorkflowTransaction>();
        _instanceRepository.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(txMock));

        // Wire a non-null branch for approvedState → handler reaches InitiateBranchingAsync
        var branch = WorkflowParallelBranch.Create(
            definitionId: definition.Id,
            branchingStateId: approvedState.Id,
            continuationStateId: approvedState.Id, // self-ref: irrelevant for this test
            branchType: ParallelBranchType.And,
            requiredCount: null,
            allowCancelOnAnyReject: false,
            timeoutMinutes: 0);
        _branchRepository.GetBranchByStateIdAsync(
                Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(branch);

        // InitiateBranchingAsync throws → handler wraps in BranchFanOutException → GOV_WFL_015 path
        _branchService.InitiateBranchingAsync(
                Arg.Any<Guid>(), Arg.Any<WorkflowParallelBranch>(),
                Arg.Any<IReadOnlyDictionary<long, HandlerResolution>>(),
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Simulated staging failure"));

        var command = BuildCommand(instance.PublicId, transition.Id);

        // ── Act ───────────────────────────────────────────────────────────────────────
        var result = await _handler.Handle(command, CancellationToken.None);

        // ── Assert ────────────────────────────────────────────────────────────────────

        // (1) Result must be a failure — BranchFanOutException catch returns Result.Fail
        result.IsSuccess.Should().BeFalse("branch fan-out staging failure must return a failure result");

        // (2) Error code must be GOV_WFL_015 in metadata — confirms correct catch path reached
        //     (generic catch re-throws; only BranchFanOutException catch returns Result.Fail with this code)
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("WorkflowErrorCode")
            && (string)e.Metadata["WorkflowErrorCode"] == WorkflowErrorCodes.BranchFanOutFailed,
            "BranchFanOutException must map to GOV_WFL_015 (BranchFanOutFailed) — not 500 re-throw");

        // (3) Own transaction MUST be rolled back — fail-CLOSED invariant for Mode A
        await txMock.Received(1).RollbackAsync(Arg.Any<CancellationToken>());

        // (4) Commit must NOT be called — no partial commit on staging failure
        await txMock.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    // ─── Slice-7: Defer-save split — ConcurrencyAtOuterSave → GOV_WFL_013 (A5) ──────────────────────

    /// <summary>
    /// Slice 7 (TDD A5): defer-save split. Concurrency must map to GOV_WFL_013 at the OUTER
    /// SaveChangesAsync boundary (not inside the repository).
    ///
    /// Path-proof (S5 META-L (c) — anti-tautology preconditions):
    ///   (a) Received(1) on UpdateWithStateGuardNoSaveAsync — proves NEW variant was the call site,
    ///       not the old in-place-save variant (which would have caught DbUpdateConcurrencyException
    ///       internally at Repo:144 and returned the failure WITHOUT executing the outer save).
    ///   (b) Received(1) on SaveChangesAsync — proves outer-save boundary executed.
    ///   (c) Mapped error = WorkflowErrorCodes.StateConflict ("GOV_WFL_013") in metadata —
    ///       spec-lock (rename-resistant via metadata, not message text — same shape as H1).
    ///   (d) Own-tx rollback observed — fail-CLOSED invariant preserved (Mode A).
    ///   (e) Commit NOT called — no partial commit on race.
    /// Without (a) + (b), the test reduces to "concurrency exception maps to 409 somewhere" —
    /// tautology shape that would PASS even if ETR silently kept calling the old in-place-save
    /// variant. Both Received(1) are HARD spec-locks.
    ///
    /// Arrange deviation from plan Section 2 (documented per advisor gate):
    /// SaveChangesAsync is wired to return Result.Fail(StateConflictAtBoundary()) — NOT to throw
    /// DbUpdateConcurrencyException — because D1-α mandates the impl translates internally
    /// and the interface returns Task&lt;Result&lt;int&gt;&gt;. Throwing across the boundary would violate
    /// the Application→Infrastructure boundary invariant (no EF types in Application).
    /// All 5 precondition asserts (a-e) are preserved unchanged.
    /// </summary>
    [Fact]
    public async Task Handle_DeferSaveMode_ConcurrencyAtOuterSave_MapsToGOV_WFL_013()
    {
        // ── Arrange ──────────────────────────────────────────────────────────────────
        var (instance, _, _, _, transition) = SetupValidScenario(approvedIsFinal: true);
        // approvedIsFinal: true → skip branch-staging block (focus solely on save boundary).

        // Wire own-tx (Mode A): no ambient → handler opens txMock → rollback observable.
        // HasAmbientTransaction defaults to false (NSubstitute bool default).
        var txMock = Substitute.For<IWorkflowTransaction>();
        _instanceRepository.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(txMock));

        // (PATH PROOF a) NoSave variant returns Ok — does NOT throw — proves we got past
        // the repo call and into the outer save. If ETR still called the old variant, this
        // setup would be IGNORED (NSubstitute returns default) and the test would fail
        // differently (override-throw on old variant fires, or wrong path).
        _instanceRepository.UpdateWithStateGuardNoSaveAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));

        // SetupValidScenario already wires UpdateWithStateGuardAsync (the OLD variant) →
        // Result.Ok at line :163. We override it to throw — if ETR still calls the old
        // variant, this throw fires WITHOUT the NoSave receive-assert, proving wrong path.
        _instanceRepository.UpdateWithStateGuardAsync(instance, Arg.Any<CancellationToken>())
            .Returns<Task<Result>>(_ => throw new InvalidOperationException(
                "REGRESSION: ETR called OLD UpdateWithStateGuardAsync — must call …NoSaveAsync after Slice 7."));

        // Outer SaveChangesAsync returns Result.Fail(StateConflictAtBoundary()) — D1-α contract:
        // impl translates DbUpdateConcurrencyException internally; no exception crosses the boundary.
        // ETR inspects IsFailed → rollback → return Result.Fail (this is the GOV_WFL_013 surface).
        _instanceRepository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<int>(WorkflowErrors.StateConflictAtBoundary())));

        var command = BuildCommand(instance.PublicId, transition.Id);

        // ── Act ───────────────────────────────────────────────────────────────────────
        var result = await _handler.Handle(command, CancellationToken.None);

        // ── Assert (path-proof FIRST, behavior-assert SECOND) ────────────────────────

        // (a) PRECONDITION: NoSave variant was the call site — proves Slice 7 wired correctly.
        await _instanceRepository.Received(1).UpdateWithStateGuardNoSaveAsync(
            instance, Arg.Any<CancellationToken>());

        // (b) PRECONDITION: outer SaveChangesAsync executed — proves the new boundary save fired.
        await _instanceRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // (c) Behavior: result IsFailed with GOV_WFL_013 metadata (spec-lock — rename-resistant).
        result.IsSuccess.Should().BeFalse(
            "DbUpdateConcurrencyException at outer save must return Result.Fail (not throw 500).");
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("WorkflowErrorCode")
            && (string)e.Metadata["WorkflowErrorCode"] == WorkflowErrorCodes.StateConflict,
            "concurrency at outer-save boundary must map to GOV_WFL_013 (StateConflict 409) — not 500 re-throw");

        // (d) Fail-CLOSED: own-tx rolled back.
        await txMock.Received(1).RollbackAsync(Arg.Any<CancellationToken>());

        // (e) No partial commit.
        await txMock.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }
}
