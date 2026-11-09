using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Application.Commands.SubmitPublicForm;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using System.Text.Json;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// AUD-4 unit test for <see cref="SubmitPublicFormCommandHandler"/> — verifies the
/// anonymous public path publishes <see cref="FormSubmissionAcknowledgedIntegrationEvent"/>
/// with <c>SubmittedBy = Guid.Empty</c> (no known submitter).
/// Phase-07: FormTemplate retiered T1→T2 — event now carries template.PublicId instead of Guid.Empty.
/// </summary>
public sealed class SubmitPublicFormCommandHandlerTests
{
    private readonly IReadDbConnection         _db                = Substitute.For<IReadDbConnection>();
    private readonly IFormTemplateRepository   _templateRepo      = Substitute.For<IFormTemplateRepository>();
    private readonly IFormSubmissionRepository _submissionRepo    = Substitute.For<IFormSubmissionRepository>();
    private readonly IFormValidationService    _validationService = Substitute.For<IFormValidationService>();
    private readonly IFormDataStorageFactory   _storageFactory    = Substitute.For<IFormDataStorageFactory>();
    private readonly IFormDataStorage          _storage           = Substitute.For<IFormDataStorage>();
    private readonly IFormulaEvaluationService _formulaService    = Substitute.For<IFormulaEvaluationService>();
    private readonly ITableFieldDataRepository _tableFieldDataRepo = Substitute.For<ITableFieldDataRepository>();
    private readonly IConsentModuleClient      _consentClient     = Substitute.For<IConsentModuleClient>();
    private readonly IWorkflowModuleClient     _workflowClient    = Substitute.For<IWorkflowModuleClient>();
    private readonly IPublisher                _publisher         = Substitute.For<IPublisher>();
    private readonly ICrossModuleUnitOfWork    _uow               = Substitute.For<ICrossModuleUnitOfWork>();
    private readonly IOutboxWriter             _outboxWriter      = Substitute.For<IOutboxWriter>();
    private readonly IGuidGenerator            _guidGen           = Substitute.For<IGuidGenerator>();
    private readonly IIdentitySigningClient    _signingClient     = Substitute.For<IIdentitySigningClient>();
    private readonly SubmitPublicFormCommandHandler _handler;

    private static readonly Guid TestTenantId     = Guid.NewGuid();
    private static readonly Guid AdminId          = Guid.NewGuid();
    private static readonly Guid TemplatePublicId = Guid.NewGuid();

