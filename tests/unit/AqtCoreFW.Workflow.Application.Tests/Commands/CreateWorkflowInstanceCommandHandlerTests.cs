using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowInstance;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateWorkflowInstanceCommandHandler.
/// Validates: definition resolution, initial-state lookup, SLA wiring, persistence, and DTO mapping.
/// Phase 5b: ctor expanded with IWorkflowHandlerResolver + IWorkflowTaskFactory + ILogger.
/// First-task wiring tested separately in CreateWorkflowInstance/CreateWorkflowInstanceCommandHandlerFirstTaskTests.cs.
/// </summary>
public sealed class CreateWorkflowInstanceCommandHandlerTests
{
    private static readonly Guid DefaultTenantId = Guid.NewGuid();

    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWorkflowInstanceRepository _instanceRepository;
    private readonly IWorkflowAssignmentResolver _assignmentResolver;
    private readonly IWorkflowHandlerResolver _handlerResolver;
    private readonly IWorkflowTaskFactory _taskFactory;
    private readonly CreateWorkflowInstanceCommandHandler _handler;

    public CreateWorkflowInstanceCommandHandlerTests()
    {
        _definitionRepository = Substitute.For<IWorkflowDefinitionRepository>();
        _instanceRepository = Substitute.For<IWorkflowInstanceRepository>();
        _assignmentResolver = Substitute.For<IWorkflowAssignmentResolver>();
        _handlerResolver = Substitute.For<IWorkflowHandlerResolver>();
        _taskFactory = Substitute.For<IWorkflowTaskFactory>();

        // Default SaveChangesAsync OK so legacy happy-path tests don't fail on the new flush.
        _instanceRepository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(1)));
        _instanceRepository.HasAmbientTransaction().Returns(false);
        _instanceRepository.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IWorkflowTransaction>()));

        // Default handler-resolver returns ToAssignee so non-final initial states resolve a pool.
        // Tests that assert NoInitialState / definition-not-found short-circuit before resolver call.
        _handlerResolver.ResolveAsync(
                Arg.Any<WorkflowState>(),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.ToAssignee(Guid.NewGuid()));

        _handler = new CreateWorkflowInstanceCommandHandler(
            _definitionRepository, _instanceRepository, _assignmentResolver,
            _handlerResolver, _taskFactory,
            TestGuidGenerator.Create(),
            NullLogger<CreateWorkflowInstanceCommandHandler>.Instance);
    }

    // --- Helpers ---

    /// <summary>
    /// Creates a WorkflowDefinition with one initial state and optionally a final state,
    /// then wires the repository mock to return it.
    /// Returns (definition, initialState) for assertion use.
    /// </summary>
    private (WorkflowDefinition definition, WorkflowState initialState) SetupDefinitionWithInitialState(
        bool includeInitialState = true)
    {
        var gen = TestGuidGenerator.Create();
        var createdBy = Guid.NewGuid();
        var definition = WorkflowDefinition.Create(gen, "Flow", "Desc", DefaultTenantId, createdBy);

        WorkflowState? initial = null;
        if (includeInitialState)
        {
            initial = WorkflowState.Create(definition.Id, "Draft", "Nháp", "Draft",
                isInitial: true, isFinal: false, "#808080", 1);
            definition.AddState(initial);
        }

        var finalState = WorkflowState.Create(definition.Id, "Approved", "Đã duyệt", "Approved",
            isInitial: false, isFinal: true, "#00FF00", 2);
        definition.AddState(finalState);

        _definitionRepository.GetWithStatesAndTransitionsAsync(definition.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        return (definition, initial!);
    }

    private static CreateWorkflowInstanceCommand BuildCommand(Guid definitionPublicId, int? slaHours = null) => new(
        EntityType: "Case",
        EntityId: Guid.NewGuid(),
        TenantId: DefaultTenantId,
        CreatedBy: Guid.NewGuid(),
        DefinitionId: definitionPublicId,
        SlaHours: slaHours);

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidDefinition_ReturnsSuccessWithDto()
    {
        var (definition, _) = SetupDefinitionWithInitialState();
        var command = BuildCommand(definition.PublicId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DefinitionId.Should().Be(definition.Id);
        result.Value.EntityType.Should().Be("Case");
    }

    [Fact]
    public async Task Handle_ValidDefinition_SetsInitialState()
    {
        var (definition, initialState) = SetupDefinitionWithInitialState();
        var command = BuildCommand(definition.PublicId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.CurrentStateId.Should().Be(initialState.Id);
    }

    [Fact]
    public async Task Handle_WithSlaHours_SetsDueAt()
    {
        var (definition, _) = SetupDefinitionWithInitialState();
        var command = BuildCommand(definition.PublicId, slaHours: 48);
        var before = DateTimeOffset.UtcNow;

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.DueAt.Should().NotBeNull();
        result.Value.DueAt!.Value.Should().BeOnOrAfter(before.AddHours(48));
    }

    [Fact]
    public async Task Handle_PersistsInstanceViaRepository()
    {
        var (definition, _) = SetupDefinitionWithInitialState();
        var command = BuildCommand(definition.PublicId);

        await _handler.Handle(command, CancellationToken.None);

        // PHR-V2 P6 S4 / GATE-1 Path A: Stage + single SaveChangesAsync (nav-prop propagates
        // IDENTITY-generated instance.Id to task.WorkflowInstanceId atomically).
        _instanceRepository.Received(1).Stage(
            Arg.Is<WorkflowInstance>(i => i.DefinitionId == definition.Id));
        await _instanceRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Failure path ---

    [Fact]
    public async Task Handle_DefinitionNotFound_ReturnsError()
    {
        var missingId = Guid.NewGuid();
        _definitionRepository.GetWithStatesAndTransitionsAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail($"WorkflowDefinition '{missingId}' not found."));

        var command = BuildCommand(missingId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_NoInitialState_ReturnsNoInitialStateError()
    {
        var (definition, _) = SetupDefinitionWithInitialState(includeInitialState: false);
        var command = BuildCommand(definition.PublicId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("initial state"));
    }

    [Fact]
    public async Task Handle_DefinitionNotFound_DoesNotCallInstanceRepository()
    {
        var missingId = Guid.NewGuid();
        _definitionRepository.GetWithStatesAndTransitionsAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail($"WorkflowDefinition '{missingId}' not found."));

        var command = BuildCommand(missingId);

        await _handler.Handle(command, CancellationToken.None);

        // Phase 5b: atomicity — neither Stage nor SaveChanges should fire on definition-not-found.
        _instanceRepository.DidNotReceive().Stage(Arg.Any<WorkflowInstance>());
        await _instanceRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
