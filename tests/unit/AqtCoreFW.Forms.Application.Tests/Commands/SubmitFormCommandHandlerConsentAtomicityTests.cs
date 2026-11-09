using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// FORMS-CONSENT-PRE-SPAN-ORPHAN: unit tests for call ordering.
///
/// Proves that IConsentModuleClient.RecordConsentAsync is invoked INSIDE the atomic
/// ICrossModuleUnitOfWork span (after BeginAsync, before CommitAsync), BEFORE
/// submissionRepository.AddAsync. This guards against the orphan: a consent record
/// committed pre-span but the submission rolls back inside the span.
/// </summary>
public sealed class SubmitFormCommandHandlerConsentAtomicityTests
{
    private readonly IFormTemplateRepository   _templateRepo;
    private readonly IFormSubmissionRepository _submissionRepo;
    private readonly IFormValidationService    _validationService;
    private readonly IFormDataStorageFactory   _storageFactory;
    private readonly IFormDataStorage          _storage;
    private readonly IFormulaEvaluationService _formulaService;
    private readonly ITableFieldDataRepository _tableFieldRepo;
    private readonly IConsentModuleClient      _consentClient;
    private readonly IWorkflowModuleClient     _workflowClient;
    private readonly IPublisher                _publisher;
    private readonly ICrossModuleUnitOfWork    _uow;
    private readonly IOutboxWriter             _outboxWriter;
    private readonly IGuidGenerator            _guidGen;
    private readonly IIdentitySigningClient    _signingClient;
    private readonly SubmitFormCommandHandler  _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId   = Guid.NewGuid();

