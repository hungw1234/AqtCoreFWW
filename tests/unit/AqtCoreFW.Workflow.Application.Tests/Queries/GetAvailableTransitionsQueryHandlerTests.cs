using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.GetAvailableTransitions;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Reflection;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetAvailableTransitionsQueryHandler.
///
/// Corrected D1/D5 semantic (Slice-5 Defect-1 fix):
///   A transition T is visible iff the actor satisfies T's OWN assignment rule
///   (T as outgoing of the instance's CURRENT state — symmetric with ETR authz-of-T gate).
///
/// Mock wiring:
///   - GetOutgoingWithAssignmentsAsync(CurrentStateId=10L) → OAs keyed by Transition.Id
///   - GetWithStatesAndTransitionsByInternalIdAsync → definition with currentState (Id=10L)
///   - Resolver is called as: resolver.Resolve(currentState, [tOA], ctx)
///     where currentState.Id == 10L and tOA.Transition.Id == dto.Id
///   - Final-target transitions get NO auto-pass — resolver decides visibility uniformly.
/// </summary>
public sealed class GetAvailableTransitionsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly long DefinitionId = 42L;
    private static readonly Guid UserId = Guid.NewGuid();
    // CurrentStateId — the state the instance is currently AT (outgoing transitions come from here)
    private static readonly long CurrentStateId = 10L;

    private readonly IReadDbConnection _db;
    private readonly IIdentityModuleClient _identityClient;
    private readonly IOrgUnitModuleClient _orgUnitClient;
    private readonly IWorkflowDefinitionRepository _defRepo;
    private readonly IWorkflowHandlerResolver _resolver;
    private readonly GetAvailableTransitionsQueryHandler _handler;

    public GetAvailableTransitionsQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _identityClient = Substitute.For<IIdentityModuleClient>();
        _orgUnitClient = Substitute.For<IOrgUnitModuleClient>();
        _defRepo = Substitute.For<IWorkflowDefinitionRepository>();
        _resolver = Substitute.For<IWorkflowHandlerResolver>();

        // Default: empty permission set — tests override when permissions needed
        _identityClient.GetEffectivePermissionCodesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string>());
        // Default: definition not found (tests that exercise resolver override this)
        _defRepo.GetWithStatesAndTransitionsByInternalIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<WorkflowDefinition>("not found"));
        // Default: no outgoing assignments
        _defRepo.GetOutgoingWithAssignmentsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>([]));
        // Default: resolver returns empty
        _resolver.ResolveAsync(
                Arg.Any<WorkflowState>(),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Empty);

        _handler = new GetAvailableTransitionsQueryHandler(
            _db, _identityClient, _orgUnitClient, _defRepo, _resolver,
            NullLogger<GetAvailableTransitionsQueryHandler>.Instance);
    }

    // --- Helpers ---

    private static long _transitionIdCounter = 100L;

    /// <summary>
    /// Builds a WorkflowTransitionDto from the current state (FromStateId=CurrentStateId).
    /// Id is unique per call — used as the OA lookup key.
    /// </summary>
    private static WorkflowTransitionDto MakeTransition(
        long? toStateId = null,
        string actionName = "Action",
        bool isRecall = false)
        => new(
            Id: _transitionIdCounter++,
            FromStateId: CurrentStateId,
            ToStateId: toStateId ?? 20L,
            ActionName: actionName,
            ActionLabelVi: actionName,
            ActionLabelEn: actionName,
            ConditionsJson: null,
            SortOrder: 1,
            IsRecall: isRecall);

    private void SetupInstance(Guid? createdBy = null)
    {
        _db.QueryFirstOrDefaultAsync<GetAvailableTransitionsQueryHandler.InstanceStateRow>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new GetAvailableTransitionsQueryHandler.InstanceStateRow
            {
                InternalId = 1L,
                CurrentStateId = CurrentStateId,
                DefinitionId = DefinitionId,
                CreatedBy = createdBy ?? Guid.Empty,
            });
    }

    private void SetupTransitions(params WorkflowTransitionDto[] transitions)
    {
        _db.QueryAsync<WorkflowTransitionDto>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<WorkflowTransitionDto>>(transitions));
    }

    private void GrantPermission(Guid userId, params string[] codes)
    {
        _identityClient.GetEffectivePermissionCodesAsync(userId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string>(codes));
    }

    private void GrantRoles(Guid userId, params string[] roleNames)
    {
        _identityClient.GetUserRoleCodesAsync(userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string>(roleNames, StringComparer.Ordinal));
    }

    /// <summary>
    /// Builds a WorkflowState with the given Id (T1 entity — reflection-assigned).
    /// </summary>
    private static WorkflowState MakeState(long stateId, bool isFinal = false)
    {
        var gen = TestGuidGenerator.Create();
        var def = WorkflowDefinition.Create(gen, "Def", "Desc", Guid.NewGuid(), Guid.NewGuid());
        var state = WorkflowState.Create(def.Id, $"State{stateId}", $"State {stateId}", $"State{stateId}",
            isInitial: stateId == CurrentStateId, isFinal: isFinal, "#808080", 0);
        typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
            .SetValue(state, stateId);
        return state;
    }

    /// <summary>
    /// Builds a WorkflowTransition T1 entity with the given Id (reflection-assigned).
    /// From=CurrentStateId so it appears as an outgoing transition of the current state.
    /// </summary>
    private static WorkflowTransition MakeTransitionEntity(long transitionId, long toStateId = 20L)
    {
        var state = MakeState(CurrentStateId);
        var t = WorkflowTransition.Create(
            state.Id, CurrentStateId, toStateId,
            $"Action{transitionId}", $"Nhãn {transitionId}", $"Label{transitionId}",
            conditionsJson: null, sortOrder: 1);
        typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
            .SetValue(t, transitionId);
        return t;
    }

    /// <summary>
    /// Sets up:
    ///   - definition repo returning a definition with currentState (Id=CurrentStateId=10L)
    ///   - GetOutgoingWithAssignmentsAsync(CurrentStateId) → [OA(transitionEntity, null)] per dto
    ///   - resolver on currentState matching by s.Id==CurrentStateId → given resolution per transition
    ///
    /// For each (dto, resolution) pair the resolver is configured to return the given resolution
    /// when called with the OA whose Transition.Id == dto.Id.
    /// </summary>
    private void SetupCurrentStateAuthz(
        WorkflowDefinition definition,
        (WorkflowTransitionDto Dto, HandlerResolution Resolution)[] transitions)
    {
        _defRepo.GetWithStatesAndTransitionsByInternalIdAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        // Build OA list from current state — one per dto
        var oaList = transitions.Select(pair =>
        {
            var tEntity = MakeTransitionEntity(pair.Dto.Id, pair.Dto.ToStateId);
            return new OutgoingAssignment(tEntity, null); // Assignment=null is fine for unit tests
        }).ToArray();

        _defRepo.GetOutgoingWithAssignmentsAsync(CurrentStateId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(oaList));

        // Configure resolver per transition: match on currentState + single OA whose Transition.Id == dto.Id
        var currentState = definition.States.First(s => s.Id == CurrentStateId);
        foreach (var (dto, resolution) in transitions)
        {
            var capturedId = dto.Id; // capture for lambda
            _resolver.ResolveAsync(
                    Arg.Is<WorkflowState>(s => s.Id == currentState.Id),
                    Arg.Is<IReadOnlyList<OutgoingAssignment>>(list =>
                        list.Count == 1 && list[0].Transition.Id == capturedId),
                    Arg.Any<WorkflowResolutionContext>(),
                    Arg.Any<CancellationToken>())
                .Returns(resolution);
        }
    }

    /// <summary>
    /// Builds a definition containing a currentState (Id=CurrentStateId) and optional extra states.
    /// </summary>
    private static WorkflowDefinition MakeDefinitionWithCurrentState(params long[] extraStateIds)
    {
        var gen = TestGuidGenerator.Create();
        var def = WorkflowDefinition.Create(gen, "Def", "Desc", Guid.NewGuid(), Guid.NewGuid());
        var currentState = MakeState(CurrentStateId);
        def.AddState(currentState);
        foreach (var id in extraStateIds)
            def.AddState(MakeState(id));
        return def;
    }

    // ─── Success path ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolver returns Assignee = actor for T's own OA (from current state) → transition returned.
    /// D5: authz is on T's own assignment rule, not the target's outgoing pool.
    /// </summary>
    [Fact]
    public async Task Handle_ResolverAssignee_ActorIsAssignee_TransitionReturned()
    {
        SetupInstance();
        var transition = MakeTransition(actionName: "Submit");
        SetupTransitions(transition);

        var def = MakeDefinitionWithCurrentState(transition.ToStateId);
        SetupCurrentStateAuthz(def, [(transition, HandlerResolution.ToAssignee(UserId))]);

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].ActionName.Should().Be("Submit");
    }

    /// <summary>
    /// Resolver returns Pool and actor has matching permission → transition returned.
    /// D5: resolver.Resolve(currentState, [tOA], ctx) called for T's own rule.
    /// </summary>
    [Fact]
    public async Task Handle_ResolverPool_ActorHasMatchingPermission_TransitionReturned()
    {
        SetupInstance();
        var transition = MakeTransition(actionName: "Approve");
        SetupTransitions(transition);

        var def = MakeDefinitionWithCurrentState(transition.ToStateId);
        var resolution = HandlerResolution.Pool(new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, "CASES.WORKFLOW.APPROVE", AssignmentCombineMode.Union)
        });
        SetupCurrentStateAuthz(def, [(transition, resolution)]);
        GrantPermission(UserId, "CASES.WORKFLOW.APPROVE");

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].ActionName.Should().Be("Approve");
    }

    /// <summary>
    /// Resolver returns a ByRole Pool; actor holds the role (but NO matching permission code) →
    /// transition returned. Regression guard for the permission-only authz bug that hid ByRole
    /// transitions from role-holders (transition authz must be in parity with claim eligibility).
    /// </summary>
    [Fact]
    public async Task Handle_ResolverPool_ByRole_ActorHasRole_TransitionReturned()
    {
        SetupInstance();
        var transition = MakeTransition(actionName: "Approve");
        SetupTransitions(transition);

        var def = MakeDefinitionWithCurrentState(transition.ToStateId);
        var resolution = HandlerResolution.Pool(new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Role, "GOVOFFICER", AssignmentCombineMode.Union)
        });
        SetupCurrentStateAuthz(def, [(transition, resolution)]);
        GrantRoles(UserId, "GOVOFFICER"); // role-only; no permission code granted

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].ActionName.Should().Be("Approve");
    }

    /// <summary>
    /// Resolver returns Pool but actor lacks the required permission → transition hidden (fail-CLOSED).
    /// D5: authz checked via T's OWN rule; result is same for final/non-final targets.
    /// </summary>
    [Fact]
    public async Task Handle_ResolverPool_ActorMissingPermission_TransitionFiltered()
    {
        SetupInstance();
        var transition = MakeTransition(actionName: "Approve");
        SetupTransitions(transition);

        var def = MakeDefinitionWithCurrentState(transition.ToStateId);
        var resolution = HandlerResolution.Pool(new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, "CASES.WORKFLOW.APPROVE", AssignmentCombineMode.Union)
        });
        SetupCurrentStateAuthz(def, [(transition, resolution)]);
        GrantPermission(UserId, "CASES.VIEW"); // different permission

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    /// <summary>
    /// D5 Defect-2 fix: final-target transitions get NO auto-pass — resolver decides uniformly.
    /// Transition whose target is a final state: actor must still satisfy T's own rule.
    /// If actor fails, transition is hidden (no bypass for final targets).
    /// </summary>
    [Fact]
    public async Task Handle_FinalTargetTransition_ResolverDecides_NoAutoPass()
    {
        SetupInstance();
        // ToStateId=99L is a final state — but no auto-pass in corrected semantic
        var transition = MakeTransition(toStateId: 99L, actionName: "Close");
        SetupTransitions(transition);

        var def = MakeDefinitionWithCurrentState(transition.ToStateId);
        // Resolver returns unsatisfied pool for T's own rule → hidden
        var resolution = HandlerResolution.Pool(new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, "CASES.WORKFLOW.CLOSE", AssignmentCombineMode.Union)
        });
        SetupCurrentStateAuthz(def, [(transition, resolution)]);
        // Actor has NO permissions

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("final-target transition without matching rule must be hidden");
    }

    /// <summary>
    /// Final-target transition: actor satisfies T's own rule → transition shown.
    /// (No double-standard for final vs non-final targets.)
    /// </summary>
    [Fact]
    public async Task Handle_FinalTargetTransition_ActorSatisfiesRule_TransitionShown()
    {
        SetupInstance();
        var transition = MakeTransition(toStateId: 99L, actionName: "Close");
        SetupTransitions(transition);

        var def = MakeDefinitionWithCurrentState(transition.ToStateId);
        SetupCurrentStateAuthz(def, [(transition, HandlerResolution.ToAssignee(UserId))]);

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].ActionName.Should().Be("Close");
    }

    /// <summary>
    /// Actor has no permissions; T1 resolves to actor directly (Assignee) → shown.
    /// T2 resolves to a pool actor lacks → hidden.
    /// </summary>
    [Fact]
    public async Task Handle_EmptyPermissions_AssigneeTransitionShown_PoolTransitionFiltered()
    {
        SetupInstance();
        var submitT = MakeTransition(toStateId: 20L, actionName: "Submit");
        var reviewT = MakeTransition(toStateId: 21L, actionName: "Review");
        SetupTransitions(submitT, reviewT);

        var def = MakeDefinitionWithCurrentState(20L, 21L);
        SetupCurrentStateAuthz(def, [
            (submitT, HandlerResolution.ToAssignee(UserId)),
            (reviewT, HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, "REVIEW_PERM", AssignmentCombineMode.Union)
            }))
        ]);
        // Actor has no permissions — but Assignee branch does not need permissions

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].ActionName.Should().Be("Submit");
    }

    /// <summary>
    /// Multiple transitions; actor satisfies resolver for some but not all.
    /// </summary>
    [Fact]
    public async Task Handle_MultipleTransitions_ResolverFiltersCorrectly()
    {
        SetupInstance();
        var submitT = MakeTransition(toStateId: 20L, actionName: "Submit");
        var approveT = MakeTransition(toStateId: 21L, actionName: "Approve");
        var rejectT = MakeTransition(toStateId: 22L, actionName: "Reject");
        var escalateT = MakeTransition(toStateId: 23L, actionName: "Escalate");
        SetupTransitions(submitT, approveT, rejectT, escalateT);

        var def = MakeDefinitionWithCurrentState(20L, 21L, 22L, 23L);
        SetupCurrentStateAuthz(def, [
            (submitT, HandlerResolution.ToAssignee(UserId)),
            (approveT, HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, "CASES.WORKFLOW.APPROVE", AssignmentCombineMode.Union)
            })),
            (rejectT, HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, "CASES.WORKFLOW.REJECT", AssignmentCombineMode.Union)
            })),
            (escalateT, HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, "CASES.WORKFLOW.ESCALATE", AssignmentCombineMode.Union)
            }))
        ]);
        GrantPermission(UserId, "CASES.WORKFLOW.APPROVE", "CASES.WORKFLOW.ESCALATE");

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Select(t => t.ActionName).Should().Contain(["Submit", "Approve", "Escalate"]);
        result.Value.Select(t => t.ActionName).Should().NotContain("Reject");
    }

    // ─── Case (h): resolver-based eligibility (fail-CLOSED for resolver throw) ───

    /// <summary>
    /// Case (h): returns only transitions whose OWN rule the actor satisfies.
    /// Resolver throw on one transition → that transition excluded (no 500).
    /// D5: fail-CLOSED — resolver throw means hide, not allow-through.
    /// </summary>
    [Fact]
    public async Task Handle_ResolverThrows_TransitionExcluded_FailClosed_Not500()
    {
        SetupInstance();
        var approveT = MakeTransition(toStateId: 20L, actionName: "Approve");
        var rejectT = MakeTransition(toStateId: 30L, actionName: "Reject");
        SetupTransitions(approveT, rejectT);
        GrantPermission(UserId, "CASES.WORKFLOW.APPROVE");

        // Build definition with currentState
        var def = MakeDefinitionWithCurrentState(20L, 30L);
        _defRepo.GetWithStatesAndTransitionsByInternalIdAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(def));

        // Build OAs for both transitions
        var approveTEntity = MakeTransitionEntity(approveT.Id, 20L);
        var rejectTEntity = MakeTransitionEntity(rejectT.Id, 30L);
        _defRepo.GetOutgoingWithAssignmentsAsync(CurrentStateId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new[]
            {
                new OutgoingAssignment(approveTEntity, null),
                new OutgoingAssignment(rejectTEntity, null)
            }));

        var currentState = def.States.First(s => s.Id == CurrentStateId);

        // approveT: resolver returns satisfying pool
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == currentState.Id),
                Arg.Is<IReadOnlyList<OutgoingAssignment>>(list =>
                    list.Count == 1 && list[0].Transition.Id == approveT.Id),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, "CASES.WORKFLOW.APPROVE", AssignmentCombineMode.Union)
            }));

        // rejectT: resolver THROWS (deferred mode) → excluded, no 500
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == currentState.Id),
                Arg.Is<IReadOnlyList<OutgoingAssignment>>(list =>
                    list.Count == 1 && list[0].Transition.Id == rejectT.Id),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Throws(new NotSupportedException("PreviousHandler not supported — INTERNAL"));

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue("handler must return Ok even when resolver throws for one transition");
        result.Value.Should().ContainSingle("only Approve passes; Reject is excluded by resolver throw");
        result.Value[0].ActionName.Should().Be("Approve");
    }

    /// <summary>
    /// Transition not found in currentOutgoing OA set → hidden (fail-CLOSED).
    /// This occurs when the transition DTO exists in Dapper SQL but has no assignment row
    /// and the OA set was populated without it (e.g. DB inconsistency or missing seed).
    /// </summary>
    [Fact]
    public async Task Handle_TransitionNotInOutgoingOASet_TransitionHidden_FailClosed()
    {
        SetupInstance();
        var transition = MakeTransition(actionName: "Orphan");
        SetupTransitions(transition);

        var def = MakeDefinitionWithCurrentState(transition.ToStateId);
        _defRepo.GetWithStatesAndTransitionsByInternalIdAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(def));
        // GetOutgoingWithAssignmentsAsync returns EMPTY — transition.Id not present → hidden
        _defRepo.GetOutgoingWithAssignmentsAsync(CurrentStateId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>([]));

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("transition not in OA set must be hidden (fail-CLOSED)");
    }

    // ─── M2: InitiatorUserId uses instance.CreatedBy, not request.UserId ─────────

    /// <summary>
    /// M2 fix: resolver must be invoked with InitiatorUserId == instance.CreatedBy, not request.UserId.
    /// Verified via NSubstitute arg-capture on WorkflowResolutionContext.
    /// Both a distinct InstanceCreatorId and a distinct request.UserId are used to prove the fix is not
    /// accidentally satisfied by UserId == CreatedBy coincidence.
    /// </summary>
    [Fact]
    public async Task Handle_InitiatorModeRule_UsesInstanceCreatorNotRequestUser()
    {
        var instanceCreatorId = Guid.NewGuid();
        // request.UserId is a completely different user (not the creator)
        var requestUserId = Guid.NewGuid();

        SetupInstance(createdBy: instanceCreatorId);
        var transition = MakeTransition(actionName: "InitiatorOnly");
        SetupTransitions(transition);

        var def = MakeDefinitionWithCurrentState(transition.ToStateId);
        _defRepo.GetWithStatesAndTransitionsByInternalIdAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(def));

        var tEntity = MakeTransitionEntity(transition.Id, transition.ToStateId);
        _defRepo.GetOutgoingWithAssignmentsAsync(CurrentStateId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(
                new[] { new OutgoingAssignment(tEntity, null) }));

        // Capture the resolution context passed to the resolver
        WorkflowResolutionContext? capturedCtx = null;
        var currentState = def.States.First(s => s.Id == CurrentStateId);
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == currentState.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Do<WorkflowResolutionContext>(c => capturedCtx = c),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Empty);

        await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, requestUserId),
            CancellationToken.None);

        // M2 assertion: resolver receives instance creator as InitiatorUserId, not the querying user
        capturedCtx.Should().NotBeNull("resolver must be invoked");
        capturedCtx!.InitiatorUserId.Should().Be(instanceCreatorId,
            "InitiatorUserId must equal instance.CreatedBy (M2 fix)");
        capturedCtx.InitiatorUserId.Should().NotBe(requestUserId,
            "InitiatorUserId must NOT be the querying user (pre-M2 bug)");
    }

    // --- Not found ---

    [Fact]
    public async Task Handle_InstanceNotFound_ReturnsNotFoundError()
    {
        _db.QueryFirstOrDefaultAsync<GetAvailableTransitionsQueryHandler.InstanceStateRow>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((GetAvailableTransitionsQueryHandler.InstanceStateRow?)null);

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain(InstanceId.ToString());
    }

    /// <summary>
    /// Definition not found → fail-CLOSED: return empty list (not 500, not allow-through).
    /// </summary>
    [Fact]
    public async Task Handle_DefinitionNotFound_ReturnsEmptyList_FailClosed()
    {
        SetupInstance();
        var transition = MakeTransition(actionName: "Submit");
        SetupTransitions(transition);
        // Default: _defRepo.GetWithStatesAndTransitionsByInternalIdAsync returns Fail (set in ctor)

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue("definition not found = fail-CLOSED → empty list, not error");
        result.Value.Should().BeEmpty();
    }

    // ─── Recall transition regression test (Slice-5 creator-recall fix) ────────────────────────

    /// <summary>
    /// Recall transition must always be included regardless of resolver result.
    /// WorkflowHandlerResolver excludes IsRecall by design (WorkflowHandlerResolver.cs:32) so
    /// resolver.Resolve would return Empty → recall T was incorrectly hidden pre-fix.
    /// The execute path's creator guard is the real authz; GetAvailableTransitions shows the
    /// recall action so the creator can see and trigger it.
    /// </summary>
    [Fact]
    public async Task Handle_RecallTransition_AlwaysIncluded_RegardlessOfResolver()
    {
        SetupInstance();

        // One recall transition + one non-recall (non-recall uses fail-CLOSED OA-not-found path)
        var recallT = MakeTransition(toStateId: 30L, actionName: "Recall", isRecall: true);
        var nonRecallT = MakeTransition(toStateId: 20L, actionName: "Submit");
        SetupTransitions(recallT, nonRecallT);

        var def = MakeDefinitionWithCurrentState(20L, 30L);
        // Wire definition repo so handler finds the current state
        _defRepo.GetWithStatesAndTransitionsByInternalIdAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(def));

        // Wire non-recall transition with resolver returning a satisfied pool (so it is also included)
        var nonRecallEntity = MakeTransitionEntity(nonRecallT.Id, nonRecallT.ToStateId);
        _defRepo.GetOutgoingWithAssignmentsAsync(CurrentStateId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(
                new[] { new OutgoingAssignment(nonRecallEntity, null) }));
        var currentState = def.States.First(s => s.Id == CurrentStateId);
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == currentState.Id),
                Arg.Is<IReadOnlyList<OutgoingAssignment>>(list =>
                    list.Count == 1 && list[0].Transition.Id == nonRecallT.Id),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.ToAssignee(UserId));

        var result = await _handler.Handle(
            new GetAvailableTransitionsQuery(InstanceId, TenantId, UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2, "both recall and non-recall transitions must be in the result");
        result.Value.Should().Contain(t => t.ActionName == "Recall" && t.IsRecall,
            "recall transition must always be included — resolver gate is N/A for recall");
        result.Value.Should().Contain(t => t.ActionName == "Submit",
            "non-recall transition with satisfied resolver must also be included");
    }
}
