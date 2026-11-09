using AqtCoreFW.Forms.Application.Commands.SubmitPublicForm;
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
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// FORMS-CONSENT-PRE-SPAN-ORPHAN: unit tests for public/anonymous submit consent atomicity.
///
/// Mirrors SubmitFormCommandHandlerConsentAtomicityTests but for the public path.
/// Proves that IConsentModuleClient.RecordConsentAsync is invoked INSIDE the atomic span
/// (after BeginAsync + SetTenant pin, before CommitAsync), BEFORE submissionRepository.AddAsync.
/// </summary>
public sealed class SubmitPublicFormCommandHandlerConsentAtomicityTests
{
    private readonly IReadDbConnection           _db;
    private readonly IFormTemplateRepository     _templateRepo;
    private readonly IFormSubmissionRepository   _submissionRepo;
    private readonly IFormValidationService      _validationService;
    private readonly IFormDataStorageFactory     _storageFactory;
    private readonly IFormDataStorage            _storage;
    private readonly IFormulaEvaluationService   _formulaService;
    private readonly ITableFieldDataRepository   _tableFieldRepo;
    private readonly IConsentModuleClient        _consentClient;
    private readonly IWorkflowModuleClient       _workflowClient;
    private readonly IPublisher                  _publisher;
    private readonly ICrossModuleUnitOfWork      _uow;
    private readonly IOutboxWriter               _outboxWriter;
    private readonly IGuidGenerator              _guidGen;
    private readonly IIdentitySigningClient      _signingClient;
    private readonly SubmitPublicFormCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();

    public SubmitPublicFormCommandHandlerConsentAtomicityTests()
    {
        _db                = Substitute.For<IReadDbConnection>();
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

        _consentClient
            .RecordConsentAsync(Arg.Any<RecordConsentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));

