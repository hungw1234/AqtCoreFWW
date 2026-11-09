using AqtCoreFW.Workflow.Application.Commands.CloneWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;
using System.Reflection;
using System.Transactions;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for CloneWorkflowDefinitionCommandHandler.
/// Validates: cloning with version increment, source marking as not-latest,
/// deep copy of states/transitions, persistence order, and error handling.
/// </summary>
public sealed class CloneWorkflowDefinitionCommandHandlerTests
{
    private static readonly Guid DefaultTenantId = Guid.NewGuid();
    private static readonly Guid DefaultSourceId = Guid.NewGuid();

    private readonly IWorkflowDefinitionRepository _repository;
    private readonly CloneWorkflowDefinitionCommandHandler _handler;

    public CloneWorkflowDefinitionCommandHandlerTests()
    {
        _repository = Substitute.For<IWorkflowDefinitionRepository>();

        // Simulate EF IDENTITY assignment on clone states: AddAsync is only called for the clone,
        // so assigning IDs to all states of the argument is safe. Without this, clone state IDs
        // remain 0 and WorkflowTransition.Create's fromStateId > 0 guard throws.
        _repository
            .When(r => r.AddAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()))
            .Do(ci =>
            {
                var def = ci.Arg<WorkflowDefinition>();
                foreach (var state in def.States)
                    AssignTestId(state);
            });

