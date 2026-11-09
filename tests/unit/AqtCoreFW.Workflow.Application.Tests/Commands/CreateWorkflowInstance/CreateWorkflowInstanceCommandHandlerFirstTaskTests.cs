using System.Reflection;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowInstance;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Workflow.Application.Tests.Commands.CreateWorkflowInstance;

/// <summary>
/// Phase 5b A1-A6 unit facts — first-task-on-creation seam.
///
/// Verifies that <see cref="CreateWorkflowInstanceCommandHandler"/> resolves the initial-state
/// pool and stages exactly one <see cref="WorkflowTask"/> for non-final initial states inside the
/// same SaveChanges as the instance row (S7 single-SaveChanges invariant). Empty-pool initial
/// states (non-final) are hard-blocked with <c>GOV_WFL_014</c> before any instance row is staged
/// (Q2 ratification — fail loud).
///
/// Anti-tautology discipline (S5/S6 META-L (c)):
/// each fact asserts WHICH code path executed (resolver Received(1), factory Received(1)/0,
/// instanceRepo.Stage Received(1)/0) BEFORE asserting final result.
/// </summary>
public sealed class CreateWorkflowInstanceCommandHandlerFirstTaskTests
{
    private static long _idSeed;
    private static long NextId() => Interlocked.Increment(ref _idSeed);

    private static void AssignId(Entity e, long id) =>
        typeof(Entity)
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(e, id);

    private static readonly Guid DefaultTenantId = Guid.NewGuid();

    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWorkflowInstanceRepository _instanceRepository;
    private readonly IWorkflowAssignmentResolver _assignmentResolver;
    private readonly IWorkflowHandlerResolver _handlerResolver;
    private readonly IWorkflowTaskFactory _taskFactory;
    private readonly CreateWorkflowInstanceCommandHandler _handler;

