using AqtCoreFW.BusinessProcess.Application.Abstractions;
using AqtCoreFW.BusinessProcess.Application.Commands.ExecuteBusinessProcessTransition;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SignerSlotStatus = AqtCoreFW.SharedKernel.Contracts.Clients.SignerSlotStatus;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Commands.Runtime;

/// <summary>
/// Unit tests for ExecuteBusinessProcessTransitionCommandHandler — Pattern-A reject + required-form
/// short-circuit + happy-path (no form) + happy-path (with bundled form).
/// </summary>
public sealed class ExecuteBusinessProcessTransitionCommandHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid BpPublicId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TemplatePublicId = Guid.NewGuid();
    private static readonly Guid FormSubmissionId = Guid.NewGuid();

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

    public ExecuteBusinessProcessTransitionCommandHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _handler = new ExecuteBusinessProcessTransitionCommandHandler(
            _runtimeQueries, _workflowClient, _formTemplateClient, _formSubmissionClient,
            _signatureSessionClient, _uow, _tenantContext, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_RejectedBeforeAnyClientCall()
    {
        var result = await _handler.Handle(BuildCommand(tenantId: OtherTenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _workflowClient.DidNotReceiveWithAnyArgs().ResolveTransitionIdAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Handle_TransitionFormDataWithoutTemplateId_ReturnsValidationError()
    {
        var result = await _handler.Handle(
            BuildCommand(transitionFormData: new Dictionary<string, object?> { ["x"] = 1 }, transitionFormTemplateId: null),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _workflowClient.DidNotReceiveWithAnyArgs().ResolveTransitionIdAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Handle_TransitionNotResolved_ReturnsNotFound()
    {
        _workflowClient.ResolveTransitionIdAsync(InstanceId, "Approve", CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((TransitionLookup?)null);

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    [Fact]
    public async Task Handle_TransitionWithMultipleFormBindings_ReturnsValidationError()
    {
        ArrangeTransition(transitionId: 99L, fromStateId: 4L);
        _runtimeQueries.GetTransitionFormTemplateIdsAsync(BpPublicId, 99L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([
                new BpTransitionFormBinding(42L, 3, IsRequired: true),
                new BpTransitionFormBinding(43L, 1, IsRequired: false)
            ]);

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _workflowClient.DidNotReceiveWithAnyArgs().ExecuteTransitionAsync(default!, default);
        await _formSubmissionClient.DidNotReceiveWithAnyArgs()
            .CreateForBpRunAsync(default, default, default, default, default!, default, default, default);
    }

    [Fact]
    public async Task Handle_RequiredFormNotBundled_ReturnsRequireFormsWithSnapshot()
    {
        ArrangeTransition(transitionId: 99L, fromStateId: 4L);
        _runtimeQueries.GetTransitionFormTemplateIdsAsync(BpPublicId, 99L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([new BpTransitionFormBinding(42L, 3, IsRequired: true)]);
        _formTemplateClient.GetVersionSchemaByInternalIdAsync(42L, 3, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(TemplatePublicId, 3, """{"fields":[]}"""));

        var result = await _handler.Handle(BuildCommand(transitionFormData: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RequiresForms.Should().BeTrue();
        // The outcome must carry the rendered snapshot inline (publicId + schemaJson), not a bare id —
        // the FE renders the transition form from this without a follow-up fetch.
        result.Value.TransitionForms.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new BpRequiredTransitionForm(42L, TemplatePublicId, """{"fields":[]}"""));
        await _workflowClient.DidNotReceiveWithAnyArgs().ExecuteTransitionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_RequiredFormNotBundled_SnapshotMissing_ReturnsNotFound()
    {
        ArrangeTransition(transitionId: 99L, fromStateId: 4L);
        _runtimeQueries.GetTransitionFormTemplateIdsAsync(BpPublicId, 99L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([new BpTransitionFormBinding(42L, 3, IsRequired: true)]);
        _formTemplateClient.GetVersionSchemaByInternalIdAsync(42L, 3, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((FormTemplateVersionSchemaDto?)null);

        var result = await _handler.Handle(BuildCommand(transitionFormData: null), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _workflowClient.DidNotReceiveWithAnyArgs().ExecuteTransitionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_HappyPath_NoFormBundled_ExecutesTransition()
    {
        ArrangeTransition(transitionId: 99L, fromStateId: 4L);
        _runtimeQueries.GetTransitionFormTemplateIdsAsync(BpPublicId, 99L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([]);
        _workflowClient.ExecuteTransitionAsync(Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>())
            .Returns(ExecuteTransitionOutcome.Success(new ExecuteTransitionResult(
                Guid.Empty, "Approved", IsFinal: false, NewStateInternalId: 7L)));
        _runtimeQueries.GetStateFormTemplateIdsAsync(BpPublicId, 7L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([new BpStateFormBinding(50L, 1, IsRequired: false)]);

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.NewStateInternalId.Should().Be(7L);
        result.Value.StateForms.Should().BeEquivalentTo(new[] { 50L });
        result.Value.TransitionFormSubmissionId.Should().BeNull();
        await _formSubmissionClient.DidNotReceiveWithAnyArgs()
            .CreateForBpRunAsync(default, default, default, default, default!, default, default, default);
    }

    [Fact]
    public async Task Handle_HappyPath_WithFormBundled_FormSubmittedBeforeExecute()
    {
        ArrangeTransition(transitionId: 99L, fromStateId: 4L);
        _runtimeQueries.GetTransitionFormTemplateIdsAsync(BpPublicId, 99L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([new BpTransitionFormBinding(42L, 3, IsRequired: true)]);
        _formTemplateClient.GetVersionSchemaByInternalIdAsync(42L, 3, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(TemplatePublicId, 3, "{}"));
        _formSubmissionClient.CreateForBpRunAsync(
                InstanceId, 4L, TemplatePublicId, ActorId,
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>(),
                CurrentTenantId, Arg.Any<BpRunSignaturePayload?>(), Arg.Any<CancellationToken>())
            .Returns(BpRunSubmissionOutcome.Success(FormSubmissionId));
        _workflowClient.ExecuteTransitionAsync(Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>())
            .Returns(ExecuteTransitionOutcome.Success(new ExecuteTransitionResult(
                Guid.Empty, "Approved", IsFinal: false, NewStateInternalId: 7L)));
        _runtimeQueries.GetStateFormTemplateIdsAsync(BpPublicId, 7L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([]);

        var data = new Dictionary<string, object?> { ["x"] = 1 };
        var result = await _handler.Handle(
            BuildCommand(transitionFormData: data, transitionFormTemplateId: 42L),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.TransitionFormSubmissionId.Should().Be(FormSubmissionId);
        Received.InOrder(() =>
        {
            _formSubmissionClient.CreateForBpRunAsync(
                InstanceId, 4L, TemplatePublicId, ActorId,
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>(),
                CurrentTenantId, Arg.Any<BpRunSignaturePayload?>(), Arg.Any<CancellationToken>());
            _workflowClient.ExecuteTransitionAsync(Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>());
        });
    }

    private void ArrangeTransition(long transitionId, long fromStateId) =>
        _workflowClient.ResolveTransitionIdAsync(InstanceId, "Approve", CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new TransitionLookup(transitionId, fromStateId));

    [Fact]
    public async Task Handle_WithBundledFormSignature_PassesProofBundleToFormsClient()
    {
        var carrier = new BpRunSignaturePayload([3, 1, 4], [1, 5], "Pkcs7", "SHA256", "RSA", "usb-vgca");
        ArrangeTransition(transitionId: 99L, fromStateId: 4L);
        _runtimeQueries.GetTransitionFormTemplateIdsAsync(BpPublicId, 99L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([new BpTransitionFormBinding(42L, 3, IsRequired: true)]);
        _formTemplateClient.GetVersionSchemaByInternalIdAsync(42L, 3, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(TemplatePublicId, 3, "{}"));
        _formSubmissionClient.CreateForBpRunAsync(
                InstanceId, 4L, TemplatePublicId, ActorId,
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>(),
                CurrentTenantId, Arg.Any<BpRunSignaturePayload?>(), Arg.Any<CancellationToken>())
            .Returns(BpRunSubmissionOutcome.Success(FormSubmissionId));
        _workflowClient.ExecuteTransitionAsync(Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>())
            .Returns(ExecuteTransitionOutcome.Success(new ExecuteTransitionResult(
                Guid.Empty, "Approved", IsFinal: false, NewStateInternalId: 7L)));
        _runtimeQueries.GetStateFormTemplateIdsAsync(BpPublicId, 7L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([]);

        var data = new Dictionary<string, object?> { ["x"] = 1 };
        var result = await _handler.Handle(
            BuildCommand(transitionFormData: data, transitionFormTemplateId: 42L, signature: carrier),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // The exact carrier must reach the Forms client unchanged — verified at ingest inside the tx.
        await _formSubmissionClient.Received(1).CreateForBpRunAsync(
            InstanceId, 4L, TemplatePublicId, ActorId,
            Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>(),
            CurrentTenantId, carrier, Arg.Any<CancellationToken>());
    }

    private static ExecuteBusinessProcessTransitionCommand BuildCommand(
        Guid? tenantId = null,
        IReadOnlyDictionary<string, object?>? transitionFormData = null,
        long? transitionFormTemplateId = null,
        BpRunSignaturePayload? signature = null,
        BpSignGateConfig? signGateConfig = null) =>
        new(InstanceId,
            BpPublicId,
            "Approve",
            transitionFormTemplateId,
            transitionFormData,
            ConsentGiven: false,
            Comment: null,
            tenantId ?? CurrentTenantId,
            ActorId,
            Signature: signature,
            SignGateConfig: signGateConfig);

    // ─────────────────────────────────────────────────────────────────────────────
    // Sign-gate tests (M-of-N state machine, RequiresMoreSignatures short-circuit)
    // ─────────────────────────────────────────────────────────────────────────────

    private static readonly Guid SignerA = Guid.NewGuid();
    private static readonly Guid SignerB = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();
    private const string StepKey = "transition-99";
    private const string ArtifactRef = "tenant-ref/doc.pdf";

    private void ArrangeNoFormBinding(long transitionId = 99L, long fromStateId = 4L)
    {
        ArrangeTransition(transitionId, fromStateId);
        _runtimeQueries.GetTransitionFormTemplateIdsAsync(
                BpPublicId, transitionId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private void ArrangeSignGateOpenSuccess()
    {
        _signatureSessionClient
            .OpenSessionAsync(CurrentTenantId, InstanceId, StepKey, ArtifactRef,
                Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(OpenSessionOutcome.Created(SessionId));
    }

    [Fact]
    public async Task Handle_SignGateNotStarted_ReturnsRequiresMoreSignatures_WithFirstSigner()
    {
        ArrangeNoFormBinding();
        ArrangeSignGateOpenSuccess();

        // Gate not satisfied: both slots Pending, first signer is A
        var slots = new[]
        {
            new SignerSlotStatus(SignerA, 1, SignerSlotState.Pending, null),
            new SignerSlotStatus(SignerB, 2, SignerSlotState.Pending, null),
        };
        _signatureSessionClient
            .GetGateStateAsync(SessionId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(SessionGateStateOutcome.Pending(SignerA, slots));

        var result = await _handler.Handle(
            BuildCommand(signGateConfig: new BpSignGateConfig(StepKey, ArtifactRef,
                [SignerA, SignerB])),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RequiresSignatures.Should().BeTrue();
        result.Value.NextSignerId.Should().Be(SignerA);
        result.Value.SigningSessionId.Should().Be(SessionId);
        result.Value.SignerSlots.Should().HaveCount(2);
        // Transition must NOT execute when gate is unsatisfied
        await _workflowClient.DidNotReceiveWithAnyArgs().ExecuteTransitionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_SignerACompletedSignerBPending_ReturnsRequiresMoreSignatures_WithSignerB()
    {
        ArrangeNoFormBinding();
        ArrangeSignGateOpenSuccess();

        // A completed, B still Pending
        var slots = new[]
        {
            new SignerSlotStatus(SignerA, 1, SignerSlotState.Completed, Guid.NewGuid()),
            new SignerSlotStatus(SignerB, 2, SignerSlotState.Pending, null),
        };
        _signatureSessionClient
            .GetGateStateAsync(SessionId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(SessionGateStateOutcome.Pending(SignerB, slots));

        var result = await _handler.Handle(
            BuildCommand(signGateConfig: new BpSignGateConfig(StepKey, ArtifactRef,
                [SignerA, SignerB])),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RequiresSignatures.Should().BeTrue();
        result.Value.NextSignerId.Should().Be(SignerB);
        await _workflowClient.DidNotReceiveWithAnyArgs().ExecuteTransitionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_AllSignersCompleted_GateSatisfied_TransitionExecutes()
    {
        ArrangeNoFormBinding();
        ArrangeSignGateOpenSuccess();

        // Both slots Completed — gate satisfied
        var slots = new[]
        {
            new SignerSlotStatus(SignerA, 1, SignerSlotState.Completed, Guid.NewGuid()),
            new SignerSlotStatus(SignerB, 2, SignerSlotState.Completed, Guid.NewGuid()),
        };
        _signatureSessionClient
            .GetGateStateAsync(SessionId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(SessionGateStateOutcome.Satisfied(slots));

        _workflowClient.ExecuteTransitionAsync(Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>())
            .Returns(ExecuteTransitionOutcome.Success(new ExecuteTransitionResult(
                Guid.Empty, "Signed", IsFinal: false, NewStateInternalId: 10L)));
        _runtimeQueries.GetStateFormTemplateIdsAsync(BpPublicId, 10L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _handler.Handle(
            BuildCommand(signGateConfig: new BpSignGateConfig(StepKey, ArtifactRef,
                [SignerA, SignerB])),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.RequiresSignatures.Should().BeFalse();
        result.Value.NewStateName.Should().Be("Signed");
        await _workflowClient.Received(1).ExecuteTransitionAsync(Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SignerPendingVerify_GateStaysUnsatisfied_NoTransitionAdvance()
    {
        // Decision #7: PendingVerify blocks the gate unconditionally — a failed cert-bind must
        // not allow the transition to advance.
        ArrangeNoFormBinding();
        ArrangeSignGateOpenSuccess();

        // A is PendingVerify (cert-bind not yet verified by Phase-6 sandbox)
        var slots = new[]
        {
            new SignerSlotStatus(SignerA, 1, SignerSlotState.PendingVerify, Guid.NewGuid()),
            new SignerSlotStatus(SignerB, 2, SignerSlotState.Pending, null),
        };
        _signatureSessionClient
            .GetGateStateAsync(SessionId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(SessionGateStateOutcome.Pending(Guid.Empty, slots)); // no next usable signer while PendingVerify

        var result = await _handler.Handle(
            BuildCommand(signGateConfig: new BpSignGateConfig(StepKey, ArtifactRef,
                [SignerA, SignerB])),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RequiresSignatures.Should().BeTrue();
        // Transition must NOT advance when any slot is PendingVerify
        await _workflowClient.DidNotReceiveWithAnyArgs().ExecuteTransitionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_OpenSessionFails_ReturnsValidationError()
    {
        ArrangeNoFormBinding();

        _signatureSessionClient
            .OpenSessionAsync(CurrentTenantId, InstanceId, StepKey, ArtifactRef,
                Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(OpenSessionOutcome.Fail("SESSION_INVALID_INPUT", "Bad input."));

        var result = await _handler.Handle(
            BuildCommand(signGateConfig: new BpSignGateConfig(StepKey, ArtifactRef,
                [SignerA])),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _workflowClient.DidNotReceiveWithAnyArgs().ExecuteTransitionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_NoSignGateConfig_BypassesSignGateEntirely()
    {
        // When no SignGateConfig is provided, sign-gate client must never be called.
        ArrangeTransition(99L, 4L);
        _runtimeQueries.GetTransitionFormTemplateIdsAsync(BpPublicId, 99L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([]);
        _workflowClient.ExecuteTransitionAsync(Arg.Any<ExecuteTransitionRequest>(), Arg.Any<CancellationToken>())
            .Returns(ExecuteTransitionOutcome.Success(new ExecuteTransitionResult(
                Guid.Empty, "Done", true, 5L)));
        _runtimeQueries.GetStateFormTemplateIdsAsync(BpPublicId, 5L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        await _signatureSessionClient.DidNotReceiveWithAnyArgs()
            .OpenSessionAsync(default, default, default!, default!, default!, default, default);
    }
}