        // The handler loads the per-transition assignment map (GetForValidationAsync), the parallel-branch
        // graph (GetParallelBranchesForCloneAsync), and the max existing version (GetMaxVersionByKeyAsync)
        // to drive the deep-copy + version computation. Default them to the empty / single-version happy
        // path so every test runs deterministically — NSubstitute auto-values for these (notably the
        // tuple's dictionary) differ between isolated and full-suite runs, which made Handle intermittently
        // throw NullReferenceException inside the graph projection.
        _repository.GetForValidationAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(((WorkflowDefinition?)null,
                (IReadOnlyDictionary<long, OutgoingAssignment?>)new Dictionary<long, OutgoingAssignment?>()));
        _repository.GetParallelBranchesForCloneAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowParallelBranch>)Array.Empty<WorkflowParallelBranch>());
        // All test sources are version 1 and the only row for their key → MAX = 1 → clone is version 2.
        _repository.GetMaxVersionByKeyAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(1);

        _handler = new CloneWorkflowDefinitionCommandHandler(_repository, TestGuidGenerator.Create());
    }

    // --- Helpers ---

    private static long _idSeed;

    /// <summary>Assigns a unique test Id to a T1 entity via reflection (bypasses EF DB-assignment).</summary>
    private static long AssignTestId(AqtCoreFW.SharedKernel.Domain.Entity entity)
    {
        var id = Interlocked.Increment(ref _idSeed);
        typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(entity, id);
        return id;
    }

    /// <summary>Builds a valid clone command.</summary>
    private static CloneWorkflowDefinitionCommand BuildValidCommand(
        Guid? sourceId = null) => new(
        SourceId: sourceId ?? DefaultSourceId,
        TenantId: DefaultTenantId,
        CreatedBy: Guid.NewGuid());

    /// <summary>Creates a definition with states and transitions for cloning.</summary>
    private static WorkflowDefinition CreateSourceDefinition(Guid? id = null)
    {
        var sourceId = id ?? DefaultSourceId;
        var def = WorkflowDefinition.Create(
            TestGuidGenerator.Create(), "Source Workflow", "Source Description", DefaultTenantId, Guid.NewGuid());

        // Add states and transitions to test deep-copy
        // Assign unique test IDs so clone ID-differentiation assertions work in unit tests
        var s1 = WorkflowState.Create(def.Id, "Draft", "Nháp", "Draft", true, false, "#808080", 1);
        AssignTestId(s1);
        var s2 = WorkflowState.Create(def.Id, "InReview", "Đang xét", "In Review", false, false, "#FFAA00", 2);
        AssignTestId(s2);
        var s3 = WorkflowState.Create(def.Id, "Approved", "Đã duyệt", "Approved", false, true, "#00FF00", 3);
        AssignTestId(s3);

        def.AddState(s1);
        def.AddState(s2);
        def.AddState(s3);

        var t1 = WorkflowTransition.Create(def.Id, s1.Id, s2.Id,
            "Submit", "Gửi", "Submit", null, 1);
        AssignTestId(t1);
        var t2 = WorkflowTransition.Create(def.Id, s2.Id, s3.Id,
            "Approve", "Phê duyệt", "Approve", null, 2);
        AssignTestId(t2);

        def.AddTransition(t1);
        def.AddTransition(t2);

        return def;
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ClonesDefinition()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidCommand_CloneHasIncrementedVersion()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));
        // Clone version = MAX(version) over all rows for the key (incl. soft-deleted) + 1. Stub a
        // non-trivial MAX so the assertion proves the handler derives the version from the repo lookup,
        // not from source.Version coinciding with the default mock.
        _repository.GetMaxVersionByKeyAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(7);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Version.Should().Be(8);
    }

    [Fact]
    public async Task Handle_ValidCommand_CloneIsLatest()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.IsLatest.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidCommand_ClonePreservesName()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Name.Should().Be("Source Workflow");
    }

    [Fact]
    public async Task Handle_ValidCommand_ClonePreservesDefinitionKey()
    {
        var command = BuildValidCommand();
        var source = WorkflowDefinition.Create(
            TestGuidGenerator.Create(), "Workflow", "Desc", DefaultTenantId, Guid.NewGuid(),
            definitionKey: "stable-workflow-key");

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.DefinitionKey.Should().Be("stable-workflow-key");
    }

    [Fact]
    public async Task Handle_ValidCommand_CloneHasDeepCopiedStates()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        await _handler.Handle(command, CancellationToken.None);

        // Deep-copy is delegated to the canonical ReplaceGraphAsync path: the handler hands it fresh
        // clone-state entities re-projected from the source's 3 states. The clone shell itself is cleared
        // before that call and repopulated by it, so assert on the delegated states, not the in-memory shell.
        await _repository.Received(1).ReplaceGraphAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Is<List<WorkflowState>>(states =>
                states.Count == 3 &&
                states.Select(s => s.Name).OrderBy(n => n)
                    .SequenceEqual(new[] { "Approved", "Draft", "InReview" })),
            Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_CloneHasDeepCopiedTransitions()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        await _handler.Handle(command, CancellationToken.None);

        // The 2 source transitions are projected into name-keyed specs and handed to ReplaceGraphAsync;
        // state FKs travel as stable state Names (re-resolved to the clone's fresh ids after states persist).
        await _repository.Received(1).ReplaceGraphAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<List<WorkflowState>>(),
            Arg.Is<IReadOnlyList<NewTransitionSpec>>(specs =>
                specs.Count == 2 &&
                specs.Any(s => s.ActionName == "Submit" && s.FromStateName == "Draft" && s.ToStateName == "InReview") &&
                specs.Any(s => s.ActionName == "Approve" && s.FromStateName == "InReview" && s.ToStateName == "Approved")),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_MarksSourceNotLatest()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);
        source.IsLatest.Should().BeTrue();  // Initially latest

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        await _handler.Handle(command, CancellationToken.None);

        // After cloning, source should be marked not-latest
        source.IsLatest.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsSourceUpdateFirst()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        await _handler.Handle(command, CancellationToken.None);

        // UpdateAsync should be called for the source
        await _repository.Received(1).UpdateAsync(
            Arg.Is<WorkflowDefinition>(d => d.Id == source.Id && !d.IsLatest),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsCloneViaAddAsync()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        await _handler.Handle(command, CancellationToken.None);

        // AddAsync should be called for the clone (a different object: not source, IsLatest, Version+1)
        await _repository.Received(1).AddAsync(
            Arg.Is<WorkflowDefinition>(d =>
                !ReferenceEquals(d, source) &&
                d.IsLatest &&
                d.Version == source.Version + 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistenceOrderIsCorrect()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        await _handler.Handle(command, CancellationToken.None);

        // Persistence order: supersede source (UpdateAsync) → persist clone shell (AddAsync, assigns clone.Id)
        // → rebuild the full clone graph via the canonical ReplaceGraphAsync.
        Received.InOrder(async () =>
        {
            await _repository.UpdateAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>());
            await _repository.AddAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>());
            await _repository.ReplaceGraphAsync(
                Arg.Any<WorkflowDefinition>(), Arg.Any<List<WorkflowState>>(),
                Arg.Any<IReadOnlyList<NewTransitionSpec>>(), Arg.Any<IReadOnlyList<NewBranchSpec>>(),
                Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        });
    }

    // PHASE4B-WF-FU2: ambient TransactionScope must enclose both repo writes so a mid-sequence
    // failure rolls the source IsLatest=false flip back. Captures Transaction.Current at the
    // moment the repo is invoked — proves the scope is active during persistence calls.
    [Fact]
    public async Task Handle_ValidCommand_PersistenceCallsRunInsideAmbientTransactionScope()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        Transaction? updateAmbient = null;
        Transaction? addAmbient = null;

        _repository
            .When(r => r.UpdateAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()))
            .Do(_ => updateAmbient = Transaction.Current);
        _repository
            .When(r => r.AddAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()))
            .Do(_ => addAmbient = Transaction.Current);

        await _handler.Handle(command, CancellationToken.None);

        updateAmbient.Should().NotBeNull("UpdateAsync must run inside an ambient TransactionScope");
        addAmbient.Should().NotBeNull("AddAsync must run inside an ambient TransactionScope");
        addAmbient.Should().BeSameAs(updateAmbient,
            "both writes must enlist in the same ambient transaction so a mid-sequence failure rolls back atomically");
    }

    [Fact]
    public async Task Handle_AddAsyncThrows_AmbientScopeNotCompleted()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        TransactionStatus? observedStatus = null;
        _repository
            .When(r => r.AddAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                // Capture status while ambient txn is still alive, then throw
                observedStatus = Transaction.Current?.TransactionInformation.Status;
                throw new InvalidOperationException("simulated DB failure on clone insert");
            });

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        observedStatus.Should().Be(TransactionStatus.Active,
            "scope.Complete() must not have been called when AddAsync throws — txn aborts on dispose");
    }

    // --- Error scenarios ---

    [Fact]
    public async Task Handle_SourceNotFound_ReturnsNotFoundError()
    {
        var command = BuildValidCommand();

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("Not found"));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_SourceNotFound_DoesNotCallUpdateOrAdd()
    {
        var command = BuildValidCommand();

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("Not found"));

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().AddAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<CancellationToken>());
    }

    // --- Edge cases ---

    [Fact]
    public async Task Handle_EmptyStatesList_CloneHasEmptyStates()
    {
        var command = BuildValidCommand();
        var source = WorkflowDefinition.Create(
            TestGuidGenerator.Create(), "Empty Workflow", "No states", DefaultTenantId, Guid.NewGuid());

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.States.Should().BeEmpty();
        result.Value.Transitions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CloneOfClone_VersionIncrementsCorrectly()
    {
        // Test shows that cloning always increments from current version
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);
        // Source starts at v1 (default)
        source.Version.Should().Be(1);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        var result = await _handler.Handle(command, CancellationToken.None);

        // Clone of v1 is v2
        result.Value.Version.Should().Be(2);
    }

    [Fact]
    public async Task Handle_PreservesDefinitionTenantId()
    {
        var command = BuildValidCommand();
        var source = CreateSourceDefinition(command.SourceId);

        _repository.GetWithStatesAndTransitionsAsync(command.SourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.TenantId.Should().Be(source.TenantId);
    }

    [Fact]
    public async Task Handle_MultipleClonesSameSource_AllSucceed()
    {
        var source = CreateSourceDefinition(DefaultSourceId);
        var command1 = BuildValidCommand();
        var command2 = BuildValidCommand();

        _repository.GetWithStatesAndTransitionsAsync(DefaultSourceId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));

        // First clone
        var result1 = await _handler.Handle(command1, CancellationToken.None);
        // Second clone (using same source)
        var result2 = await _handler.Handle(command2, CancellationToken.None);

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
    }
}
