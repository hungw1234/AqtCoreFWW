using System.Reflection;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Clients;
using AqtCoreFW.Workflow.Infrastructure.Tests.Helpers;
using FluentAssertions;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Clients;

/// <summary>
/// Phase 5b A7 — first-task wiring mirror for <see cref="InProcessWorkflowModuleClient"/>.
///
/// Verifies BP-path (CreateInstanceAsync, used by StartBusinessProcessRunCommandHandler via
/// CreateInstanceByInternalIdAsync delegate) wires the same resolver → factory → SaveChanges
/// flow as the Application handler. Empty-pool surfaces as a Fail outcome (Q5b ratification —
/// WorkflowInstanceResult.IsSuccess=false + ErrorCode=GOV_WFL_014), NOT a thrown exception.
///
/// A7a — happy path: SpecificUser resolver result threads through factory + outcome IsSuccess.
/// A7b — empty pool blocks: outcome.IsSuccess=false + ErrorCode=GOV_WFL_014; no Stage, no Save.
/// </summary>
public sealed class InProcessWorkflowModuleClientFirstTaskTests
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

    private static long _idSeed;
    private static long NextId() => Interlocked.Increment(ref _idSeed);

    private static void AssignId(Entity e, long id) =>
        typeof(Entity)
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(e, id);

    public InProcessWorkflowModuleClientFirstTaskTests()
    {
        _instanceRepo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(1)));
        _instanceRepo.HasAmbientTransaction().Returns(false);
        _instanceRepo.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IWorkflowTransaction>()));

        _sut = new InProcessWorkflowModuleClient(
            _definitionRepo, _instanceRepo, _mediator,
            _gen,
            _graphValidator,
            _handlerResolver,
            _taskFactory,
            NullLogger<InProcessWorkflowModuleClient>.Instance);
    }

    private (WorkflowDefinition definition, WorkflowState initial) BuildDef(bool initialIsFinal = false)
    {
        var createdBy = Guid.NewGuid();
        var definition = WorkflowDefinition.Create(_gen, "Flow", "Desc", DefaultTenantId, createdBy);
        AssignId(definition, NextId());

        var initial = WorkflowState.Create(definition.Id, "Draft", "Nháp", "Draft",
            isInitial: true, isFinal: initialIsFinal, "#808080", 1);
        AssignId(initial, NextId());
        definition.AddState(initial);

        _definitionRepo.GetWithStatesAndTransitionsAsync(definition.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        return (definition, initial);
    }

    private static CreateWorkflowInstanceRequest BuildReq(Guid definitionPublicId) => new(
        DefinitionId: definitionPublicId,
        EntityType: "BPRun",
        EntityId: Guid.NewGuid(),
        TenantId: DefaultTenantId,
        CreatedBy: Guid.NewGuid());

    // ── A7a — happy path: SpecificUser resolver → factory + IsSuccess ──────────────

    [Fact]
    public async Task CreateInstanceAsync_NonFinalSpecificUser_ReturnsSuccessAndStagesFirstTask()
    {
        var expectedUser = Guid.NewGuid();
        var (definition, initial) = BuildDef(initialIsFinal: false);

        _definitionRepo.GetOutgoingWithAssignmentsAsync(initial.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new List<OutgoingAssignment>()));
        _handlerResolver.ResolveAsync(
                Arg.Any<WorkflowState>(),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.ToAssignee(expectedUser));

        HandlerResolution? capturedResolution = null;
        string? capturedTitle = null;
        _taskFactory.StageFirstOrNextTask(
                Arg.Any<WorkflowInstance>(), Arg.Do<string>(t => capturedTitle = t),
                Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Do<HandlerResolution>(r => capturedResolution = r))
            .Returns((WorkflowTask)null!);

        var outcome = await _sut.CreateInstanceAsync(BuildReq(definition.PublicId), CancellationToken.None);

        outcome.IsSuccess.Should().BeTrue("happy-path outcome must be success");
        outcome.ErrorCode.Should().BeNull();
        outcome.InstanceId.Should().NotBe(Guid.Empty);
        outcome.InitialStateInternalId.Should().Be(initial.Id);

        // Anti-tautology: resolver Received(1), factory Received(1) with correct thread
        await _handlerResolver.Received(1).ResolveAsync(
            Arg.Any<WorkflowState>(),
            Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
            Arg.Any<WorkflowResolutionContext>(),
            Arg.Any<CancellationToken>());
        _taskFactory.Received(1).StageFirstOrNextTask(
            Arg.Any<WorkflowInstance>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid?>(), Arg.Any<HandlerResolution>());
        capturedResolution!.Assignee.Should().Be(expectedUser);
        capturedTitle.Should().Be(initial.DisplayNameVi, "Q3 ratification: title = DisplayNameVi");

        // PHR-V2 P6 S4 / GATE-1 Path A: single SaveChangesAsync (nav-prop propagates IDENTITY).
        _instanceRepo.Received(1).Stage(Arg.Any<WorkflowInstance>());
        await _instanceRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── A7b — empty pool blocks: Fail outcome + zero side effects ─────────────────

    [Fact]
    public async Task CreateInstanceAsync_NonFinalEmptyPool_ReturnsFailOutcomeWith_GOV_WFL_014()
    {
        var (definition, initial) = BuildDef(initialIsFinal: false);

        _definitionRepo.GetOutgoingWithAssignmentsAsync(initial.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new List<OutgoingAssignment>()));
        _handlerResolver.ResolveAsync(
                Arg.Any<WorkflowState>(),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Empty);

        var outcome = await _sut.CreateInstanceAsync(BuildReq(definition.PublicId), CancellationToken.None);

        outcome.IsSuccess.Should().BeFalse("empty pool at non-final initial state must hard-block");
        outcome.ErrorCode.Should().Be(WorkflowErrorCodes.NoHandlerResolved,
            "Q5b: contract surfaces GOV_WFL_014 not exception");
        outcome.InstanceId.Should().Be(Guid.Empty, "fail outcome zeroes the InstanceId");

        // Atomicity: no orphan staging or save
        _instanceRepo.DidNotReceive().Stage(Arg.Any<WorkflowInstance>());
        _taskFactory.DidNotReceive().StageFirstOrNextTask(
            Arg.Any<WorkflowInstance>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid?>(), Arg.Any<HandlerResolution>());
        await _instanceRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── A7c — final initial state: degenerate, no resolver / no factory ────────────

    [Fact]
    public async Task CreateInstanceAsync_FinalInitialState_SkipsResolverAndFactory()
    {
        var (definition, initial) = BuildDef(initialIsFinal: true);

        var outcome = await _sut.CreateInstanceAsync(BuildReq(definition.PublicId), CancellationToken.None);

        outcome.IsSuccess.Should().BeTrue();
        outcome.InitialStateInternalId.Should().Be(initial.Id);

        await _handlerResolver.DidNotReceive().ResolveAsync(
            Arg.Any<WorkflowState>(),
            Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
            Arg.Any<WorkflowResolutionContext>(),
            Arg.Any<CancellationToken>());
        _taskFactory.DidNotReceive().StageFirstOrNextTask(
            Arg.Any<WorkflowInstance>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid?>(), Arg.Any<HandlerResolution>());

        // A7c final-initial degenerate: only the instance flush — no task to stage.
        _instanceRepo.Received(1).Stage(Arg.Any<WorkflowInstance>());
        await _instanceRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
