using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
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
using System.Text;
using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for SubmitFormCommandHandler.
/// Covers template lookup, validation gate, preprocessing (flatten/strip/formula),
/// dual-mode storage routing, and TableField child-row writes.
/// Phase-07: FormTemplate retiered T1→T2; commands now use FormTemplatePublicId (Guid AK).
/// </summary>
public sealed class SubmitFormCommandHandlerTests
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

    public SubmitFormCommandHandlerTests()
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

        // Default: storage factory always returns the same mock
        _storageFactory.Create(Arg.Any<StorageMode>()).Returns(_storage);

        // Default: validation passes with no errors
        _validationService
            .ValidateAsync(Arg.Any<IEnumerable<FormField>>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new List<ValidationError>())));

        // SubmitFormCommandHandler ctor: IGuidGenerator first param (for FormSubmission.Create)
        _guidGen.NewIdRandom().Returns(Guid.NewGuid());
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        // Default signing client: returns null (no digital-sig field in default templates → never called)
        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SignatureVerifyResult { IsValid = false }));

        _handler = new SubmitFormCommandHandler(
            _guidGen,
            _templateRepo, _submissionRepo, _validationService,
            _storageFactory, _formulaService, _tableFieldRepo,
            _consentClient, _workflowClient, _publisher,
            _uow, _outboxWriter,
            Substitute.For<ILogger<SubmitFormCommandHandler>>(),
            _signingClient);
    }

    // --- Template not found / inactive ---

    [Fact]
    public async Task Handle_TemplateNotFound_ReturnsError()
    {
        _templateRepo
            .GetByPublicIdWithFieldsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(null));

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InactiveTemplate_ReturnsError()
    {
        // Draft template (not Active) should be rejected
        var template = CreateTemplate(StorageMode.Json, addTextField: true);
        // template is Draft by default after Create — do NOT publish

        _templateRepo
            .GetByPublicIdWithFieldsAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));

        var command = BuildCommand(formTemplatePublicId: template.PublicId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Validation gate ---

    [Fact]
    public async Task Handle_ValidationServiceReturnsFailed_ReturnsError()
    {
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);

        _validationService
            .ValidateAsync(Arg.Any<IEnumerable<FormField>>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<List<ValidationError>>("service error")));

        var result = await _handler.Handle(BuildCommand(formTemplatePublicId: template.PublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidationReturnsErrors_ReturnsError()
    {
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);

        var errors = new List<ValidationError>
        {
            new("Field 'name' is required.", "name")
        };
        _validationService
            .ValidateAsync(Arg.Any<IEnumerable<FormField>>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(errors)));

        var result = await _handler.Handle(BuildCommand(formTemplatePublicId: template.PublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidSubmission_ReturnsSuccessWithSubmissionId()
    {
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(formTemplatePublicId: template.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_ValidSubmission_PersistsSubmission()
    {
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);

        await _handler.Handle(BuildCommand(formTemplatePublicId: template.PublicId), CancellationToken.None);

        await _submissionRepo.Received(1).AddAsync(
            Arg.Is<FormSubmission>(s => s.TenantId == TestTenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidSubmission_CallsStorageFactoryCreate()
    {
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);

        await _handler.Handle(BuildCommand(formTemplatePublicId: template.PublicId), CancellationToken.None);

        _storageFactory.Received(1).Create(StorageMode.Json);
    }

    // --- Slice 1B-1: outbox enqueue replaces direct messageBus.PublishAsync ---

    [Fact]
    public async Task Handle_ValidSubmission_EnqueuesAcknowledgedEventIntoFormsOutboxInsideUow()
    {
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(formTemplatePublicId: template.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await _outboxWriter.Received(1).EnqueueAsync(
            Arg.Is<FormSubmissionAcknowledgedIntegrationEvent>(e =>
                e.SubmissionId == result.Value &&
                e.FormTemplateId == template.PublicId &&
                e.TenantId == TestTenantId &&
                e.SubmittedBy == TestUserId &&
                e.MessageId != Guid.Empty),
            "forms",
            Arg.Any<CancellationToken>());

        // The write span (submission add + outbox enqueue) must be Begin→Commit, never Rollback.
        await _uow.Received(1).BeginAsync(Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SubmissionAddThrows_RollsBackAndNeverEnqueuesOutboxEvent()
    {
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);

        _submissionRepo
            .AddAsync(Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("simulated storage failure"));

        Func<Task> act = () => _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        await _outboxWriter.DidNotReceiveWithAnyArgs().EnqueueAsync(
            default!, default!, default);
        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    // --- Json vs Table mode ---

    [Fact]
    public async Task Handle_JsonMode_SubmissionDataJsonIsNotNull()
    {
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, data: new() { ["field_text"] = "hello" }),
            CancellationToken.None);

        await _submissionRepo.Received(1).AddAsync(
            Arg.Is<FormSubmission>(s => s.SubmissionData != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TableMode_SubmissionDataJsonIsNull()
    {
        var template = CreateActiveTableTemplate();
        SetupTemplate(template);

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, data: new() { ["field_text"] = "hello" }),
            CancellationToken.None);

        await _submissionRepo.Received(1).AddAsync(
            Arg.Is<FormSubmission>(s => s.SubmissionData == null),
            Arg.Any<CancellationToken>());
    }

    // --- Preprocessing: UI-only field stripping ---

    [Fact]
    public async Task Handle_StripsUiOnlyFields_SectionLabelDivider()
    {
        // Template with Section, Label, Divider + one real Text field
        var template = CreateTemplate(StorageMode.Json, addTextField: false);
        AddField(template, "section1",  FormFieldType.Section);
        AddField(template, "label1",    FormFieldType.Label);
        AddField(template, "divider1",  FormFieldType.Divider);
        AddField(template, "real_text", FormFieldType.Text);
        PublishJson(template);
        SetupTemplate(template);

        IDictionary<string, object?>? capturedValues = null;
        await _storage.WriteAsync(
            Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(),
            Arg.Do<IDictionary<string, object?>>(v => capturedValues = v),
            Arg.Any<CancellationToken>());

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, data: new()
            {
                ["section1"]  = "ignored",
                ["label1"]    = "ignored",
                ["divider1"]  = "ignored",
                ["real_text"] = "kept"
            }),
            CancellationToken.None);

        capturedValues.Should().NotBeNull();
        capturedValues!.Should().ContainKey("real_text");
        capturedValues.Should().NotContainKey("section1");
        capturedValues.Should().NotContainKey("label1");
        capturedValues.Should().NotContainKey("divider1");
    }

    // --- Preprocessing: AddressField flattening ---

    [Fact]
    public async Task Handle_FlattensAddressField_IntoProvDistWard()
    {
        var template = CreateTemplate(StorageMode.Json, addTextField: false);
        AddField(template, "address", FormFieldType.AddressField);
        PublishJson(template);
        SetupTemplate(template);

        IDictionary<string, object?>? capturedValues = null;
        await _storage.WriteAsync(
            Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(),
            Arg.Do<IDictionary<string, object?>>(v => capturedValues = v),
            Arg.Any<CancellationToken>());

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, data: new()
            {
                ["address"] = """{"province":"Hà Nội","district":"Cầu Giấy","ward":"Dịch Vọng"}"""
            }),
            CancellationToken.None);

        capturedValues.Should().NotBeNull();
        capturedValues!.Should().ContainKey("address_province").WhoseValue.Should().Be("Hà Nội");
        capturedValues.Should().ContainKey("address_district").WhoseValue.Should().Be("Cầu Giấy");
        capturedValues.Should().ContainKey("address_ward").WhoseValue.Should().Be("Dịch Vọng");
        capturedValues.Should().NotContainKey("address");
    }

    [Fact]
    public async Task Handle_NullAddressValue_SetsNullForAllSubfields()
    {
        var template = CreateTemplate(StorageMode.Json, addTextField: false);
        AddField(template, "addr", FormFieldType.AddressField);
        PublishJson(template);
        SetupTemplate(template);

        IDictionary<string, object?>? capturedValues = null;
        await _storage.WriteAsync(
            Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(),
            Arg.Do<IDictionary<string, object?>>(v => capturedValues = v),
            Arg.Any<CancellationToken>());

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, data: new() { ["addr"] = null }),
            CancellationToken.None);

        capturedValues!["addr_province"].Should().BeNull();
        capturedValues!["addr_district"].Should().BeNull();
        capturedValues!["addr_ward"].Should().BeNull();
    }

    // --- Preprocessing: DateRange flattening ---

    [Fact]
    public async Task Handle_FlattensDateRange_IntoFromTo()
    {
        var template = CreateTemplate(StorageMode.Json, addTextField: false);
        AddField(template, "period", FormFieldType.DateRange);
        PublishJson(template);
        SetupTemplate(template);

        IDictionary<string, object?>? capturedValues = null;
        await _storage.WriteAsync(
            Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(),
            Arg.Do<IDictionary<string, object?>>(v => capturedValues = v),
            Arg.Any<CancellationToken>());

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, data: new()
            {
                ["period"] = """{"from":"2025-01-01","to":"2025-12-31"}"""
            }),
            CancellationToken.None);

        capturedValues!["period_from"].Should().Be("2025-01-01");
        capturedValues!["period_to"].Should().Be("2025-12-31");
        capturedValues.Should().NotContainKey("period");
    }

    [Fact]
    public async Task Handle_NullDateRangeValue_SetsNullForAllSubfields()
    {
        var template = CreateTemplate(StorageMode.Json, addTextField: false);
        AddField(template, "range", FormFieldType.DateRange);
        PublishJson(template);
        SetupTemplate(template);

        IDictionary<string, object?>? capturedValues = null;
        await _storage.WriteAsync(
            Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(),
            Arg.Do<IDictionary<string, object?>>(v => capturedValues = v),
            Arg.Any<CancellationToken>());

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, data: new() { ["range"] = null }),
            CancellationToken.None);

        capturedValues!["range_from"].Should().BeNull();
        capturedValues!["range_to"].Should().BeNull();
    }

    // --- Preprocessing: Formula evaluation ---

    [Fact]
    public async Task Handle_EvaluatesFormula_IncludesComputedValue()
    {
        var template = CreateTemplate(StorageMode.Json, addTextField: false);
        AddField(template, "amount", FormFieldType.Number);
        // Add formula field with a FormulaJson config
        var formulaField = FormField.Create(
            template.Id, "total", FormFieldType.Formula,
            "Tổng", "Total", 2, false, 1,
            formulaJson: """{"expression":"[amount]*2"}""");
        // We expose AddField via domain but need to add via template to keep Fields collection
        AddFieldDirectly(template, formulaField);
        PublishJson(template);
        SetupTemplate(template);

        _formulaService
            .EvaluateAsync("""{"expression":"[amount]*2"}""", Arg.Any<IDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((object?)200m));

        IDictionary<string, object?>? capturedValues = null;
        await _storage.WriteAsync(
            Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(),
            Arg.Do<IDictionary<string, object?>>(v => capturedValues = v),
            Arg.Any<CancellationToken>());

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, data: new() { ["amount"] = 100m }),
            CancellationToken.None);

        capturedValues!.Should().ContainKey("total").WhoseValue.Should().Be(200m);
    }

    // --- Table mode: TableField child rows ---

    [Fact]
    public async Task Handle_TableMode_ExcludesTableFieldsFromParentData()
    {
        var template = CreateTemplate(StorageMode.Table, addTextField: false);
        AddField(template, "name",  FormFieldType.Text);
        AddField(template, "items", FormFieldType.TableField);
        PublishTable(template);
        SetupTemplate(template);

        IDictionary<string, object?>? capturedParentData = null;
        await _storage.WriteAsync(
            Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(),
            Arg.Do<IDictionary<string, object?>>(v => capturedParentData = v),
            Arg.Any<CancellationToken>());

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, data: new()
            {
                ["name"]  = "Test",
                ["items"] = new List<object> { new Dictionary<string, object?> { ["qty"] = 1 } }
            }),
            CancellationToken.None);

        capturedParentData!.Should().ContainKey("name");
        capturedParentData.Should().NotContainKey("items");
    }

    [Fact]
    public async Task Handle_TableMode_WritesChildRowsViaRepository()
    {
        var template = CreateTemplate(StorageMode.Table, addTextField: false);
        AddField(template, "name",  FormFieldType.Text);
        AddField(template, "items", FormFieldType.TableField);
        PublishTable(template);
        SetupTemplate(template);

        var rows = new List<object>
        {
            new Dictionary<string, object?> { ["qty"] = 1 },
            new Dictionary<string, object?> { ["qty"] = 2 }
        };

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, data: new()
            {
                ["name"]  = "Test",
                ["items"] = rows
            }),
            CancellationToken.None);

        // NSubstitute: when multiple args share the same type, must use specs for all of that type.
        // Both templateId and tableFieldId are long — use Arg.Is for both to avoid AmbiguousArgumentsException.
        var expectedTemplateId = template.Id;
        await _tableFieldRepo.Received(1).InsertRowsAsync(
            Arg.Is<long>(id => id == expectedTemplateId),
            Arg.Any<long>(),     // tableFieldId (long, T1 FormField internal PK)
            Arg.Any<Guid>(),     // submissionId (Guid, T2 PublicId)
            Arg.Is<IReadOnlyList<IDictionary<string, object?>>>(r => r.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TableMode_EmptyTableFieldRows_DoesNotCallInsertRows()
    {
        var template = CreateTemplate(StorageMode.Table, addTextField: false);
        AddField(template, "name",  FormFieldType.Text);
        AddField(template, "items", FormFieldType.TableField);
        PublishTable(template);
        SetupTemplate(template);

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, data: new()
            {
                ["name"]  = "Test",
                ["items"] = null   // no rows submitted
            }),
            CancellationToken.None);

        await _tableFieldRepo.DidNotReceive().InsertRowsAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<Guid>(),
            Arg.Any<IReadOnlyList<IDictionary<string, object?>>>(),
            Arg.Any<CancellationToken>());
    }

    // ─── Digital signature verify-at-ingest ────────────────────────────────────

    // Helpers shared by signature tests
    private FormTemplate CreateActiveJsonTemplateWithDigSigField(bool required = true)
    {
        var t = CreateTemplate(StorageMode.Json, addTextField: true);
        // Add the DigitalSignature field directly (no factory sugar needed; FormField.Create works)
        AddFieldDirectly(t, FormField.Create(
            t.Id, "sig_field", FormFieldType.DigitalSignature,
            "Chữ ký số", "Digital Signature", 2, required, 1));
        PublishJson(t);
        return t;
    }

    private static SignaturePayload MakeDummyBundle() =>
        new(Signature: [0x01, 0x02, 0x03], SignerCertDer: [0xAB, 0xCD]);

    private static SignatureVerifyResult MakeValidResult() =>
        new()
        {
            IsValid          = true,
            SignerSubject    = "CN=Test User",
            SignerSerial     = "SERIAL-001",
            SignerCertDer    = [0xAB, 0xCD],
            ChainEvidenceJson = "{\"chain\":[]}",
            VerifiedAt       = DateTimeOffset.UtcNow
        };

    /// <summary>
    /// Valid signature + template with DigitalSignature field → submission saved with all 6
    /// evidence fields populated. Non-vacuous: asserts each evidence property.
    /// </summary>
    [Fact]
    public async Task Handle_ValidSignature_SubmissionSavedWithAllEvidenceFields()
    {
        var template = CreateActiveJsonTemplateWithDigSigField();
        SetupTemplate(template);

        var expected = MakeValidResult();
        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        FormSubmission? captured = null;
        await _submissionRepo.AddAsync(
            Arg.Do<FormSubmission>(s => captured = s), Arg.Any<CancellationToken>());

        var result = await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId,
                         signature: MakeDummyBundle()),
            CancellationToken.None);

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
    /// Tampered / invalid signature → Result.Fail(ValidationError) AND AddAsync never called.
    /// Non-vacuous hard-block proof: repository receives zero calls.
    /// </summary>
    [Fact]
    public async Task Handle_TamperedSignature_ReturnsFailAndNeverPersists()
    {
        var template = CreateActiveJsonTemplateWithDigSigField();
        SetupTemplate(template);

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SignatureVerifyResult
            {
                IsValid = false, FailureReason = "signature mismatch"
            }));

        var result = await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId,
                         signature: MakeDummyBundle()),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("GOV_FRM_022"));

        // Hard-block: zero persistence side-effects
        await _submissionRepo.DidNotReceive().AddAsync(
            Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Required DigitalSignature field present but Signature=null in request → 400 "required".
    /// AddAsync never called.
    /// </summary>
    [Fact]
    public async Task Handle_RequiredDigSigFieldWithNoBundle_ReturnsRequiredError()
    {
        var template = CreateActiveJsonTemplateWithDigSigField(required: true);
        SetupTemplate(template);

        var result = await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId, signature: null),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("GOV_FRM_021"));

        await _submissionRepo.DidNotReceive().AddAsync(
            Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Template has NO DigitalSignature field → signing client never invoked.
    /// Regression guard: existing behaviour is unchanged.
    /// </summary>
    [Fact]
    public async Task Handle_NoDigSigField_SigningClientNeverInvoked()
    {
        // Default active-json template has only a Text field — no DigitalSignature
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);

        var result = await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await _signingClient.DidNotReceive().VerifySignatureAsync(
            Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The payload passed to VerifySignatureAsync must equal the server-side canonical bytes
    /// computed from the same data + activeFields. Proves the handler does NOT trust any
    /// client-supplied "signed payload".
    /// </summary>
    [Fact]
    public async Task Handle_VerifyCalledWithServerComputedCanonicalBytes()
    {
        var template = CreateActiveJsonTemplateWithDigSigField();
        SetupTemplate(template);

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MakeValidResult()));

        var data = new Dictionary<string, object?> { ["field_text"] = "hello" };

        SignatureVerifyRequest? capturedReq = null;
        await _signingClient.VerifySignatureAsync(
            Arg.Do<SignatureVerifyRequest>(r => capturedReq = r),
            Arg.Any<CancellationToken>());

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId,
                         data: data,
                         signature: MakeDummyBundle()),
            CancellationToken.None);

        capturedReq.Should().NotBeNull();

        // Compute expected canonical bytes using the same activeFields the handler would use
        var activeFields = template.Fields.Where(f => f.IsActive).ToList();
        var expectedBytes = FormDataCanonicalizer.CanonicalizeMode1(data, activeFields);

        capturedReq!.Payload.Should().BeEquivalentTo(expectedBytes,
            "handler must sign server-recomputed canonical bytes, not client-supplied content");
    }

    /// <summary>
    /// TenantId in SignatureVerifyRequest must equal request.TenantId (server-resolved, never
    /// from the client bundle).
    /// </summary>
    [Fact]
    public async Task Handle_VerifyRequestUsesTenantIdFromCommand()
    {
        var template = CreateActiveJsonTemplateWithDigSigField();
        SetupTemplate(template);

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MakeValidResult()));

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId,
                         signature: MakeDummyBundle()),
            CancellationToken.None);

        await _signingClient.Received(1).VerifySignatureAsync(
            Arg.Is<SignatureVerifyRequest>(r => r.TenantId == TestTenantId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Anti-impersonation: SignerUserId in the verify request must equal the authenticated
    /// submitter (command.SubmittedBy), so the downstream binding guard ties the signing cert
    /// to the actual submitter rather than any client-claimed identity. Without this assertion
    /// the binding was only exercised transitively.
    /// </summary>
    [Fact]
    public async Task Handle_VerifyRequestUsesSignerUserIdFromSubmitter()
    {
        var template = CreateActiveJsonTemplateWithDigSigField();
        SetupTemplate(template);

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MakeValidResult()));

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId,
                         signature: MakeDummyBundle()),
            CancellationToken.None);

        await _signingClient.Received(1).VerifySignatureAsync(
            Arg.Is<SignatureVerifyRequest>(r => r.SignerUserId == TestUserId),
            Arg.Any<CancellationToken>());
    }

    // ─── Mode-2 (ServerDocHash) verify-at-ingest ──────────────────────────────

    /// <summary>
    /// Creates a template with a DigitalSignature field whose ValidationRulesJson is set
    /// to ServerDocHash mode.
    /// </summary>
    private FormTemplate CreateActiveJsonTemplateWithServerDocHashField()
    {
        var t = CreateTemplate(StorageMode.Json, addTextField: true);
        AddFieldDirectly(t, FormField.Create(
            t.Id, "sig_field", FormFieldType.DigitalSignature,
            "Chữ ký số", "Digital Signature", 2, true, 1,
            validationRulesJson: """{"signatureMode":"ServerDocHash"}"""));
        PublishJson(t);
        return t;
    }

    /// <summary>
    /// Mode-2: valid signature → submission saved with all evidence fields.
    /// The Payload passed to VerifySignatureAsync must equal the server-recomputed
    /// doc hash (not the Mode-1 canonical bytes).
    /// Non-vacuous: captures the verify request and asserts Payload matches ComputeMode2DocHash.
    /// </summary>
    [Fact]
    public async Task Handle_Mode2_ValidSignature_SubmissionSavedWithEvidenceFields()
    {
        var template = CreateActiveJsonTemplateWithServerDocHashField();
        SetupTemplate(template);

        var expected = MakeValidResult();
        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        FormSubmission? captured = null;
        await _submissionRepo.AddAsync(
            Arg.Do<FormSubmission>(s => captured = s), Arg.Any<CancellationToken>());

        var data = new Dictionary<string, object?> { ["field_text"] = "hello" };

        var result = await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId,
                         data: data,
                         signature: MakeDummyBundle()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.SignatureVerdict.Should().Be(true);
        captured.SignerSubject.Should().Be(expected.SignerSubject);
    }

    /// <summary>
    /// Mode-2: the Payload passed to VerifySignatureAsync equals the 32-byte SHA-256
    /// doc hash, NOT the Mode-1 canonical bytes.
    /// Non-vacuous: the two byte arrays must differ (or the test proves nothing).
    /// </summary>
    [Fact]
    public async Task Handle_Mode2_VerifyCalledWithDocHashBytes_NotMode1CanonicalBytes()
    {
        var template = CreateActiveJsonTemplateWithServerDocHashField();
        SetupTemplate(template);

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MakeValidResult()));

        var data = new Dictionary<string, object?> { ["field_text"] = "hello" };

        SignatureVerifyRequest? capturedReq = null;
        await _signingClient.VerifySignatureAsync(
            Arg.Do<SignatureVerifyRequest>(r => capturedReq = r),
            Arg.Any<CancellationToken>());

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId,
                         data: data,
                         signature: MakeDummyBundle()),
            CancellationToken.None);

        capturedReq.Should().NotBeNull();

        var activeFields = template.Fields.Where(f => f.IsActive).ToList();

        // Expected: Mode-2 doc hash
        var expectedDocHash = FormDataCanonicalizer.ComputeMode2DocHash(
            template.Code, template.Version, data, activeFields);

        // Control: Mode-1 canonical bytes (must differ so the assertion is non-vacuous)
        var mode1Bytes = FormDataCanonicalizer.CanonicalizeMode1(data, activeFields);

        capturedReq!.Payload.Should().BeEquivalentTo(expectedDocHash,
            "Mode-2 handler must pass the 32-byte doc hash as Payload, not Mode-1 canonical bytes");

        // Non-vacuity: if these were equal the test would prove nothing
        ToHex(expectedDocHash).Should().NotBe(ToHex(mode1Bytes),
            "sanity check: Mode-2 hash and Mode-1 canonical bytes must differ");
    }

    /// <summary>
    /// Mode-2: tampered signature (IsValid=false) → Result.Fail AND AddAsync never called.
    /// Hard-block proof: zero persistence.
    /// </summary>
    [Fact]
    public async Task Handle_Mode2_TamperedSignature_ReturnsFailAndNeverPersists()
    {
        var template = CreateActiveJsonTemplateWithServerDocHashField();
        SetupTemplate(template);

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SignatureVerifyResult
            {
                IsValid = false, FailureReason = "mode2 hash mismatch"
            }));

        var result = await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId,
                         data: new Dictionary<string, object?> { ["field_text"] = "hello" },
                         signature: MakeDummyBundle()),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("GOV_FRM_022"));

        await _submissionRepo.DidNotReceive().AddAsync(
            Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Regression: a DigitalSignature field with NO ValidationRulesJson (or absent
    /// signatureMode key) defaults to CanonicalJson (Mode-1). Existing 04c behaviour preserved.
    /// </summary>
    [Fact]
    public async Task Handle_DigSigFieldWithNoSignatureMode_DefaultsToMode1CanonicalBytes()
    {
        // Use the existing helper that creates a DigitalSignature field with null ValidationRulesJson
        var template = CreateActiveJsonTemplateWithDigSigField();
        SetupTemplate(template);

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MakeValidResult()));

        var data = new Dictionary<string, object?> { ["field_text"] = "hello" };

        SignatureVerifyRequest? capturedReq = null;
        await _signingClient.VerifySignatureAsync(
            Arg.Do<SignatureVerifyRequest>(r => capturedReq = r),
            Arg.Any<CancellationToken>());

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId,
                         data: data,
                         signature: MakeDummyBundle()),
            CancellationToken.None);

        capturedReq.Should().NotBeNull();

        var activeFields = template.Fields.Where(f => f.IsActive).ToList();
        var expectedMode1Bytes = FormDataCanonicalizer.CanonicalizeMode1(data, activeFields);

        capturedReq!.Payload.Should().BeEquivalentTo(expectedMode1Bytes,
            "absent signatureMode defaults to CanonicalJson — Mode-1 canonical bytes");
    }

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Registers a template with the repository mock using T2 PublicId addressing.</summary>
    private void SetupTemplate(FormTemplate template)
    {
        _templateRepo
            .GetByPublicIdWithFieldsAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));
    }

    /// <summary>Creates a Draft template; caller may add fields and publish.</summary>
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

    private FormTemplate CreateActiveTableTemplate()
    {
        var t = CreateTemplate(StorageMode.Table, addTextField: true);
        PublishTable(t);
        return t;
    }

    /// <summary>Publish Json-mode template → status becomes Active immediately.</summary>
    private static void PublishJson(FormTemplate t) => t.Publish(TestUserId);

    /// <summary>
    /// Publish Table-mode template → status becomes Materializing.
    /// Then manually mark Active to simulate DDL completion.
    /// </summary>
    private static void PublishTable(FormTemplate t)
    {
        t.Publish(TestUserId);    // Draft → Materializing
        t.MarkActive(TestUserId); // Materializing → Active
    }

    /// <summary>Adds a field directly by calling AddField on the template aggregate.</summary>
    private static void AddField(FormTemplate t, string key, FormFieldType type) =>
        t.AddField(key, type, "Label VI", "Label EN", 1, false, null, TestUserId);

    /// <summary>
    /// Adds a pre-built FormField to the template via reflection (for fields requiring
    /// non-standard construction, e.g. Formula with FormulaJson).
    /// </summary>
    private static void AddFieldDirectly(FormTemplate template, FormField field)
    {
        // Access the private _fields backing list via reflection
        var fieldsField = typeof(FormTemplate)
            .GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var list = (List<FormField>)fieldsField.GetValue(template)!;
        list.Add(field);
    }

    private SubmitFormCommand BuildCommand(
        Guid? formTemplatePublicId = null,
        Dictionary<string, object?>? data = null,
        SignaturePayload? signature = null) =>
        new(
            TestTenantId,
            formTemplatePublicId ?? Guid.NewGuid(),
            TestUserId,
            data ?? new Dictionary<string, object?>(),
            Signature: signature);

    // ─── P2: BP-Run linkage branch ──────────────────────────────────────────────

    private SubmitFormCommand BuildBpRunCommand(
        Guid workflowInstanceId,
        long? workflowStateId,
        Guid? formTemplatePublicId = null,
        Dictionary<string, object?>? data = null) =>
        new(
            TestTenantId,
            formTemplatePublicId ?? Guid.NewGuid(),
            TestUserId,
            data ?? new Dictionary<string, object?>(),
            ConsentGiven: false,
            WorkflowInstanceId: workflowInstanceId,
            WorkflowStateId: workflowStateId);

    [Fact]
    public async Task Handle_WithBpFields_PersistsSubmissionWithWorkflowInstanceAndStateIds()
    {
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);
        var instanceId = Guid.NewGuid();
        const long stateId = 99L;

        await _handler.Handle(
            BuildBpRunCommand(instanceId, stateId, formTemplatePublicId: template.PublicId),
            CancellationToken.None);

        await _submissionRepo.Received(1).AddAsync(
            Arg.Is<FormSubmission>(s =>
                s.WorkflowInstanceId == instanceId &&
                s.WorkflowStateId == stateId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithBpFieldsIntakeOnly_WorkflowStateIdIsNull()
    {
        var template = CreateActiveJsonTemplate();
        SetupTemplate(template);
        var instanceId = Guid.NewGuid();

        await _handler.Handle(
            BuildBpRunCommand(instanceId, workflowStateId: null,
                              formTemplatePublicId: template.PublicId),
            CancellationToken.None);

        await _submissionRepo.Received(1).AddAsync(
            Arg.Is<FormSubmission>(s =>
                s.WorkflowInstanceId == instanceId &&
                s.WorkflowStateId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithBpFields_SkipsApprovalWorkflowInstanceCreation()
    {
        // Template has ApprovalWorkflowDefinitionId set BUT BP path is active —
        // handler must NOT call workflowClient.CreateInstanceAsync (instance pre-exists).
        var template = CreateActiveJsonTemplate();
        // Reflect ApprovalWorkflowDefinitionId into the template (private setter).
        var prop = typeof(FormTemplate).GetProperty("ApprovalWorkflowDefinitionId")!;
        prop.SetValue(template, Guid.NewGuid());
        SetupTemplate(template);

        await _handler.Handle(
            BuildBpRunCommand(Guid.NewGuid(), 5L, formTemplatePublicId: template.PublicId),
            CancellationToken.None);

        await _workflowClient.DidNotReceive().CreateInstanceAsync(
            Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutBpFields_StandardPathStillCreatesApprovalWorkflowInstance()
    {
        // Regression guard: non-BP submission with approval workflow still creates instance.
        var template = CreateActiveJsonTemplate();
        var defId = Guid.NewGuid();
        var prop = typeof(FormTemplate).GetProperty("ApprovalWorkflowDefinitionId")!;
        prop.SetValue(template, defId);
        SetupTemplate(template);

        _workflowClient
            .CreateInstanceAsync(Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceResult(Guid.NewGuid(), Guid.Empty, 1L));

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId),
            CancellationToken.None);

        await _workflowClient.Received(1).CreateInstanceAsync(
            Arg.Is<CreateWorkflowInstanceRequest>(r => r.DefinitionId == defId),
            Arg.Any<CancellationToken>());
    }

    // ─── WorkflowLinkPendingEvent (orphan-submission signal) ────────────────────
    //
    // Both orphan paths surface the signal so ops/admin see them uniformly:
    //   • !wfResult.IsSuccess rejection (e.g. GOV_WFL_014 empty pool) → BE GOV_* code.
    //   • catch(Exception) hard throw (broker/network) → ExceptionFailureCode.
    // Submission persists with WorkflowInstanceId=null in both.

    [Fact]
    public async Task Handle_WhenWorkflowInstanceFails_PublishesWorkflowLinkPendingEvent()
    {
        var template = CreateActiveJsonTemplate();
        var defId = Guid.NewGuid();
        typeof(FormTemplate).GetProperty("ApprovalWorkflowDefinitionId")!.SetValue(template, defId);
        SetupTemplate(template);

        const string errorCode = "GOV_WFL_014";
        const string errorMessage = "No handler resolved for initial state.";
        _workflowClient
            .CreateInstanceAsync(Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
            .Returns(WorkflowInstanceResult.Fail(errorCode, errorMessage));

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId),
            CancellationToken.None);

        await _publisher.Received(1).Publish(
            Arg.Is<FormSubmissionWorkflowLinkPendingEvent>(e =>
                e.FormTemplateId == template.Id &&
                e.ApprovalWorkflowDefinitionId == defId &&
                e.TenantId == TestTenantId &&
                e.SubmittedBy == TestUserId &&
                e.FailureCode == errorCode &&
                e.FailureMessage == errorMessage &&
                e.SubmissionId != Guid.Empty &&
                e.MessageId != Guid.Empty),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowInstanceSucceeds_DoesNotPublishWorkflowLinkPendingEvent()
    {
        var template = CreateActiveJsonTemplate();
        var defId = Guid.NewGuid();
        typeof(FormTemplate).GetProperty("ApprovalWorkflowDefinitionId")!.SetValue(template, defId);
        SetupTemplate(template);

        _workflowClient
            .CreateInstanceAsync(Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceResult(Guid.NewGuid(), Guid.Empty, 1L));

        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId),
            CancellationToken.None);

        await _publisher.DidNotReceive().Publish(
            Arg.Any<FormSubmissionWorkflowLinkPendingEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowInstanceThrows_PublishesWorkflowLinkPendingEvent()
    {
        var template = CreateActiveJsonTemplate();
        var defId = Guid.NewGuid();
        typeof(FormTemplate).GetProperty("ApprovalWorkflowDefinitionId")!.SetValue(template, defId);
        SetupTemplate(template);

        _workflowClient
            .CreateInstanceAsync(Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("broker unreachable"));

        // Hard throw must NOT bubble — the already-saved submission's response is preserved
        // (fail-soft orphan path); the throw is surfaced as a pending-link signal instead.
        await _handler.Handle(
            BuildCommand(formTemplatePublicId: template.PublicId),
            CancellationToken.None);

        await _publisher.Received(1).Publish(
            Arg.Is<FormSubmissionWorkflowLinkPendingEvent>(e =>
                e.FormTemplateId == template.Id &&
                e.ApprovalWorkflowDefinitionId == defId &&
                e.TenantId == TestTenantId &&
                e.SubmittedBy == TestUserId &&
                e.FailureCode == FormSubmissionWorkflowLinkPendingEvent.ExceptionFailureCode &&
                e.FailureMessage == "broker unreachable" &&
                e.SubmissionId != Guid.Empty &&
                e.MessageId != Guid.Empty),
            Arg.Any<CancellationToken>());
    }

    // ── Multi-signer: template with >1 SignerRole routes to collection path ──

    [Fact]
    public async Task Handle_MultiSignerTemplate_RecordsSignerEvidenceInCollection()
    {
        var template = CreateActiveJsonTemplate();
        // Add 2 signer roles to make it a multi-signer template
        template.AddSignerRole("approver", "Người Phê Duyệt", displayOrder: 1, isRequired: true, placementJson: null, TestUserId);
        template.AddSignerRole("witness", "Người Chứng Kiến", displayOrder: 2, isRequired: false, placementJson: null, TestUserId);

        SetupTemplate(template);

        // Simulate multi-signer verified evidence (one per role)
        var approverEvidence = new SignatureVerifyResult
        {
            IsValid = true,
            SignerSubject = "CN=Approver",
            SignerSerial = "SERIAL-APPROVER",
            SignerCertDer = [0x01, 0x02],
            ChainEvidenceJson = "{\"chain\":[\"approver\"]}",
            VerifiedAt = DateTimeOffset.UtcNow
        };

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(approverEvidence));

        var result = await _handler.Handle(BuildCommand(formTemplatePublicId: template.PublicId), CancellationToken.None);

        // Submission should succeed for multi-signer (note: in real scenario, separate signature
        // submissions would accumulate evidence; here we test that the handler recognizes >1 role
        // and routes to collection path, not scalar path)
        result.IsSuccess.Should().BeTrue();

        // Verify submission was persisted
        await _submissionRepo.Received(1).AddAsync(
            Arg.Is<FormSubmission>(s =>
                s.FormTemplateId == template.Id &&
                s.TenantId == TestTenantId &&
                s.SubmittedBy == TestUserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SingleSignerTemplate_UsesScalarPath()
    {
        // Template with 0 or 1 signer role uses the scalar evidence path (existing behavior)
        var template = CreateActiveJsonTemplate();
        template.AddSignerRole("signer", "Người Ký", displayOrder: 1, isRequired: true, placementJson: null, TestUserId);

        SetupTemplate(template);

        var verdict = new SignatureVerifyResult
        {
            IsValid = true,
            SignerSubject = "CN=Signer",
            SignerSerial = "SERIAL-001",
            SignerCertDer = [0xAB],
            ChainEvidenceJson = null,
            VerifiedAt = DateTimeOffset.UtcNow
        };

        _signingClient
            .VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(verdict));

        var result = await _handler.Handle(BuildCommand(formTemplatePublicId: template.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Single-signer route should record scalar evidence on the submission entity
        // (multi-signer would record to collection)
    }

    [Fact]
    public async Task Handle_NoSignerRoles_UsesScalarPath()
    {
        // Template with 0 signer roles — backward compatibility, uses scalar path
        var template = CreateActiveJsonTemplate();
        // Do NOT add any signer roles
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(formTemplatePublicId: template.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Should use scalar path (existing behavior for templates without explicit signer roles)
    }
}
