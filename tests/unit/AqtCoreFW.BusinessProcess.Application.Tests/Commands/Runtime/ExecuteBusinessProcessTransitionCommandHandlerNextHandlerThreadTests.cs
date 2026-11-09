using AqtCoreFW.BusinessProcess.Application.Abstractions;
using AqtCoreFW.BusinessProcess.Application.Commands.ExecuteBusinessProcessTransition;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Commands.Runtime;

/// <summary>
/// A5 — Slice 10b: BP-path NextHandlerUserId 3-layer thread coverage (UNIT layer per D3).
///
/// Verifies that <see cref="ExecuteBusinessProcessTransitionCommand.NextHandlerUserId"/>
/// is propagated UNCHANGED through to the <see cref="IWorkflowModuleClient.ExecuteTransitionAsync"/>
/// call site at handler line 116 (Slice 10a DDTO-DEFAULT).
///
/// Anti-tautology preconditions (S5 META-L (c) prevention):
///   (a) IWorkflowModuleClient.ExecuteTransitionAsync was called exactly once — proves handler reached line 109.
///       Without this mock, ResolveTransitionIdAsync returns null → handler short-circuits at NotFound →
///       precondition (a) fails (ReceivedCallsException "received no matching calls"). This was the RED state.
///   (b) Captured request is non-null — proves Arg.Do&lt;&gt; fired.
///   (c) captured.ActorId == cmd.ActorId AND captured.InstanceId == cmd.WorkflowInstanceId —
///       proves handler ran the real code path, not a default-constructed stub.
///       Both are distinct non-default Guids by construction (class-level statics).
///   (d) LOAD-BEARING: captured.NextHandlerUserId == cmd.NextHandlerUserId.
///
/// Without (a)+(b)+(c), assert (d) can reduce to "null == null" tautology in A5b since
/// Guid? defaults to null on both sides independently. Precondition (c) breaks the tautology by
/// requiring that at least two OTHER captured fields prove the handler ran the real path.
///
/// File placed at Commands/ per spec (not Commands/Runtime/) — note for orchestrator: existing peer test
/// lives at Commands/Runtime/; spec mandated Commands/ — preserved verbatim, no initiative taken.
/// </summary>
public sealed class ExecuteBusinessProcessTransitionCommandHandlerNextHandlerThreadTests
{
    // Static distinct Guids — satisfies anti-tautology precondition (c): non-default by construction.
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid BpPublicId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private readonly IBusinessProcessRuntimeQueries _runtimeQueries = Substitute.For<IBusinessProcessRuntimeQueries>();
    private readonly IWorkflowModuleClient _workflowClient = Substitute.For<IWorkflowModuleClient>();
    private readonly IFormTemplateQueryClient _formTemplateClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly IFormSubmissionRunClient _formSubmissionClient = Substitute.For<IFormSubmissionRunClient>();
    private readonly IDocumentSignatureSessionClient _signatureSessionClient =
        Substitute.For<IDocumentSignatureSessionClient>();
    private readonly ICrossModuleUnitOfWork _uow = Substitute.For<ICrossModuleUnitOfWork>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ILogger<ExecuteBusinessProcessTransitionCommandHandler> _logger =
        Substitute.For<ILogger<ExecuteBusinessProcessTransitionCommandHandler>>();
    private readonly ExecuteBusinessProcessTransitionCommandHandler _handler;