        _handler = new SubmitPublicFormCommandHandler(
            _guidGen,
            _db, _templateRepo, _submissionRepo, _validationService,
            _storageFactory, _formulaService, _tableFieldRepo,
            _consentClient, _workflowClient, _publisher,
            _uow, _outboxWriter,
            Substitute.For<ILogger<SubmitPublicFormCommandHandler>>(),
            _signingClient);
    }

    // --- Call ordering guard: consent INSIDE the span (public path) ---

    [Fact]
    public async Task Handle_WithConsentAndPiiFields_RecordsConsentInsideUowSpan()
    {
        var template = CreateActiveJsonTemplateWithPiiField();
        template.SetConsentConfig(requiresConsent: true, consentText: null, Guid.Empty);
        var templateIdRow = new SubmitPublicFormCommandHandler.TemplateIdRow
        {
            Id = template.Id,
            TenantId = TestTenantId,
            PublicId = template.PublicId
        };

        SetupDbLookup(template.Code, templateIdRow);
        SetupTemplate(template);

        var result = await _handler.Handle(
            BuildCommand(
                templateCode: template.Code,
                consentGiven: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Verify UoW was opened with tenant (public path pins SESSION_CONTEXT)
        await _uow.Received(1).BeginAsync(TestTenantId, Arg.Any<CancellationToken>());

        // Verify consent is called exactly once
        await _consentClient.Received(1).RecordConsentAsync(
            Arg.Any<RecordConsentRequest>(),
            Arg.Any<CancellationToken>());

        // Verify submission added
        await _submissionRepo.Received(1).AddAsync(
            Arg.Any<FormSubmission>(),
            Arg.Any<CancellationToken>());

        // Verify commit (not rollback)
        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PublicPath_AnonymousConsent_UsesGuidEmptyForDataSubjectId()
    {
        var template = CreateActiveJsonTemplateWithPiiField();
        template.SetConsentConfig(requiresConsent: true, consentText: null, Guid.Empty);
        var templateIdRow = new SubmitPublicFormCommandHandler.TemplateIdRow
        {
            Id = template.Id,
            TenantId = TestTenantId,
            PublicId = template.PublicId
        };

        SetupDbLookup(template.Code, templateIdRow);
        SetupTemplate(template);

        RecordConsentRequest? capturedRequest = null;
        await _consentClient.RecordConsentAsync(
            Arg.Do<RecordConsentRequest>(r => capturedRequest = r),
            Arg.Any<CancellationToken>());

        var result = await _handler.Handle(
            BuildCommand(
                templateCode: template.Code,
                consentGiven: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Verify consent was recorded with Guid.Empty for anonymous public path
        capturedRequest.Should().NotBeNull();
        capturedRequest!.DataSubjectId.Should().Be(Guid.Empty,
            "anonymous public submit uses Guid.Empty as subject ID");
        capturedRequest.TenantId.Should().Be(TestTenantId);
        capturedRequest.PurposeCode.Should().Be("form_submission");
    }

    [Fact]
    public async Task Handle_WithConsentAndPiiFields_ConsentRecordIdLinkedToSubmission()
    {
        var template = CreateActiveJsonTemplateWithPiiField();
        template.SetConsentConfig(requiresConsent: true, consentText: null, Guid.Empty);
        var templateIdRow = new SubmitPublicFormCommandHandler.TemplateIdRow
        {
            Id = template.Id,
            TenantId = TestTenantId,
            PublicId = template.PublicId
        };

        SetupDbLookup(template.Code, templateIdRow);
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
                templateCode: template.Code,
                consentGiven: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // The submission passed to AddAsync must have ConsentRecordId set BEFORE insertion
        capturedSubmission.Should().NotBeNull();
        capturedSubmission!.ConsentRecordId.Should().Be(consentRecordId,
            "ConsentRecordId must be linked before AddAsync (FK constraint)");
    }

    [Fact]
    public async Task Handle_NoConsentNeeded_DoesNotCallConsentClient()
    {
        var template = CreateActiveJsonTemplate();
        var templateIdRow = new SubmitPublicFormCommandHandler.TemplateIdRow
        {
            Id = template.Id,
            TenantId = TestTenantId,
            PublicId = template.PublicId
        };

        SetupDbLookup(template.Code, templateIdRow);
        SetupTemplate(template);

        var result = await _handler.Handle(
            BuildCommand(
                templateCode: template.Code,
                consentGiven: false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Consent client must NOT be called when no PII
        await _consentClient.DidNotReceiveWithAnyArgs().RecordConsentAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ConsentRecordThrows_RollsBackUow()
    {
        var template = CreateActiveJsonTemplateWithPiiField();
        template.SetConsentConfig(requiresConsent: true, consentText: null, Guid.Empty);
        var templateIdRow = new SubmitPublicFormCommandHandler.TemplateIdRow
        {
            Id = template.Id,
            TenantId = TestTenantId,
            PublicId = template.PublicId
        };

        SetupDbLookup(template.Code, templateIdRow);
        SetupTemplate(template);

        _consentClient
            .RecordConsentAsync(Arg.Any<RecordConsentRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("consent service unavailable"));

        Func<Task> act = () => _handler.Handle(
            BuildCommand(
                templateCode: template.Code,
                consentGiven: true),
            CancellationToken.None);

        // The throw inside the span should bubble
        await act.Should().ThrowAsync<InvalidOperationException>();

        // UoW must be rolled back
        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());

        // Submission must NOT be added
        await _submissionRepo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    // --- Helpers ---

    private void SetupDbLookup(string code, SubmitPublicFormCommandHandler.TemplateIdRow row)
    {
        _db.QueryFirstOrDefaultWithPublicFormsBypassAsync<SubmitPublicFormCommandHandler.TemplateIdRow>(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SubmitPublicFormCommandHandler.TemplateIdRow?>(row));
    }

    private void SetupTemplate(FormTemplate template)
    {
        _templateRepo
            .GetPublicWithFieldsAsync(template.Id, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));
    }

    private FormTemplate CreateTemplate(StorageMode mode, bool addTextField)
    {
        var t = FormTemplate.Create(
            _guidGen, TestTenantId, "Template", "Biểu mẫu", $"public_code_{Guid.NewGuid():N}"[..15], mode, Guid.Empty);
        if (addTextField)
            t.AddField("field_text", FormFieldType.Text, "Văn bản", "Text", 1, false, null, Guid.Empty);
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
        t.AddField("ssn", FormFieldType.Text, "CMND", "SSN", 1, true, null, Guid.Empty, isPii: true);
        PublishJson(t);
        return t;
    }

    private static void PublishJson(FormTemplate t) => t.Publish(Guid.Empty);

    private SubmitPublicFormCommand BuildCommand(
        string? templateCode = null,
        bool consentGiven = false,
        Dictionary<string, object?>? data = null) =>
        new(
            templateCode ?? "UNKNOWN",
            data ?? new Dictionary<string, object?>(),
            ConsentGiven: consentGiven,
            Signature: null,
            ClientIp: "127.0.0.1",
            UserAgent: "UnitTest");
}
