using System.Reflection;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.ExecuteTransition;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Errors;
using AqtCoreFW.Workflow.Domain.Validation;
using AqtCoreFW.Workflow.Infrastructure.Clients;
using AqtCoreFW.Workflow.Infrastructure.Tests.Helpers;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Contract = AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Clients;

/// <summary>
/// Unit tests for InProcessWorkflowModuleClient (Phase-02 cross-module contract).
/// Validates delegation to ExecuteTransitionCommandHandler via IMediator and error-code mapping.
///
/// Phase-4b B6: T2 entities use PublicId (Guid AK) for external lookups.
///   T1 entities (WorkflowState, WorkflowTransition) have long Id assigned via reflection
///   so state-matching logic in the client (t.FromStateId == instance.CurrentStateId) resolves correctly.
///
/// Explicit SKIPs (red-team F12):
/// - Concurrent transitions → dead guard (D2), re-add after reinstatement.
/// - Deprecated definition mid-lifetime → no enforcement in Phase-02.
/// - CancellationToken mid-transaction → EF + MediatR tested upstream.
/// - Cache thundering herd → no cache in Phase-02.
/// </summary>
public sealed class InProcessWorkflowModuleClientTests
{
    private static readonly Guid DefaultTenantId = Guid.NewGuid();

    private readonly IWorkflowInstanceRepository _instanceRepo = Substitute.For<IWorkflowInstanceRepository>();
    private readonly IWorkflowDefinitionRepository _definitionRepo = Substitute.For<IWorkflowDefinitionRepository>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IGuidGenerator _gen = TestGuidGenerator.Create();
    private readonly IWorkflowGraphValidator _graphValidator = Substitute.For<IWorkflowGraphValidator>();
    private readonly IWorkflowHandlerResolver _handlerResolver = Substitute.For<IWorkflowHandlerResolver>();
    private readonly IWorkflowTaskFactory _taskFactory = Substitute.For<IWorkflowTaskFactory>();
    private readonly InProcessWorkflowModuleClient _sut;

    // Monotonic seed for T1 long-Id assignment via reflection (no EF save in unit tests)
    private static long _idSeed;