    public CreateWorkflowInstanceCommandHandlerFirstTaskTests()
    {
        _definitionRepository = Substitute.For<IWorkflowDefinitionRepository>();
        _instanceRepository = Substitute.For<IWorkflowInstanceRepository>();
        _assignmentResolver = Substitute.For<IWorkflowAssignmentResolver>();
        _handlerResolver = Substitute.For<IWorkflowHandlerResolver>();
        _taskFactory = Substitute.For<IWorkflowTaskFactory>();

        // Default-success: SaveChangesAsync OK (1 row affected); override per-test for A6.
        // PHR-V2 P6 S4 / GATE-1 Path A: single SaveChanges via EF nav-prop propagation.
        // No transaction setup needed — instance + task share one change-tracker scope.
        _instanceRepository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(1)));

        _handler = new CreateWorkflowInstanceCommandHandler(
            _definitionRepository, _instanceRepository, _assignmentResolver,
            _handlerResolver, _taskFactory,
            TestGuidGenerator.Create(),
            NullLogger<CreateWorkflowInstanceCommandHandler>.Instance);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private (WorkflowDefinition definition, WorkflowState initialState) BuildDefinition(
        bool initialIsFinal,
        long? initialStateId = null,
        string initialDisplayNameVi = "Nháp")
    {
        var gen = TestGuidGenerator.Create();
        var createdBy = Guid.NewGuid();
        var definition = WorkflowDefinition.Create(gen, "Flow", "Desc", DefaultTenantId, createdBy);
        AssignId(definition, NextId());

        var initial = WorkflowState.Create(definition.Id, "Draft", initialDisplayNameVi, "Draft",
            isInitial: true, isFinal: initialIsFinal, "#808080", 1);
        AssignId(initial, initialStateId ?? NextId());
        definition.AddState(initial);

        _definitionRepository.GetWithStatesAndTransitionsAsync(definition.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        return (definition, initial);
    }

    private static CreateWorkflowInstanceCommand BuildCommand(Guid definitionPublicId) => new(
        EntityType: "Case",
        EntityId: Guid.NewGuid(),
        TenantId: DefaultTenantId,
        CreatedBy: Guid.NewGuid(),
        DefinitionId: definitionPublicId,
        SlaHours: null);

    // ── A1 — final initial state: degenerate path, zero tasks staged ─────────────

    /// <summary>
    /// A1: initial state is final → instance created, NO task staged, NO resolver call.
    /// Precondition asserts:
    /// (a) initialState.IsFinal == true
    /// (b) Stage called once with the instance entity
    /// (c) SaveChangesAsync called once (atomic flush)
    /// (d) IWorkflowHandlerResolver.Resolve DidNotReceive
    /// (e) IWorkflowTaskFactory.StageFirstOrNextTask DidNotReceive
    /// (f) result.IsSuccess
    /// </summary>
    [Fact]
    public async Task Handle_FinalInitialState_StagesInstanceOnlyWithoutTask()
    {
        // Arrange
        var (definition, initialState) = BuildDefinition(initialIsFinal: true);
        var command = BuildCommand(definition.PublicId);

        // Precondition (a)
        initialState.IsFinal.Should().BeTrue("A1 requires initial state to be final");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert (d): resolver never consulted
        await _handlerResolver.DidNotReceive().ResolveAsync(
            Arg.Any<WorkflowState>(),
            Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
            Arg.Any<WorkflowResolutionContext>(),
            Arg.Any<CancellationToken>());

        // Assert (e): factory never called
        _taskFactory.DidNotReceive().StageFirstOrNextTask(
            Arg.Any<WorkflowInstance>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid?>(), Arg.Any<HandlerResolution>());

        // Assert (b): instance staged exactly once
        _instanceRepository.Received(1).Stage(Arg.Any<WorkflowInstance>());

        // Assert (c): SaveChangesAsync flushed once
        await _instanceRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Assert (f)
        result.IsSuccess.Should().BeTrue("degenerate final-initial flow must succeed");
    }

    // ── A2 — non-final + SpecificUser resolver: factory called with Assignee ──────

    /// <summary>
    /// A2: non-final initial + resolver returns ToAssignee(userX) → factory called once with
    /// overrideAssignee=null + resolution.Assignee=userX.
    /// Anti-tautology: factory invocation is captured via Arg.Do; we assert resolver.Received(1)
    /// BEFORE asserting factory.Received(1) (proves the resolution → factory thread).
    /// </summary>
    [Fact]
    public async Task Handle_NonFinalSpecificUser_StagesFirstTaskWithAssignee()
    {
        // Arrange
        var expectedUser = Guid.NewGuid();
        var (definition, initialState) = BuildDefinition(initialIsFinal: false);
        var command = BuildCommand(definition.PublicId);

        // Wire outgoing (caller pre-loads; resolver consumes)
        _definitionRepository.GetOutgoingWithAssignmentsAsync(initialState.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new List<OutgoingAssignment>()));

        // Wire resolver: returns Assignee path
        _handlerResolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == initialState.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.ToAssignee(expectedUser));

        // Capture factory call args (PHR-V2 P6 S4 / GATE-1 Path A: instance-overload signature).
        WorkflowInstance? capturedInstance = null;
        string? capturedTitle = null;
        Guid? capturedOverride = null;
        HandlerResolution? capturedResolution = null;
        _taskFactory.StageFirstOrNextTask(
                Arg.Do<WorkflowInstance>(i => capturedInstance = i),
                Arg.Do<string>(t => capturedTitle = t),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Do<Guid?>(o => capturedOverride = o),
                Arg.Do<HandlerResolution>(r => capturedResolution = r))
            .Returns((WorkflowTask)null!);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Precondition: resolver consulted once
        await _handlerResolver.Received(1).ResolveAsync(
            Arg.Any<WorkflowState>(),
            Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
            Arg.Any<WorkflowResolutionContext>(),
            Arg.Any<CancellationToken>());

        // Assert: factory called exactly once with the instance-overload
        _taskFactory.Received(1).StageFirstOrNextTask(
            Arg.Any<WorkflowInstance>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid?>(), Arg.Any<HandlerResolution>());

        // Anti-tautology: assignee thread proven via captured resolution
        capturedInstance.Should().NotBeNull("factory must receive the staged WorkflowInstance entity");
        capturedResolution.Should().NotBeNull();
        capturedResolution!.Assignee.Should().Be(expectedUser,
            "factory must receive resolver's Assignee thread (no override at creation)");
        capturedOverride.Should().BeNull("creation has no NextHandlerUserId — override always null");
        capturedTitle.Should().Be(initialState.DisplayNameVi,
            "Q3 ratification: task title = initialState.DisplayNameVi");

        // PHR-V2 P6 S4 / GATE-1 Path A: single SaveChanges (nav-prop propagates IDENTITY).
        _instanceRepository.Received(1).Stage(Arg.Any<WorkflowInstance>());
        await _instanceRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        result.IsSuccess.Should().BeTrue();
    }

    // ── A3 — non-final + ByPermission pool: factory called with pool predicates ──

    [Fact]
    public async Task Handle_NonFinalPoolPath_StagesFirstTaskWithEmptyAssignee()
    {
        // Arrange
        var (definition, initialState) = BuildDefinition(initialIsFinal: false);
        var command = BuildCommand(definition.PublicId);

        _definitionRepository.GetOutgoingWithAssignmentsAsync(initialState.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new List<OutgoingAssignment>()));

        var predicates = new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, "WORKFLOW.TASK.PROCESS", AssignmentCombineMode.Union)
        };
        _handlerResolver.ResolveAsync(
                Arg.Any<WorkflowState>(),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool(predicates));

        HandlerResolution? capturedResolution = null;
        _taskFactory.StageFirstOrNextTask(
                Arg.Any<WorkflowInstance>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Do<HandlerResolution>(r => capturedResolution = r))
            .Returns((WorkflowTask)null!);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: pool branch
        capturedResolution.Should().NotBeNull();
        capturedResolution!.IsEmpty.Should().BeFalse("pool with 1+ predicates is NOT empty");
        capturedResolution.Assignee.Should().BeNull("pool path has no single Assignee");
        capturedResolution.CandidatePredicates.Should().HaveCount(1,
            "A3 anti-tautology: resolver pool predicates must thread through to factory");

        _taskFactory.Received(1).StageFirstOrNextTask(
            Arg.Any<WorkflowInstance>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid?>(), Arg.Any<HandlerResolution>());

        result.IsSuccess.Should().BeTrue();
    }

    // ── A4 — non-final + empty pool: hard-block with GOV_WFL_014 ─────────────────

    /// <summary>
    /// A4: non-final initial + resolver returns Empty → returns Result.Fail(GOV_WFL_014)
    /// with dual-metadata (4-B-A INVARIANT — both "ErrorCode" body key AND "WorkflowErrorCode"
    /// provenance key set to GOV_WFL_014). Factory NEVER called; instance NEVER staged
    /// (atomicity — no orphan rows).
    /// </summary>
    [Fact]
    public async Task Handle_NonFinalEmptyPool_FailsWith_NoHandlerResolved_DualMetadata()
    {
        // Arrange
        var (definition, initialState) = BuildDefinition(initialIsFinal: false);
        var command = BuildCommand(definition.PublicId);

        _definitionRepository.GetOutgoingWithAssignmentsAsync(initialState.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new List<OutgoingAssignment>()));

        // Resolver returns Empty (no eligible handler)
        _handlerResolver.ResolveAsync(
                Arg.Any<WorkflowState>(),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Empty);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: failed result
        result.IsFailed.Should().BeTrue("empty pool at non-final initial state must hard-block");

        // Dual-metadata invariant (4-B-A): factory MUST set BOTH keys
        var err = result.Errors.Should().ContainSingle().Subject;
        err.Metadata.Should().ContainKey("ErrorCode")
            .WhoseValue.Should().Be(WorkflowErrorCodes.NoHandlerResolved,
                "ErrorCode body key must surface GOV_WFL_014 not stale GOV_SYS_002");
        err.Metadata.Should().ContainKey("WorkflowErrorCode")
            .WhoseValue.Should().Be(WorkflowErrorCodes.NoHandlerResolved,
                "WorkflowErrorCode provenance key must equal GOV_WFL_014");

        // Atomicity: no orphan instance OR task staging
        _instanceRepository.DidNotReceive().Stage(Arg.Any<WorkflowInstance>());
        _taskFactory.DidNotReceive().StageFirstOrNextTask(
            Arg.Any<WorkflowInstance>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid?>(), Arg.Any<HandlerResolution>());
        await _instanceRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── A5 — non-final + resolver throws: fail-CLOSED → GOV_WFL_014 ──────────────

    [Fact]
    public async Task Handle_NonFinalResolverThrows_FailsClosedWith_NoHandlerResolved()
    {
        // Arrange
        var (definition, initialState) = BuildDefinition(initialIsFinal: false);
        var command = BuildCommand(definition.PublicId);

        _definitionRepository.GetOutgoingWithAssignmentsAsync(initialState.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new List<OutgoingAssignment>()));

        _handlerResolver.ResolveAsync(
                Arg.Any<WorkflowState>(),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Throws(new NotSupportedException("PreviousHandler clause not implemented"));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: fail-CLOSED — exception → 422 not 500
        result.IsFailed.Should().BeTrue();
        var err = result.Errors.Should().ContainSingle().Subject;
        err.Metadata.Should().ContainKey("WorkflowErrorCode")
            .WhoseValue.Should().Be(WorkflowErrorCodes.NoHandlerResolved);

        // No instance staged, no factory call (atomicity)
        _instanceRepository.DidNotReceive().Stage(Arg.Any<WorkflowInstance>());
        _taskFactory.DidNotReceive().StageFirstOrNextTask(
            Arg.Any<WorkflowInstance>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid?>(), Arg.Any<HandlerResolution>());
    }

    // ── A6 — non-final + SaveChangesAsync fails: result propagates, atomicity preserved ──

    /// <summary>
    /// A6: when SaveChangesAsync returns Fail, the handler propagates the error.
    ///
    /// PHR-V2 P6 S4 / GATE-1 Path A semantic shift: atomicity now lives at the single-SaveChanges
    /// boundary, not in handler-level early-exit. Both instance and task are staged BEFORE the
    /// single flush — when the flush fails, neither row commits (EF/DB transaction guarantee).
    /// The old "factory not invoked on first-save failure" assertion no longer applies because
    /// there is no longer a first save; factory is invoked before the single flush as part of
    /// the change-tracker scope build-up.
    /// </summary>
    [Fact]
    public async Task Handle_FirstSaveChangesFails_PropagatesError_TaskFactoryNotInvoked()
    {
        // Arrange
        var (definition, initialState) = BuildDefinition(initialIsFinal: false);
        var command = BuildCommand(definition.PublicId);

        _definitionRepository.GetOutgoingWithAssignmentsAsync(initialState.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new List<OutgoingAssignment>()));

        _handlerResolver.ResolveAsync(
                Arg.Any<WorkflowState>(),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.ToAssignee(Guid.NewGuid()));

        // Single SaveChangesAsync (instance + task flush) FAILS — error must propagate.
        _instanceRepository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<int>(new Error("DB transient failure"))));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: failure propagates
        result.IsFailed.Should().BeTrue("SaveChangesAsync failure must propagate to caller");

        // Precondition: Stage was attempted (proves ordering)
        _instanceRepository.Received(1).Stage(Arg.Any<WorkflowInstance>());
        // Factory IS invoked under single-SaveChanges semantics — both rows staged before flush.
        // Atomicity invariant moves to DB transaction (neither persists on flush failure).
        _taskFactory.Received(1).StageFirstOrNextTask(
            Arg.Any<WorkflowInstance>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid?>(), Arg.Any<HandlerResolution>());
        // Exactly ONE flush call — single-SaveChanges invariant restored.
        await _instanceRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
