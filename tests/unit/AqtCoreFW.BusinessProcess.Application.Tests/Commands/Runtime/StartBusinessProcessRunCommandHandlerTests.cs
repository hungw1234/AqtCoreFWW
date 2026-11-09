using AqtCoreFW.BusinessProcess.Application.Abstractions;
using AqtCoreFW.BusinessProcess.Application.Commands.StartBusinessProcessRun;
using AqtCoreFW.BusinessProcess.Domain.Errors;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Commands.Runtime;

/// <summary>
/// Unit tests for StartBusinessProcessRunCommandHandler — Pattern-A reject + BP NotFound + BP inactive +
/// intake required + happy-path (with intake) + happy-path (no intake).
/// </summary>
public sealed class StartBusinessProcessRunCommandHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid BpPublicId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid EntityId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid TemplatePublicId = Guid.NewGuid();
    private static readonly Guid SubmissionId = Guid.NewGuid();

    private readonly IBusinessProcessRuntimeQueries _runtimeQueries = Substitute.For<IBusinessProcessRuntimeQueries>();
    private readonly IWorkflowModuleClient _workflowClient = Substitute.For<IWorkflowModuleClient>();
    private readonly IFormTemplateQueryClient _formTemplateClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly IFormSubmissionRunClient _formSubmissionClient = Substitute.For<IFormSubmissionRunClient>();
    private readonly ICrossModuleUnitOfWork _uow = Substitute.For<ICrossModuleUnitOfWork>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IGuidGenerator _gen = Substitute.For<IGuidGenerator>();
    private readonly ILogger<StartBusinessProcessRunCommandHandler> _logger =
        Substitute.For<ILogger<StartBusinessProcessRunCommandHandler>>();
    private readonly StartBusinessProcessRunCommandHandler _handler;

    public StartBusinessProcessRunCommandHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);
        _handler = new StartBusinessProcessRunCommandHandler(
            _runtimeQueries, _workflowClient, _formTemplateClient, _formSubmissionClient,
            _uow, _tenantContext, _gen, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_RejectedBeforeRuntimeLookup()
    {
        var result = await _handler.Handle(BuildCommand(tenantId: OtherTenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _runtimeQueries.DidNotReceiveWithAnyArgs().GetRuntimeContextAsync(default, default, default);
        await _workflowClient.DidNotReceiveWithAnyArgs().CreateInstanceByInternalIdAsync(default, default!, default, default, default, default);
    }

    [Fact]
    public async Task Handle_BpNotFound_ReturnsNotFound()
    {
        ArrangeRuntime(null);

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        result.Errors.First().Message.Should().Contain(BusinessProcessErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_BpInactive_ReturnsValidationError()
    {
        ArrangeRuntime(new BpRuntimeContext(1L, 10L, null, null, IsActive: false));

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.First().Message.Should().Contain(BusinessProcessErrorCodes.Inactive);
    }

    [Fact]
    public async Task Handle_IntakeRequiredButDataMissing_ReturnsValidationError()
    {
        ArrangeRuntime(new BpRuntimeContext(1L, 10L, 42L, 3, IsActive: true));

        var result = await _handler.Handle(BuildCommand(intakeFormData: null), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.First().Message.Should().Contain(BusinessProcessErrorCodes.IntakePairingInvalid);
        await _workflowClient.DidNotReceiveWithAnyArgs().CreateInstanceByInternalIdAsync(default, default!, default, default, default, default);
    }

    [Fact]
    public async Task Handle_HappyPath_NoIntake_DoesNotCallFormClient()
    {
        ArrangeRuntime(new BpRuntimeContext(1L, 10L, null, null, IsActive: true));
        _workflowClient.CreateInstanceByInternalIdAsync(10L, "Case", EntityId, CurrentTenantId, ActorId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceResult(InstanceId, Guid.Empty, 5L));

        var result = await _handler.Handle(BuildCommand(intakeFormData: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkflowInstanceId.Should().Be(InstanceId);
        result.Value.InitialStateInternalId.Should().Be(5L);
        result.Value.IntakeFormSubmissionId.Should().BeNull();
        await _formSubmissionClient.DidNotReceiveWithAnyArgs()
            .CreateForBpRunAsync(default, default, default, default, default!, default, default, default, default);
    }

    [Fact]
    public async Task Handle_HappyPath_WithIntake_BothClientsInvoked()
    {
        ArrangeRuntime(new BpRuntimeContext(1L, 10L, 42L, 3, IsActive: true));
        _workflowClient.CreateInstanceByInternalIdAsync(10L, "Case", EntityId, CurrentTenantId, ActorId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceResult(InstanceId, Guid.Empty, 5L));
        _formTemplateClient.GetVersionSchemaByInternalIdAsync(42L, 3, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(TemplatePublicId, 3, "{}"));
        _formSubmissionClient.CreateForBpRunAsync(
                InstanceId, null, TemplatePublicId, ActorId,
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>(),
                CurrentTenantId, Arg.Any<BpRunSignaturePayload?>(), Arg.Any<CancellationToken>())
            .Returns(BpRunSubmissionOutcome.Success(SubmissionId));

        var intake = new Dictionary<string, object?> { ["name"] = "x" };
        var result = await _handler.Handle(BuildCommand(intakeFormData: intake), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IntakeFormSubmissionId.Should().Be(SubmissionId);
        await _formSubmissionClient.Received(1).CreateForBpRunAsync(
            InstanceId, null, TemplatePublicId, ActorId,
            Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>(),
            CurrentTenantId, Arg.Any<BpRunSignaturePayload?>(), Arg.Any<CancellationToken>());
    }

    private void ArrangeRuntime(BpRuntimeContext? ctx) =>
        _runtimeQueries.GetRuntimeContextAsync(BpPublicId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(ctx);

    [Fact]
    public async Task Handle_WithIntakeSignature_PassesProofBundleToFormsClient()
    {
        var carrier = new BpRunSignaturePayload([9, 8, 7], null, "Raw", "SHA256", "RSA", "remote-viettel-mysign");
        ArrangeRuntime(new BpRuntimeContext(1L, 10L, 42L, 3, IsActive: true));
        _workflowClient.CreateInstanceByInternalIdAsync(10L, "Case", EntityId, CurrentTenantId, ActorId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceResult(InstanceId, Guid.Empty, 5L));
        _formTemplateClient.GetVersionSchemaByInternalIdAsync(42L, 3, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(TemplatePublicId, 3, "{}"));
        _formSubmissionClient.CreateForBpRunAsync(
                InstanceId, null, TemplatePublicId, ActorId,
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>(),
                CurrentTenantId, Arg.Any<BpRunSignaturePayload?>(), Arg.Any<CancellationToken>())
            .Returns(BpRunSubmissionOutcome.Success(SubmissionId));

        var intake = new Dictionary<string, object?> { ["name"] = "x" };
        var result = await _handler.Handle(BuildCommand(intakeFormData: intake, signature: carrier), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // The exact carrier must reach the Forms client unchanged — verified at ingest inside the tx.
        await _formSubmissionClient.Received(1).CreateForBpRunAsync(
            InstanceId, null, TemplatePublicId, ActorId,
            Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>(),
            CurrentTenantId, carrier, Arg.Any<CancellationToken>());
    }

    private static StartBusinessProcessRunCommand BuildCommand(
        Guid? tenantId = null,
        IReadOnlyDictionary<string, object?>? intakeFormData = null,
        BpRunSignaturePayload? signature = null) =>
        new(BpPublicId,
            tenantId ?? CurrentTenantId,
            "Case",
            EntityId,
            intakeFormData,
            ConsentGiven: false,
            ActorId,
            signature);
}