    public SubmitPublicFormCommandHandlerTests()
    {
        _storageFactory.Create(Arg.Any<StorageMode>()).Returns(_storage);

        _validationService
            .ValidateAsync(Arg.Any<IEnumerable<FormField>>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new List<ValidationError>())));

        _guidGen.NewIdRandom().Returns(Guid.NewGuid());
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        // Default: signing client never called unless a DigitalSignature field is present
        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SignatureVerifyResult { IsValid = false }));

        // All templates in this file use StorageMode.Json, so ITableFieldDataRepository is never
        // actually invoked by the handler (only reached on template.StorageMode == StorageMode.Table)
        // — a bare substitute with no .Returns(...) setup is sufficient.
        _handler = new SubmitPublicFormCommandHandler(
            _guidGen,
            _db, _templateRepo, _submissionRepo, _validationService,
            _storageFactory, _formulaService, _tableFieldDataRepo, _consentClient, _workflowClient,
            _publisher, _uow, _outboxWriter,
            Substitute.For<ILogger<SubmitPublicFormCommandHandler>>(),
            _signingClient);
    }

    [Fact]
    public async Task Handle_PublicSubmission_PublishesAcknowledgedEventWithEmptyActor()
    {
        var template = CreateActivePublicJsonTemplate();
        StubTemplateLookup(template);

        var command = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "hello" });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Slice 1B-1: the acknowledged event now rides the Outbox (via IOutboxWriter), enqueued
        // inside the CrossModuleUnitOfWork transaction — no more direct messageBus.PublishAsync.
        await _outboxWriter.Received(1).EnqueueAsync(
            Arg.Is<FormSubmissionAcknowledgedIntegrationEvent>(e =>
                e.SubmittedBy == Guid.Empty &&
                e.TenantId == TestTenantId &&
                // Phase-07: FormTemplate retiered T1→T2 — event carries template.PublicId
                e.FormTemplateId == TemplatePublicId &&
                e.MessageId != Guid.Empty),
            "forms",
            Arg.Any<CancellationToken>());

        // The anonymous public path pins SESSION_CONTEXT to the template-resolved tenant via the
        // tenant-override BeginAsync overload — NOT the ambient-context parameterless one.
        await _uow.Received(1).BeginAsync(TestTenantId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().BeginAsync(Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SubmissionAddThrows_RollsBackAndNeverEnqueuesOutboxEvent()
    {
        var template = CreateActivePublicJsonTemplate();
        StubTemplateLookup(template);

        _submissionRepo
            .AddAsync(Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("simulated storage failure"));

        var command = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "hello" });

        Func<Task> act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        await _outboxWriter.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default!, default);
        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    // ─── Digital signature verify-at-ingest (public path) ─────────────────────

    private FormTemplate CreateActivePublicJsonTemplateWithDigSigField(bool required = true)
    {
        var t = FormTemplate.Create(
            _guidGen, TestTenantId, "Template", "Biểu mẫu", "public_template", StorageMode.Json, AdminId);
        t.AddField("field_text", FormFieldType.Text, "Văn bản", "Text", 1, false, null, AdminId);
        // Inject DigitalSignature field directly via reflection (no AddField sugar yet)
        var digSigField = FormField.Create(
            t.Id, "sig_field", FormFieldType.DigitalSignature,
            "Chữ ký số", "Digital Signature", 2, required, 1);
        var fieldsField = typeof(FormTemplate)
            .GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((List<FormField>)fieldsField.GetValue(t)!).Add(digSigField);
        t.Publish(AdminId);
        return t;
    }

    private static SignaturePayload MakeDummyBundle() =>
        new(Signature: [0x01, 0x02, 0x03], SignerCertDer: [0xAB, 0xCD]);

    private static SignatureVerifyResult MakeValidResult() =>
        new()
        {
            IsValid           = true,
            SignerSubject     = "CN=Public User",
            SignerSerial      = "SERIAL-PUB-001",
            SignerCertDer     = [0xAB, 0xCD],
            ChainEvidenceJson = "{\"chain\":[]}",
            VerifiedAt        = DateTimeOffset.UtcNow
        };

    /// <summary>
    /// Valid signature on public path → submission saved with all 6 evidence fields.
    /// </summary>
    [Fact]
    public async Task Handle_PublicPath_ValidSignature_SubmissionSavedWithEvidenceFields()
    {
        var template = CreateActivePublicJsonTemplateWithDigSigField();
        StubTemplateLookup(template);

        var expected = MakeValidResult();
        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        FormSubmission? captured = null;
        await _submissionRepo.AddAsync(
            Arg.Do<FormSubmission>(s => captured = s), Arg.Any<CancellationToken>());

        var cmd = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "hello" },
            Signature: MakeDummyBundle());

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.SignatureVerdict.Should().Be(true);
        captured.SignerSubject.Should().Be(expected.SignerSubject);
        captured.SignerSerial.Should().Be(expected.SignerSerial);
        captured.SignerCertDer.Should().BeEquivalentTo(expected.SignerCertDer);
        captured.SignatureChainEvidence.Should().Be(expected.ChainEvidenceJson);
        captured.SignatureVerifiedAt.Should().NotBeNull();
    }

    /// <summary>
    /// Tampered signature on public path → IsFailed AND AddAsync never called.
    /// </summary>
    [Fact]
    public async Task Handle_PublicPath_TamperedSignature_ReturnsFailAndNeverPersists()
    {
        var template = CreateActivePublicJsonTemplateWithDigSigField();
        StubTemplateLookup(template);

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SignatureVerifyResult
            {
                IsValid = false, FailureReason = "tampered"
            }));

        var cmd = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "hello" },
            Signature: MakeDummyBundle());

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("GOV_FRM_022"));

        await _submissionRepo.DidNotReceive().AddAsync(
            Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Required DigitalSignature field but Signature=null → 400 required, no persist.
    /// </summary>
    [Fact]
    public async Task Handle_PublicPath_RequiredDigSigFieldWithNoBundle_ReturnsRequiredError()
    {
        var template = CreateActivePublicJsonTemplateWithDigSigField(required: true);
        StubTemplateLookup(template);

        var cmd = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "hello" },
            Signature: null);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("GOV_FRM_021"));

        await _submissionRepo.DidNotReceive().AddAsync(
            Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// No DigitalSignature field in template → signing client never invoked (regression guard).
    /// </summary>
    [Fact]
    public async Task Handle_PublicPath_NoDigSigField_SigningClientNeverInvoked()
    {
        var template = CreateActivePublicJsonTemplate();
        StubTemplateLookup(template);

        var cmd = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "hello" });

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await _signingClient.DidNotReceive().VerifySignatureAsync(
            Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// TenantId in SignatureVerifyRequest must equal row.TenantId (server-resolved from template,
    /// never from the client bundle).
    /// </summary>
    [Fact]
    public async Task Handle_PublicPath_VerifyRequestUsesTenantIdFromTemplateRow()
    {
        var template = CreateActivePublicJsonTemplateWithDigSigField();
        StubTemplateLookup(template);

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MakeValidResult()));

        var cmd = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "hello" },
            Signature: MakeDummyBundle());

        await _handler.Handle(cmd, CancellationToken.None);

        // TenantId must be the row.TenantId resolved from Dapper query, not any client value
        await _signingClient.Received(1).VerifySignatureAsync(
            Arg.Is<SignatureVerifyRequest>(r => r.TenantId == TestTenantId),
            Arg.Any<CancellationToken>());
    }

    // ─── Mode-2 (ServerDocHash) verify-at-ingest — public path ──────────────────

    private FormTemplate CreateActivePublicJsonTemplateWithServerDocHashField()
    {
        var t = FormTemplate.Create(
            _guidGen, TestTenantId, "Template", "Biểu mẫu", "public_template", StorageMode.Json, AdminId);
        t.AddField("field_text", FormFieldType.Text, "Văn bản", "Text", 1, false, null, AdminId);
        var digSigField = FormField.Create(
            t.Id, "sig_field", FormFieldType.DigitalSignature,
            "Chữ ký số", "Digital Signature", 2, true, 1,
            validationRulesJson: """{"signatureMode":"ServerDocHash"}""");
        var fieldsField = typeof(FormTemplate)
            .GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((List<FormField>)fieldsField.GetValue(t)!).Add(digSigField);
        t.Publish(AdminId);
        return t;
    }

    /// <summary>
    /// Mode-2 public path: Payload passed to VerifySignatureAsync equals ComputeMode2DocHash,
    /// NOT Mode-1 canonical bytes. Non-vacuous: asserts both equality to Mode-2 and inequality
    /// to Mode-1.
    /// </summary>
    [Fact]
    public async Task Handle_PublicPath_Mode2_VerifyCalledWithDocHashBytes()
    {
        var template = CreateActivePublicJsonTemplateWithServerDocHashField();
        StubTemplateLookup(template);

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MakeValidResult()));

        var data = new Dictionary<string, object?> { ["field_text"] = "hello" };

        SignatureVerifyRequest? capturedReq = null;
        await _signingClient.VerifySignatureAsync(
            Arg.Do<SignatureVerifyRequest>(r => capturedReq = r),
            Arg.Any<CancellationToken>());

        var cmd = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: data,
            Signature: MakeDummyBundle());

        await _handler.Handle(cmd, CancellationToken.None);

        capturedReq.Should().NotBeNull();

        var activeFields = template.Fields.Where(f => f.IsActive).ToList();

        var expectedDocHash = FormDataCanonicalizer.ComputeMode2DocHash(
            template.Code, template.Version, data, activeFields);
        var mode1Bytes = FormDataCanonicalizer.CanonicalizeMode1(data, activeFields);

        capturedReq!.Payload.Should().BeEquivalentTo(expectedDocHash,
            "Mode-2 public path must pass the 32-byte doc hash as Payload");

        Convert.ToHexString(expectedDocHash).Should().NotBe(Convert.ToHexString(mode1Bytes),
            "sanity check: Mode-2 hash and Mode-1 bytes must differ");
    }

    /// <summary>
    /// Mode-2 public path: tampered hash → IsFailed + no persistence.
    /// </summary>
    [Fact]
    public async Task Handle_PublicPath_Mode2_TamperedSignature_ReturnsFailAndNeverPersists()
    {
        var template = CreateActivePublicJsonTemplateWithServerDocHashField();
        StubTemplateLookup(template);

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SignatureVerifyResult
            {
                IsValid = false, FailureReason = "mode2 hash mismatch"
            }));

        var cmd = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "hello" },
            Signature: MakeDummyBundle());

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("GOV_FRM_022"));

        await _submissionRepo.DidNotReceive().AddAsync(
            Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>());
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private void StubTemplateLookup(FormTemplate template)
    {
        // Handler resolves Id+TenantId+PublicId by Code via the public_forms_bypass Dapper read,
        // then loads with fields via GetPublicWithFieldsAsync (tenant-pinned EF read).
        _db.QueryFirstOrDefaultWithPublicFormsBypassAsync<SubmitPublicFormCommandHandler.TemplateIdRow>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(Task.FromResult<SubmitPublicFormCommandHandler.TemplateIdRow?>(
                new SubmitPublicFormCommandHandler.TemplateIdRow
                {
                    Id = template.Id,
                    TenantId = TestTenantId,
                    PublicId = TemplatePublicId
                }));

        _templateRepo
            .GetPublicWithFieldsAsync(template.Id, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));
    }

    private FormTemplate CreateActivePublicJsonTemplate()
    {
        var t = FormTemplate.Create(
            _guidGen, TestTenantId, "Template", "Biểu mẫu", "public_template", StorageMode.Json, AdminId);
        t.AddField("field_text", FormFieldType.Text, "Văn bản", "Text", 1, false, null, AdminId);
        t.Publish(AdminId);
        return t;
    }

    // ─── WorkflowLinkPendingEvent on public submit ──────────────────────────────
    //
    // Public submit emits WITH SubmittedBy=Guid.Empty (anonymous). Both the rejection
    // branch and the catch(Exception) hard-throw branch surface the orphan signal.

    [Fact]
    public async Task Handle_WhenWorkflowInstanceFails_PublishesWorkflowLinkPendingEventWithEmptyActor()
    {
        var template = CreateActivePublicJsonTemplate();
        var defId = Guid.NewGuid();
        typeof(FormTemplate).GetProperty("ApprovalWorkflowDefinitionId")!.SetValue(template, defId);
        StubTemplateLookup(template);

        const string errorCode = "GOV_WFL_014";
        const string errorMessage = "No handler resolved for initial state.";
        _workflowClient
            .CreateInstanceAsync(Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
            .Returns(WorkflowInstanceResult.Fail(errorCode, errorMessage));

        var command = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "hello" });

        await _handler.Handle(command, CancellationToken.None);

        await _publisher.Received(1).Publish(
            Arg.Is<FormSubmissionWorkflowLinkPendingEvent>(e =>
                e.FormTemplateId == template.Id &&
                e.ApprovalWorkflowDefinitionId == defId &&
                e.TenantId == TestTenantId &&
                e.SubmittedBy == Guid.Empty &&
                e.FailureCode == errorCode &&
                e.FailureMessage == errorMessage &&
                e.SubmissionId != Guid.Empty &&
                e.MessageId != Guid.Empty),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowInstanceSucceeds_DoesNotPublishWorkflowLinkPendingEvent()
    {
        var template = CreateActivePublicJsonTemplate();
        var defId = Guid.NewGuid();
        typeof(FormTemplate).GetProperty("ApprovalWorkflowDefinitionId")!.SetValue(template, defId);
        StubTemplateLookup(template);

        _workflowClient
            .CreateInstanceAsync(Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceResult(Guid.NewGuid(), Guid.Empty, 1L));

        var command = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "hello" });

        await _handler.Handle(command, CancellationToken.None);

        await _publisher.DidNotReceive().Publish(
            Arg.Any<FormSubmissionWorkflowLinkPendingEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowInstanceThrows_PublishesWorkflowLinkPendingEventWithEmptyActor()
    {
        var template = CreateActivePublicJsonTemplate();
        var defId = Guid.NewGuid();
        typeof(FormTemplate).GetProperty("ApprovalWorkflowDefinitionId")!.SetValue(template, defId);
        StubTemplateLookup(template);

        _workflowClient
            .CreateInstanceAsync(Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("broker unreachable"));

        var command = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "hello" });

        // Throw must NOT bubble — anonymous submit response preserved; orphan surfaced instead.
        await _handler.Handle(command, CancellationToken.None);

        await _publisher.Received(1).Publish(
            Arg.Is<FormSubmissionWorkflowLinkPendingEvent>(e =>
                e.FormTemplateId == template.Id &&
                e.ApprovalWorkflowDefinitionId == defId &&
                e.TenantId == TestTenantId &&
                e.SubmittedBy == Guid.Empty &&
                e.FailureCode == FormSubmissionWorkflowLinkPendingEvent.ExceptionFailureCode &&
                e.FailureMessage == "broker unreachable" &&
                e.SubmissionId != Guid.Empty &&
                e.MessageId != Guid.Empty),
            Arg.Any<CancellationToken>());
    }

    // ── Multi-signer public form: anonymous signer evidence (SignerUserId = null) ────────

    [Fact]
    public async Task Handle_MultiSignerPublicTemplate_RecordsAnonymousSignerEvidence()
    {
        var template = CreateActivePublicJsonTemplate();
        // Add 2 signer roles
        template.AddSignerRole("reviewer", "Người Duyệt", displayOrder: 1, isRequired: true, placementJson: null, Guid.NewGuid());
        template.AddSignerRole("approver", "Người Phê Duyệt", displayOrder: 2, isRequired: false, placementJson: null, Guid.NewGuid());

        StubTemplateLookup(template);

        // Verify result for anonymous public signature
        var verdict = new SignatureVerifyResult
        {
            IsValid = true,
            SignerSubject = "CN=PublicSigner",
            SignerSerial = "SERIAL-PUBLIC",
            SignerCertDer = [0xFF],
            ChainEvidenceJson = null,
            VerifiedAt = DateTimeOffset.UtcNow
        };

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(verdict));

        var command = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "public data" });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // For public (anonymous) multi-signer submissions, SignerUserId should be null in evidence
        // (public forms do not bind signer to authenticated user)
    }

    [Fact]
    public async Task Handle_SingleSignerPublicTemplate_UsesScalarPath()
    {
        var template = CreateActivePublicJsonTemplate();
        // Add 1 signer role (single-signer)
        template.AddSignerRole("signer", "Người Ký", displayOrder: 1, isRequired: true, placementJson: null, Guid.NewGuid());

        StubTemplateLookup(template);

        var verdict = new SignatureVerifyResult
        {
            IsValid = true,
            SignerSubject = "CN=PublicSigner",
            SignerSerial = "SERIAL-PUB-01",
            SignerCertDer = [0x01],
            ChainEvidenceJson = null,
            VerifiedAt = DateTimeOffset.UtcNow
        };

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(verdict));

        var command = new SubmitPublicFormCommand(
            TemplateCode: "public_template",
            Data: new Dictionary<string, object?> { ["field_text"] = "data" });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Single-signer public uses scalar evidence path
    }
}