    public SubmitFormCommandHandlerConsentAtomicityTests()
    {
        _templateRepo      = Substitute.For<IFormTemplateRepository>();
        _submissionRepo    = Substitute.For<IFormSubmissionRepository>();
        _validationService = Substitute.For<IFormValidationService>();
        _storageFactory    = Substitute.For<IFormDataStorageFactory>();
        _storage           = Substitute.For<IFormDataStorage>();
        _formulaService    = Substitute.For<IFormulaEvaluationService>();
        _tableFieldRepo    = Substitute.For<ITableFieldDataRepository>();
        _consentClient     = Substitute.For<IConsentModuleClient>();
        _workflowClient    = Substitute.For<IWorkflowModuleClient>();
        _publisher         = Substitute.For<IPublisher>();
        _uow               = Substitute.For<ICrossModuleUnitOfWork>();
        _outboxWriter      = Substitute.For<IOutboxWriter>();
        _guidGen           = Substitute.For<IGuidGenerator>();
        _signingClient     = Substitute.For<IIdentitySigningClient>();

        _storageFactory.Create(Arg.Any<StorageMode>()).Returns(_storage);

        _validationService
            .ValidateAsync(Arg.Any<IEnumerable<FormField>>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new List<ValidationError>())));

        _guidGen.NewIdRandom().Returns(Guid.NewGuid());
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SignatureVerifyResult { IsValid = false }));

        // Default: ConsentRepository will return a Guid (the consent record ID)
        _consentClient
            .RecordConsentAsync(Arg.Any<RecordConsentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));

        _handler = new SubmitFormCommandHandler(
            _guidGen,
            _templateRepo, _submissionRepo, _validationService,
            _storageFactory, _formulaService, _tableFieldRepo,
            _consentClient, _workflowClient, _publisher,
            _uow, _outboxWriter,
            Substitute.For<ILogger<SubmitFormCommandHandler>>(),
            _signingClient);
    }

    // --- Call ordering guard: consent INSIDE the span ---

    [Fact]
    public async Task Handle_WithConsentAndPiiFields_RecordsConsentInsideUowSpan()
    {
        var template = CreateActiveJsonTemplateWithPiiField();
        // Enable consent requirement via domain method
        template.SetConsentConfig(requiresConsent: true, consentText: null, TestUserId);
        SetupTemplate(template);

        var result = await _handler.Handle(
            BuildCommand(
                formTemplatePublicId: template.PublicId,
                consentGiven: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Verify consent is called exactly once
        await _consentClient.Received(1).RecordConsentAsync(
            Arg.Any<RecordConsentRequest>(),
            Arg.Any<CancellationToken>());

        // Verify UoW was opened and committed (not rolled back)
        await _uow.Received(1).BeginAsync(Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithConsentAndPiiFields_ConsentRecordIdLinkedToSubmission()
    {
        var template = CreateActiveJsonTemplateWithPiiField();
        template.SetConsentConfig(requiresConsent: true, consentText: null, TestUserId);
        SetupTemplate(template);

        var consentRecordId = Guid.NewGuid();
        _consentClient
            .RecordConsentAsync(Arg.Any<RecordConsentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(consentRecordId));

        FormSubmission? capturedSubmission = null;
        await _submissionRepo.AddAsync(
            Arg.Do<FormSubmission>(s => capturedSubmission = s),
            Arg.Any<CancellationToken>());

        var result = await _handler.Handle(
            BuildCommand(
                formTemplatePublicId: template.PublicId,
                consentGiven: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // The submission passed to AddAsync must have ConsentRecordId set BEFORE insertion
        capturedSubmission.Should().NotBeNull();
        capturedSubmission!.ConsentRecordId.Should().Be(consentRecordId,
            "ConsentRecordId must be linked before AddAsync (FK constraint validation)");
    }

    [Fact]
    public async Task Handle_NoConsentNeeded_DoesNotCallConsentClient()
    {
        // Template without PII or without consent requirement
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);

        var result = await _handler.Handle(
            BuildCommand(
                formTemplatePublicId: template.PublicId,
                consentGiven: false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Consent client must NOT be called
        await _consentClient.DidNotReceiveWithAnyArgs().RecordConsentAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ConsentGivenFalseWithPii_DoesNotRecordConsent()
    {
        // Template requires consent + has PII, but ConsentGiven=false
        var template = CreateActiveJsonTemplateWithPiiField();
        template.SetConsentConfig(requiresConsent: true, consentText: null, TestUserId);
        SetupTemplate(template);

        // The validation gate should reject this pre-span, so consent client never called
        _validationService
            .ValidateAsync(Arg.Any<IEnumerable<FormField>>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<List<ValidationError>>(
                new ValidationError("GOV_FRM_020: Yêu cầu đồng ý xử lý dữ liệu cá nhân.", "ConsentGiven"))));

        var result = await _handler.Handle(
            BuildCommand(
                formTemplatePublicId: template.PublicId,
                consentGiven: false),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue("validation gate rejects missing consent");

        // Consent client must NOT be called for a validation failure
        await _consentClient.DidNotReceiveWithAnyArgs().RecordConsentAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ConsentRecordThrows_RollsBackUow()
    {
        var template = CreateActiveJsonTemplateWithPiiField();
        template.SetConsentConfig(requiresConsent: true, consentText: null, TestUserId);
        SetupTemplate(template);

        _consentClient
            .RecordConsentAsync(Arg.Any<RecordConsentRequest>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("consent service unavailable"));

        Func<Task> act = () => _handler.Handle(
            BuildCommand(
                formTemplatePublicId: template.PublicId,
                consentGiven: true),
            CancellationToken.None);

        // The throw inside the span should bubble (consent is INSIDE, not guarded)
        await act.Should().ThrowAsync<InvalidOperationException>();

        // UoW must be rolled back
        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());

        // Submission must NOT be added
        await _submissionRepo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_WithConsentAndPiiFields_RecordsConsentWithSubmitterSubjectId()
    {
        var template = CreateActiveJsonTemplateWithPiiField();
        template.SetConsentConfig(requiresConsent: true, consentText: null, TestUserId);
        SetupTemplate(template);

        RecordConsentRequest? capturedRequest = null;
        await _consentClient.RecordConsentAsync(
            Arg.Do<RecordConsentRequest>(r => capturedRequest = r),
            Arg.Any<CancellationToken>());

        var result = await _handler.Handle(
            BuildCommand(
                formTemplatePublicId: template.PublicId,
                consentGiven: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Verify consent was called with proper subject ID
        capturedRequest.Should().NotBeNull();
        capturedRequest!.DataSubjectId.Should().Be(TestUserId, "authenticated path uses the submitter ID");
    }

    // --- Helpers (mirror SubmitFormCommandHandlerTests) ---

    private void SetupTemplate(FormTemplate template)
    {
        _templateRepo
            .GetByPublicIdWithFieldsAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));
    }

    private FormTemplate CreateTemplate(StorageMode mode, bool addTextField)
    {
        var t = FormTemplate.Create(
            _guidGen, TestTenantId, "Template", "Biểu mẫu", "template_code", mode, TestUserId);
        if (addTextField)
            t.AddField("field_text", FormFieldType.Text, "Văn bản", "Text", 1, false, null, TestUserId);
        return t;
    }

    private FormTemplate CreateActiveJsonTemplate()
    {
        var t = CreateTemplate(StorageMode.Json, addTextField: true);
        PublishJson(t);
        return t;
    }

    private FormTemplate CreateActiveJsonTemplateWithPiiField()
    {
        var t = CreateTemplate(StorageMode.Json, addTextField: false);
        // AddField(fieldKey, type, labelVi, labelEn, displayOrder, required, validationRulesJson,
        // updatedBy, ...) — isPii is a later NAMED optional; it MUST be passed by name or the field
        // defaults to isPii:false and the handler's hasPiiFields check never triggers the consent path.
        t.AddField("ssn", FormFieldType.Text, "CMND", "SSN", 1, true, null, TestUserId, isPii: true);
        PublishJson(t);
        return t;
    }

    private static void PublishJson(FormTemplate t) => t.Publish(TestUserId);

    private SubmitFormCommand BuildCommand(
        Guid? formTemplatePublicId = null,
        bool consentGiven = false,
        Dictionary<string, object?>? data = null) =>
        new(
            TestTenantId,
            formTemplatePublicId ?? Guid.NewGuid(),
            TestUserId,
            data ?? new Dictionary<string, object?>(),
            ConsentGiven: consentGiven);
}