    public InProcessWorkflowModuleClientTests()
    {
        // Phase 5b defaults so happy-path CreateInstance tests use Stage + SaveChangesAsync flow
        // without requiring every legacy test to wire the new collaborators explicitly.
        _instanceRepo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(1)));
        _instanceRepo.HasAmbientTransaction().Returns(false);
        _instanceRepo.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IWorkflowTransaction>()));
        _handlerResolver.ResolveAsync(
                Arg.Any<WorkflowState>(),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.ToAssignee(Guid.NewGuid()));
        _definitionRepo.GetOutgoingWithAssignmentsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new List<OutgoingAssignment>()));

        _sut = new InProcessWorkflowModuleClient(
            _definitionRepo, _instanceRepo, _mediator,
            _gen,
            _graphValidator,
            _handlerResolver,
            _taskFactory,
            NullLogger<InProcessWorkflowModuleClient>.Instance);
    }

    /// <summary>
    /// Assigns a unique test long Id to a T1 entity via reflection.
    /// Required because T1 entities get long Id only from DB; tests don't save to EF.
    /// </summary>
    private static long AssignTestId(Entity entity)
    {
        var id = Interlocked.Increment(ref _idSeed);
        typeof(Entity)
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(entity, id);
        return id;
    }

    /// <summary>
    /// Builds Draft → Approved definition with "Submit" transition, wires mocks,
    /// returns entities for test assertions.
    /// T1 state/transition IDs assigned via reflection for correct state-matching.
    /// T2 definition/instance use PublicId (Guid AK) for mock wiring.
    /// </summary>
    private (WorkflowInstance instance,
             WorkflowDefinition definition,
             WorkflowState draft,
             WorkflowState approved,
             WorkflowTransition transition)
        SetupValidScenario(
            bool approvedIsFinal = false,
            string actionName = "Submit")
    {
        // S3 Slice 4: requiredPermissionCode removed from WorkflowTransition entity.
        var createdBy = Guid.NewGuid();
        var definition = WorkflowDefinition.Create(_gen, "Flow", "Desc", DefaultTenantId, createdBy);

        // T1 states: assign unique long IDs so FromStateId/CurrentStateId comparisons work
        var draft = WorkflowState.Create(definition.Id, "Draft", "Nháp", "Draft",
            isInitial: true, isFinal: false, "#808080", 1);
        AssignTestId(draft);
        var approved = WorkflowState.Create(definition.Id, "Approved", "Đã duyệt", "Approved",
            isInitial: false, isFinal: approvedIsFinal, "#00FF00", 2);
        AssignTestId(approved);
        definition.AddState(draft);
        definition.AddState(approved);

        // T1 transition: assign unique long ID so TransitionId lookup works in ExecuteTransitionCommand
        var transition = WorkflowTransition.Create(
            definition.Id, draft.Id, approved.Id,
            actionName, "Gửi", "Submit", null, 1);
        AssignTestId(transition);
        definition.AddTransition(transition);

        // T2 instance: Create uses gen for PublicId; definition.Id (long) + draft.Id (long) as FKs
        var instance = WorkflowInstance.Create(
            _gen,
            definition.Id,   // long FK
            draft.Id,        // long FK (T1 state Id)
            "Case", Guid.NewGuid(),
            DefaultTenantId, createdBy);

        // Mock wiring: instance looked up by PublicId (T2 AK); definition by internal long Id
        _instanceRepo.GetWithHistoryAsync(instance.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(instance));
        // Client uses GetWithStatesAndTransitionsByInternalIdAsync (instance.DefinitionId is long FK)
        _definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));
        // Wire PublicId lookup for CreateInstanceByDefinitionKey paths
        _definitionRepo.GetWithStatesAndTransitionsAsync(definition.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        return (instance, definition, draft, approved, transition);
    }

    private static Contract.ExecuteTransitionRequest BuildRequest(
        Guid instancePublicId, string actionName = "Submit", Guid? actorId = null, Guid? tenantId = null) =>
        new(
            InstanceId: instancePublicId,
            ActionName: actionName,
            ActorId: actorId ?? Guid.NewGuid(),
            TenantId: tenantId ?? DefaultTenantId);

    /// <summary>
    /// Builds a WorkflowInstanceDto with T2-compliant types:
    /// Id = Guid (PublicId), DefinitionId = long (internal FK), CurrentStateId = long (T1 FK).
    /// </summary>
    private static WorkflowInstanceDto FakeDto(Guid instancePublicId, long currentStateId) =>
        new(instancePublicId, 1L, "Case", Guid.NewGuid(), currentStateId,
            DefaultTenantId, DateTimeOffset.UtcNow, null, null, DateTimeOffset.UtcNow, []);

    // --- Tests ---

    [Fact]
    public async Task ExecuteTransitionAsync_HappyPath_DelegatesToMediatorAndReturnsSuccess()
    {
        var (instance, _, _, approved, transition) = SetupValidScenario(approvedIsFinal: true);
        var dto = FakeDto(instance.PublicId, approved.Id);
        _mediator.Send(Arg.Any<ExecuteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(dto));

        var outcome = await _sut.ExecuteTransitionAsync(BuildRequest(instance.PublicId));

        outcome.IsSuccess.Should().BeTrue();
        outcome.Result.Should().NotBeNull();
        outcome.Result!.NewStateName.Should().Be("Approved");
        outcome.Result.IsFinal.Should().BeTrue();

        // Verify mediator received the resolved TransitionId (long T1 PK)
        await _mediator.Received(1).Send(
            Arg.Is<ExecuteTransitionCommand>(c => c.TransitionId == transition.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteTransitionAsync_InstanceNotFound_ReturnsInstanceNotFoundCode()
    {
        var missingPublicId = Guid.NewGuid();
        _instanceRepo.GetWithHistoryAsync(missingPublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("WorkflowInstance not found."));

        var outcome = await _sut.ExecuteTransitionAsync(BuildRequest(missingPublicId));

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be(WorkflowErrorCodes.InstanceNotFound);
        await _mediator.DidNotReceiveWithAnyArgs()
            .Send(Arg.Any<ExecuteTransitionCommand>(), default);
    }

    [Fact]
    public async Task ExecuteTransitionAsync_CrossTenant_ReturnsInstanceNotFoundNotForbidden()
    {
        // Tenant-mismatch: don't leak existence — return NotFound, not Forbidden.
        var (instance, _, _, _, _) = SetupValidScenario();
        var request = BuildRequest(instance.PublicId, tenantId: Guid.NewGuid()); // DIFFERENT tenant

        var outcome = await _sut.ExecuteTransitionAsync(request);

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be(WorkflowErrorCodes.InstanceNotFound);
        await _mediator.DidNotReceiveWithAnyArgs()
            .Send(Arg.Any<ExecuteTransitionCommand>(), default);
    }

    [Fact]
    public async Task ExecuteTransitionAsync_DefinitionNotFound_ReturnsDefinitionNotFoundCode()
    {
        var (instance, definition, _, _, _) = SetupValidScenario();
        _definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("WorkflowDefinition not found."));

        var outcome = await _sut.ExecuteTransitionAsync(BuildRequest(instance.PublicId));

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be(WorkflowErrorCodes.DefinitionNotFound);
    }

    [Fact]
    public async Task ExecuteTransitionAsync_UnknownAction_ReturnsInvalidTransitionWithoutDispatch()
    {
        var (instance, _, _, _, _) = SetupValidScenario();

        var outcome = await _sut.ExecuteTransitionAsync(
            BuildRequest(instance.PublicId, actionName: "NonExistentAction"));

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be(WorkflowErrorCodes.InvalidTransition);
        // Early-return before mediator — save the dispatch cost.
        await _mediator.DidNotReceiveWithAnyArgs()
            .Send(Arg.Any<ExecuteTransitionCommand>(), default);
    }

    [Fact]
    public async Task ExecuteTransitionAsync_HandlerReturnsForbidden_OutcomeMapsToGovWfl004()
    {
        var (instance, _, _, _, _) = SetupValidScenario(); // S3 Slice 4: RPC removed from entity
        _mediator.Send(Arg.Any<ExecuteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<WorkflowInstanceDto>(
                WorkflowErrors.TransitionForbidden("Submit", "CASES.WORKFLOW.SUBMIT")));

        var outcome = await _sut.ExecuteTransitionAsync(BuildRequest(instance.PublicId));

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be(WorkflowErrorCodes.TransitionForbidden);
        outcome.ErrorMessage.Should().Contain("requires role");
    }

    // --- Phase-03: TryResolveInstanceForEntityAsync ---

    [Fact]
    public async Task TryResolveInstanceForEntityAsync_HappyPath_ReturnsInstancePublicId()
    {
        var (instance, _, _, _, _) = SetupValidScenario();
        _instanceRepo.GetActiveInstancesByEntityAsync("Case", instance.EntityId, Arg.Any<CancellationToken>())
            .Returns(new List<WorkflowInstance> { instance });

        var result = await _sut.TryResolveInstanceForEntityAsync(
            "Case", instance.EntityId, DefaultTenantId);

        // Returns PublicId (Guid AK) per T2 contract
        result.Should().Be(instance.PublicId);
    }

    [Fact]
    public async Task TryResolveInstanceForEntityAsync_NoInstance_ReturnsNull()
    {
        _instanceRepo.GetActiveInstancesByEntityAsync("Case", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<WorkflowInstance>());

        var result = await _sut.TryResolveInstanceForEntityAsync("Case", Guid.NewGuid(), DefaultTenantId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryResolveInstanceForEntityAsync_CrossTenant_ReturnsNull()
    {
        var (instance, _, _, _, _) = SetupValidScenario();
        _instanceRepo.GetActiveInstancesByEntityAsync("Case", instance.EntityId, Arg.Any<CancellationToken>())
            .Returns(new List<WorkflowInstance> { instance });

        // Query with DIFFERENT tenant — must not leak existence
        var result = await _sut.TryResolveInstanceForEntityAsync(
            "Case", instance.EntityId, tenantId: Guid.NewGuid());

        result.Should().BeNull();
    }

    // --- Phase-03: CreateInstanceByDefinitionKeyAsync ---

    [Fact]
    public async Task CreateInstanceByDefinitionKeyAsync_HappyPath_CreatesInstanceAtInitialState()
    {
        var (_, definition, draft, _, _) = SetupValidScenario();
        _definitionRepo.GetLatestByKeyAsync("CaseProcess", DefaultTenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        var result = await _sut.CreateInstanceByDefinitionKeyAsync(
            "CaseProcess", "Case", Guid.NewGuid(), DefaultTenantId, Guid.NewGuid());

        // InitialStateId is Guid.Empty per Phase-4b B4c (T1 state has no Guid AK; vestigial field)
        result.InitialStateId.Should().Be(Guid.Empty);
        // PHR-V2 P6 S4 / GATE-1 Path A: Stage + single SaveChangesAsync (nav-prop propagates IDENTITY).
        _instanceRepo.Received(1).Stage(
            Arg.Is<WorkflowInstance>(i => i.EntityType == "Case" && i.CurrentStateId == draft.Id));
        await _instanceRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateInstanceByDefinitionKeyAsync_UnknownKey_Throws()
    {
        _definitionRepo.GetLatestByKeyAsync("Unknown", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail("not found"));

        var act = async () => await _sut.CreateInstanceByDefinitionKeyAsync(
            "Unknown", "Case", Guid.NewGuid(), DefaultTenantId, Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // --- P7-FU1.2: TryGetDefinitionPublicIdAsync ---

    [Fact]
    public async Task TryGetDefinitionPublicIdAsync_HappyPath_ReturnsPublicId()
    {
        var (_, definition, _, _, _) = SetupValidScenario();

        var result = await _sut.TryGetDefinitionPublicIdAsync(definition.Id, DefaultTenantId);

        result.Should().Be(definition.PublicId);
    }

    [Fact]
    public async Task TryGetDefinitionPublicIdAsync_NotFound_ReturnsNull()
    {
        _definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(999L, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<WorkflowDefinition>("not found"));

        var result = await _sut.TryGetDefinitionPublicIdAsync(999L, DefaultTenantId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryGetDefinitionPublicIdAsync_CrossTenant_ReturnsNull()
    {
        var (_, definition, _, _, _) = SetupValidScenario();
        var otherTenantId = Guid.NewGuid();

        var result = await _sut.TryGetDefinitionPublicIdAsync(definition.Id, otherTenantId);

        result.Should().BeNull();
    }

    // --- Phase 5c: ValidateDefinitionGraphAsync SaveChanges wiring ---

    /// <summary>
    /// C-T1: valid graph → SaveChangesAsync called once (R3 canonical rewrites persisted).
    /// S5 META-L (c): assert Received(1).SaveChangesAsync BEFORE behavior assert on outcome.
    /// </summary>
    [Fact]
    public async Task ValidateDefinitionGraphAsync_ValidGraph_CallsSaveChangesOnce()
    {
        // Arrange
        var (_, definition, _, _, _) = SetupValidScenario();

        _definitionRepo.GetForValidationAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(WorkflowDefinition? Definition, IReadOnlyDictionary<long, OutgoingAssignment?> AssignmentByTransitionId)>((
                definition,
                new Dictionary<long, OutgoingAssignment?>())));

        _graphValidator.ValidateForActivateAsync(
                Arg.Any<IReadOnlyList<WorkflowState>>(),
                Arg.Any<IReadOnlyList<WorkflowTransition>>(),
                Arg.Any<IReadOnlyDictionary<long, OutgoingAssignment?>>(),
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<WorkflowParallelBranch>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(WorkflowGraphValidationReport.Ok()));

        _definitionRepo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));

        // Act
        var outcome = await _sut.ValidateDefinitionGraphAsync(definition.Id);

        // Assert — precondition: SaveChangesAsync called exactly once (Option A: persist on success).
        await _definitionRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Behavior: outcome is valid.
        outcome.IsValid.Should().BeTrue("valid graph → SaveChanges persists canonical rewrites");
    }

    /// <summary>
    /// C-T2: invalid graph → SaveChangesAsync NOT called (Option A: no persist on failure).
    /// S5 META-L (c): assert Received(0).SaveChangesAsync BEFORE behavior assert on outcome.
    /// </summary>
    [Fact]
    public async Task ValidateDefinitionGraphAsync_InvalidGraph_DoesNotCallSaveChanges()
    {
        // Arrange
        var (_, definition, _, _, _) = SetupValidScenario();

        _definitionRepo.GetForValidationAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(WorkflowDefinition? Definition, IReadOnlyDictionary<long, OutgoingAssignment?> AssignmentByTransitionId)>((
                definition,
                new Dictionary<long, OutgoingAssignment?>())));

        var violation = new GraphRuleViolation("R2", "Draft", "Submit", "Missing assignment.");
        _graphValidator.ValidateForActivateAsync(
                Arg.Any<IReadOnlyList<WorkflowState>>(),
                Arg.Any<IReadOnlyList<WorkflowTransition>>(),
                Arg.Any<IReadOnlyDictionary<long, OutgoingAssignment?>>(),
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<WorkflowParallelBranch>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(WorkflowGraphValidationReport.Fail([violation])));

        // Act
        var outcome = await _sut.ValidateDefinitionGraphAsync(definition.Id);

        // Assert — precondition: SaveChangesAsync NOT called (no persist on graph failure).
        await _definitionRepo.Received(0).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Behavior: outcome reports violations.
        outcome.IsValid.Should().BeFalse("invalid graph → no save, violations returned");
        outcome.Violations.Should().ContainSingle(v => v.Rule == "R2");
    }
}
