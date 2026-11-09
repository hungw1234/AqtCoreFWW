using AqtCoreFW.BusinessProcess.Application.Abstractions;
using AqtCoreFW.BusinessProcess.Application.Commands.SubmitBusinessProcessStateForm;
using AqtCoreFW.BusinessProcess.Domain.Errors;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Commands.Runtime;

/// <summary>
/// Unit tests for SubmitBusinessProcessStateFormCommandHandler — Pattern-A reject + FormTemplate-not-configured
/// + happy path.
/// </summary>
public sealed class SubmitBusinessProcessStateFormCommandHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid BpPublicId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TemplatePublicId = Guid.NewGuid();
    private static readonly Guid SubmissionId = Guid.NewGuid();

    private readonly IBusinessProcessRuntimeQueries _runtimeQueries = Substitute.For<IBusinessProcessRuntimeQueries>();
    private readonly IFormTemplateQueryClient _formTemplateClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly IFormSubmissionRunClient _formSubmissionClient = Substitute.For<IFormSubmissionRunClient>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ILogger<SubmitBusinessProcessStateFormCommandHandler> _logger =
        Substitute.For<ILogger<SubmitBusinessProcessStateFormCommandHandler>>();
    private readonly SubmitBusinessProcessStateFormCommandHandler _handler;

    public SubmitBusinessProcessStateFormCommandHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _handler = new SubmitBusinessProcessStateFormCommandHandler(
            _runtimeQueries, _formTemplateClient, _formSubmissionClient, _tenantContext, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_RejectedBeforeBindingLookup()
    {
        var result = await _handler.Handle(BuildCommand(tenantId: OtherTenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _runtimeQueries.DidNotReceiveWithAnyArgs().GetStateFormTemplateIdsAsync(default, default, default, default);
    }

    [Fact]
    public async Task Handle_FormTemplateNotConfiguredForState_ReturnsValidationError()
    {
        _runtimeQueries.GetStateFormTemplateIdsAsync(BpPublicId, 7L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([new BpStateFormBinding(99L, 1, IsRequired: false)]);

        var result = await _handler.Handle(BuildCommand(formTemplateId: 42L), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.First().Message.Should().Contain(BusinessProcessErrorCodes.DuplicateBinding);
        await _formSubmissionClient.DidNotReceiveWithAnyArgs()
            .CreateForBpRunAsync(default, default, default, default, default!, default, default, default, default);
    }

    [Fact]
    public async Task Handle_HappyPath_SubmitsAndReturnsSubmissionId()
    {
        _runtimeQueries.GetStateFormTemplateIdsAsync(BpPublicId, 7L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([new BpStateFormBinding(42L, 2, IsRequired: true)]);
        _formTemplateClient.GetVersionSchemaByInternalIdAsync(42L, 2, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(TemplatePublicId, 2, "{}"));
        _formSubmissionClient.CreateForBpRunAsync(
                InstanceId, 7L, TemplatePublicId, ActorId,
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>(),
                CurrentTenantId, Arg.Any<BpRunSignaturePayload?>(), Arg.Any<CancellationToken>())
            .Returns(BpRunSubmissionOutcome.Success(SubmissionId));

        var result = await _handler.Handle(BuildCommand(formTemplateId: 42L), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FormSubmissionId.Should().Be(SubmissionId);
    }

    [Fact]
    public async Task Handle_WithSignature_PassesProofBundleToFormsClient()
    {
        var carrier = new BpRunSignaturePayload([1, 2, 3], [4, 5], "Pkcs7", "SHA256", "RSA", "usb-vgca");
        _runtimeQueries.GetStateFormTemplateIdsAsync(BpPublicId, 7L, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns([new BpStateFormBinding(42L, 2, IsRequired: true)]);
        _formTemplateClient.GetVersionSchemaByInternalIdAsync(42L, 2, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(TemplatePublicId, 2, "{}"));
        _formSubmissionClient.CreateForBpRunAsync(
                InstanceId, 7L, TemplatePublicId, ActorId,
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>(),
                CurrentTenantId, Arg.Any<BpRunSignaturePayload?>(), Arg.Any<CancellationToken>())
            .Returns(BpRunSubmissionOutcome.Success(SubmissionId));

        await _handler.Handle(BuildCommand(formTemplateId: 42L, signature: carrier), CancellationToken.None);

        // The exact carrier must reach the Forms client unchanged — the proof bundle is verified at ingest.
        await _formSubmissionClient.Received(1).CreateForBpRunAsync(
            InstanceId, 7L, TemplatePublicId, ActorId,
            Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<bool>(),
            CurrentTenantId, carrier, Arg.Any<CancellationToken>());
    }

    private static SubmitBusinessProcessStateFormCommand BuildCommand(
        Guid? tenantId = null,
        long formTemplateId = 42L,
        BpRunSignaturePayload? signature = null) =>
        new(InstanceId,
            WorkflowStateId: 7L,
            BpPublicId,
            formTemplateId,
            new Dictionary<string, object?> { ["x"] = 1 },
            ConsentGiven: true,
            tenantId ?? CurrentTenantId,
            ActorId,
            signature);
}