    public ExecuteBusinessProcessTransitionCommandHandlerNextHandlerThreadTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _handler = new ExecuteBusinessProcessTransitionCommandHandler(
            _runtimeQueries, _workflowClient, _formTemplateClient, _formSubmissionClient,
            _signatureSessionClient, _uow, _tenantContext, _logger);
    }

    // ── happy-path arrangement (mirrors Commands/Runtime/ExecuteBusinessProcessTransitionCommandHandlerTests.cs) ──
    // Without this method, ResolveTransitionIdAsync returns null → handler short-circuits at NotFound
    // → ExecuteTransitionAsync is never called → precondition (a) Received(1) fails = RED state.

    private void ArrangeHappyPath(long transitionId = 99L, long fromStateId = 4L, long newStateId = 7L)
    {
        // Resolve transition lookup — REQUIRED or handler short-circuits at NotFound before reaching line 109
        _workflowClient.ResolveTransitionIdAsync(InstanceId, "Approve", CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new TransitionLookup(transitionId, fromStateId));

        // No form bindings → skip form-gating branch entirely
        _runtimeQueries.GetTransitionFormTemplateIdsAsync(BpPublicId, transitionId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([]);

        // ExecuteTransitionAsync: Arg.Do<> capture is set up per test via When().Do(); this provides the return value
        _workflowClient.ExecuteTransitionAsync(Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>())
            .Returns(ExecuteTransitionOutcome.Success(new ExecuteTransitionResult(
                Guid.Empty, "Approved", IsFinal: false, NewStateInternalId: newStateId)));

        // State forms for new state
        _runtimeQueries.GetStateFormTemplateIdsAsync(BpPublicId, newStateId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private ExecuteBusinessProcessTransitionCommand BuildCommand(Guid? nextHandlerUserId = null) =>
        new(InstanceId,
            BpPublicId,
            "Approve",
            TransitionFormTemplateId: null,
            TransitionFormData: null,
            ConsentGiven: false,
            Comment: null,
            TenantId: CurrentTenantId,
            ActorId: ActorId,
            NextHandlerUserId: nextHandlerUserId);

    // ── A5a ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A5a: A non-null Guid passed as NextHandlerUserId threads through unchanged to
    /// <see cref="IWorkflowModuleClient.ExecuteTransitionAsync"/> (handler line 116).
    /// </summary>
    [Fact]
    public async Task Handle_NextHandlerUserId_NonNullGuid_ThreadsThroughToWorkflowClient()
    {
        var expectedNextHandler = Guid.NewGuid();

        // Wire happy-path mocks — without this, ResolveTransitionIdAsync returns null → RED
        ArrangeHappyPath();

        // Capture the ExecuteTransitionRequest via Arg.Do<> (S10a A4 capture pattern)
        ExecuteTransitionRequest? captured = null;
        _workflowClient.When(c => c.ExecuteTransitionAsync(Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>()))
            .Do(call => captured = call.Arg<ExecuteTransitionRequest>());

        var cmd = BuildCommand(nextHandlerUserId: expectedNextHandler);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Precondition (a): workflow client was reached — proves handler executed past all short-circuit guards
        await _workflowClient.Received(1).ExecuteTransitionAsync(
            Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>());

        // Precondition (b): capture fired
        captured.Should().NotBeNull("ExecuteTransitionAsync must have been called with a request object");

        // Precondition (c): anti-tautology — other fields match cmd, proving handler ran the real code path
        captured!.ActorId.Should().Be(ActorId,
            "captured ActorId must match cmd.ActorId to prove handler ran, not a default-constructed stub");
        captured.InstanceId.Should().Be(InstanceId,
            "captured InstanceId must match cmd.WorkflowInstanceId to prove handler ran the real path");

        // Assert (d) LOAD-BEARING: thread-through
        captured.NextHandlerUserId.Should().Be(expectedNextHandler,
            "3-layer BP thread: ExecuteBusinessProcessTransitionCommand.NextHandlerUserId → ExecuteTransitionRequest.NextHandlerUserId at handler:116 (Slice 10a DDTO-DEFAULT)");

        // Sanity: handler succeeded end-to-end
        result.IsSuccess.Should().BeTrue("handler must succeed when all gates pass");
    }

    // ── A5b ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A5b: null NextHandlerUserId threads through as null (NOT defaulted or replaced).
    ///
    /// Anti-tautology requirement: cmd has non-default ActorId + InstanceId (class-level statics) so that
    /// "null == null" cannot be trivially true without handler executing.
    /// Precondition (c) proves handler ran via captured.ActorId == distinct static ActorId.
    /// </summary>
    [Fact]
    public async Task Handle_NextHandlerUserId_Null_ThreadsNullThroughToWorkflowClient()
    {
        // Wire happy-path mocks — without this, ResolveTransitionIdAsync returns null → RED
        ArrangeHappyPath();

        ExecuteTransitionRequest? captured = null;
        _workflowClient.When(c => c.ExecuteTransitionAsync(Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>()))
            .Do(call => captured = call.Arg<ExecuteTransitionRequest>());

        // cmd has NextHandlerUserId = null; ActorId and InstanceId are distinct non-default Guids (class-level statics)
        var cmd = BuildCommand(nextHandlerUserId: null);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Precondition (a): workflow client was reached
        await _workflowClient.Received(1).ExecuteTransitionAsync(
            Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>());

        // Precondition (b): capture fired
        captured.Should().NotBeNull("ExecuteTransitionAsync must have been called with a request object");

        // Precondition (c): anti-tautology — distinct fields prove handler ran, breaking null==null trivially
        captured!.ActorId.Should().Be(ActorId,
            "captured ActorId proves handler ran the real path (not default-value coincidence)");
        captured.InstanceId.Should().Be(InstanceId,
            "captured InstanceId confirms real handler path executed");

        // Assert (d) LOAD-BEARING: null must thread through, NOT be replaced by a default value
        captured.NextHandlerUserId.Should().BeNull(
            "null must thread through unchanged — handler must NOT substitute a default for null NextHandlerUserId");

        result.IsSuccess.Should().BeTrue("handler must succeed when NextHandlerUserId is null and all gates pass");
    }
}
