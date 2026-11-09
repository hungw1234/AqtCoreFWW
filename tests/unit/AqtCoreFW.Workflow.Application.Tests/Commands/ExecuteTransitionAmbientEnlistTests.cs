using System.Transactions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Commands.ExecuteTransition;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Reflection;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// A3 unit tests — Slice 6 ambient-enlist guard.
/// Verifies that <see cref="ExecuteTransitionCommandHandler"/> opens its own transaction
/// (Mode A) when no ambient transaction is active, and skips opening one (Mode B) when
/// a <see cref="TransactionScope"/> is already active.
///
/// Test seam: <see cref="IWorkflowInstanceRepository.HasAmbientTransaction"/> and
/// <see cref="IWorkflowInstanceRepository.BeginTransactionAsync"/> are on the repository
/// interface (Application→Infrastructure boundary), so they are NSubstitute-mockable
/// without any EF Core dependency in the Application test project.
/// </summary>
public sealed class ExecuteTransitionAmbientEnlistTests
{
    // ── shared state ─────────────────────────────────────────────────────────────
    private static long _idSeed;

    private static long NextId() => Interlocked.Increment(ref _idSeed);

    private static void AssignId(AqtCoreFW.SharedKernel.Domain.Entity e, long id)
    {
        typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(e, id);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal happy-path scenario (Draft→Submitted, isFinal=true, no branch)
    /// and returns all mocks wired for a successful transition + a transaction mock
    /// that the caller can inspect.
    /// </summary>
    private static (
        ExecuteTransitionCommandHandler handler,
        IWorkflowInstanceRepository instanceRepo,
        IWorkflowTransaction txMock,
        ExecuteTransitionCommand cmd)
        BuildHandler(bool hasAmbientTransactionReturns)
    {
        var gen = TestGuidGenerator.Create();
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        // ── domain objects ──
        var definition = WorkflowDefinition.Create(gen, "Test", "Desc", tenantId, actor);
        var draft = WorkflowState.Create(definition.Id, "Draft", "Nháp", "Draft",
            isInitial: true, isFinal: false, "#808080", 0);
        AssignId(draft, NextId());

        var submitted = WorkflowState.Create(definition.Id, "Submitted", "Đã nộp", "Submitted",
            isInitial: false, isFinal: true,  "#1890ff", 1);
        AssignId(submitted, NextId());

        definition.AddState(draft);
        definition.AddState(submitted);

        var transition = WorkflowTransition.Create(
            definition.Id, draft.Id, submitted.Id,
            "Submit", "Nộp", "Submit",
            conditionsJson: null, sortOrder: 0);
        AssignId(transition, NextId());
        definition.AddTransition(transition);

        var instance = WorkflowInstance.Create(gen, definition.Id, draft.Id,
            "IntegrationTest", Guid.NewGuid(), tenantId, actor);

        // ── mocks ──
        var instanceRepo   = Substitute.For<IWorkflowInstanceRepository>();
        var defRepo        = Substitute.For<IWorkflowDefinitionRepository>();
        var condEval       = Substitute.For<IConditionEvaluator>();
        var branchRepo     = Substitute.For<IParallelBranchRepository>();
        var branchService  = Substitute.For<IParallelBranchService>();
        var attachmentRepo = Substitute.For<ITransitionAttachmentRepository>();
        var identityClient = Substitute.For<IIdentityModuleClient>();
        var resolver       = Substitute.For<IWorkflowHandlerResolver>();
        var txMock         = Substitute.For<IWorkflowTransaction>();

        // Wire instance + definition loads
        instanceRepo.GetWithHistoryAsync(instance.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(instance));
        defRepo.GetWithStatesAndTransitionsByInternalIdAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));
        // Slice 7: ETR calls UpdateWithStateGuardNoSaveAsync + SaveChangesAsync (defer-save split).
        instanceRepo.UpdateWithStateGuardNoSaveAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));
        instanceRepo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(0)));
        // Old variant still wired for completeness (not called by ETR directly; retained for 3 other callers).
        instanceRepo.UpdateWithStateGuardAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        // Authz-of-T gate: wire a ByPermission OA so the actor is authorized
        const string Permission = "TEST.SUBMIT";
        var oa = new OutgoingAssignment(transition, null);
        defRepo.GetOutgoingWithAssignmentsAsync(draft.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new[] { oa }));
        identityClient.GetEffectivePermissionCodesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string> { Permission });
        resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == draft.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, Permission, AssignmentCombineMode.Union)
            }));

        // No parallel branch configured (submitted is final anyway)
        branchRepo.GetBranchByStateIdAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowParallelBranch?)null);

        // Condition evaluator always passes
        condEval.Evaluate(Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, object?>>())
            .Returns(Result.Ok());

        // ── ambient-enlist seam ──
        instanceRepo.HasAmbientTransaction().Returns(hasAmbientTransactionReturns);
        instanceRepo.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(txMock));

        var handler = new ExecuteTransitionCommandHandler(
            instanceRepo, defRepo, condEval,
            branchRepo, branchService, attachmentRepo, identityClient,
            resolver, NullLogger<ExecuteTransitionCommandHandler>.Instance,
            gen,
            new WorkflowTaskFactory(Substitute.For<IWorkflowTaskRepository>()),
            Substitute.For<IWorkflowTaskRepository>(), // task repo: source-state task completion (auto-empty open-task list)
            Substitute.For<IOrgUnitModuleClient>());   // org-unit codes for authz parity (auto-empty set)

        var cmd = new ExecuteTransitionCommand(
            InstanceId:  instance.PublicId,
            TransitionId: transition.Id,
            ActorId:     actor,
            Comment:     null);

        return (handler, instanceRepo, txMock, cmd);
    }

    // ── A3a. Mode A — no ambient transaction ─────────────────────────────────────

    [Fact]
    public async Task Mode_A_NoAmbient_OpensAndCommitsOwnTransaction()
    {
        // Arrange: repository says no ambient tx is active
        var (handler, instanceRepo, txMock, cmd) = BuildHandler(hasAmbientTransactionReturns: false);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert: handler opened its own transaction and committed it
        result.IsSuccess.Should().BeTrue($"transition should succeed (errors: {string.Join(", ", result.Errors.Select(e => e.Message))})");
        await instanceRepo.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await txMock.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        // Rollback must NOT be called on the success path
        await txMock.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    // ── A3b. Mode B — ambient TransactionScope present ───────────────────────────

    [Fact]
    public async Task Mode_B_AmbientTransactionScope_DoesNotOpenOwnTransaction()
    {
        // Arrange: repository says ambient tx IS active (simulates BP-path TransactionScope)
        var (handler, instanceRepo, txMock, cmd) = BuildHandler(hasAmbientTransactionReturns: true);

        // Act — wrapping in TransactionScope to be realistic about BP-path caller pattern,
        // though the repository stub is what actually drives the guard logic in-unit.
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            TransactionScopeAsyncFlowOption.Enabled);

        var result = await handler.Handle(cmd, CancellationToken.None);
        scope.Complete();

        // Assert: handler must NOT open its own transaction when ambient is active
        result.IsSuccess.Should().BeTrue($"transition should succeed (errors: {string.Join(", ", result.Errors.Select(e => e.Message))})");
        await instanceRepo.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        // Commit/Rollback on txMock must also be silent (no ownsTransaction)
        await txMock.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await txMock.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }
}
