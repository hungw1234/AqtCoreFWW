using AqtCoreFW.BusinessProcess.Application.Queries.GetBpRunSubmissions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Queries;

/// <summary>
/// Unit tests for the BP-Run submitted-data read. Pins: cross-tenant guard, empty run, happy-path
/// label+value assembly, snapshot-as-label-authority (drop data-only keys, null missing values),
/// intake-first ordering, and missing-snapshot skip (other forms still render).
/// </summary>
public sealed class GetBpRunSubmissionsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();

    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IFormSubmissionRunClient _submissionRunClient = Substitute.For<IFormSubmissionRunClient>();
    private readonly IFormTemplateQueryClient _formTemplateClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly GetBpRunSubmissionsQueryHandler _handler;

    public GetBpRunSubmissionsQueryHandlerTests()
    {
        _tenantContext.TenantId.Returns(TenantId);
        _tenantContext.IsSystemAdmin.Returns(false);
        _handler = new GetBpRunSubmissionsQueryHandler(
            _tenantContext,
            _submissionRunClient,
            _formTemplateClient,
            NullLogger<GetBpRunSubmissionsQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_CrossTenant_Fails_NoClientCalls()
    {
        var result = await _handler.Handle(
            new GetBpRunSubmissionsQuery(InstanceId, OtherTenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _submissionRunClient.DidNotReceiveWithAnyArgs().GetLatestForRunAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_RunWithNoSubmissions_ReturnsEmptyList()
    {
        _submissionRunClient.GetLatestForRunAsync(InstanceId, TenantId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FormSubmissionSummaryDto>)Array.Empty<FormSubmissionSummaryDto>());

        var result = await _handler.Handle(
            new GetBpRunSubmissionsQuery(InstanceId, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        await _formTemplateClient.DidNotReceiveWithAnyArgs()
            .GetVersionSchemaByPublicIdAsync(default, default, default, default);
    }

    [Fact]
    public async Task Handle_HappyPath_ResolvesLabelsValuesInDisplayOrder()
    {
        var formId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        ArrangeRun((formId, submissionId, Version: 1, StateId: null, SubmittedAt: DateTimeOffset.UtcNow));
        ArrangeSchema(formId, 1, """
            { "fields": [
                { "key": "phone", "labelVi": "Số điện thoại", "labelEn": "Phone", "displayOrder": 2 },
                { "key": "full_name", "labelVi": "Họ tên", "labelEn": "Full name", "displayOrder": 1 }
            ] }
            """);
        ArrangeData(submissionId, ("full_name", "Nguyễn A"), ("phone", "0900"));

        var result = await _handler.Handle(
            new GetBpRunSubmissionsQuery(InstanceId, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var sub = result.Value.Should().ContainSingle().Subject;
        sub.WorkflowStateId.Should().BeNull();
        // DisplayOrder: full_name (1) before phone (2).
        sub.Fields.Select(f => f.Key).Should().ContainInOrder("full_name", "phone");
        sub.Fields[0].LabelVi.Should().Be("Họ tên");
        sub.Fields[0].Value.Should().Be("Nguyễn A");
        sub.Fields[1].Value.Should().Be("0900");
    }

    [Fact]
    public async Task Handle_SnapshotIsLabelAuthority_DropsDataOnlyKeys_NullsMissingValues()
    {
        var formId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        ArrangeRun((formId, submissionId, Version: 2, StateId: null, SubmittedAt: DateTimeOffset.UtcNow));
        // Schema declares "kept" + "blank"; data carries "kept" + a stale "removed" key.
        ArrangeSchema(formId, 2, """
            { "fields": [
                { "key": "kept", "labelVi": "Giữ", "labelEn": "Kept", "displayOrder": 1 },
                { "key": "blank", "labelVi": "Trống", "labelEn": "Blank", "displayOrder": 2 }
            ] }
            """);
        ArrangeData(submissionId, ("kept", "x"), ("removed", "stale"));

        var result = await _handler.Handle(
            new GetBpRunSubmissionsQuery(InstanceId, TenantId), CancellationToken.None);

        var sub = result.Value.Should().ContainSingle().Subject;
        sub.Fields.Select(f => f.Key).Should().Equal("kept", "blank"); // "removed" dropped
        sub.Fields.Single(f => f.Key == "kept").Value.Should().Be("x");
        sub.Fields.Single(f => f.Key == "blank").Value.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SurfacesFieldType_ForTypeAwareRendering()
    {
        var formId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        ArrangeRun((formId, submissionId, Version: 1, StateId: null, SubmittedAt: DateTimeOffset.UtcNow));
        ArrangeSchema(formId, 1, """
            { "fields": [
                { "key": "note", "type": "Text", "labelVi": "Ghi chú", "labelEn": "Note", "displayOrder": 1 },
                { "key": "attachment", "type": "File", "labelVi": "Tệp", "labelEn": "File", "displayOrder": 2 },
                { "key": "sign", "type": "Signature", "labelVi": "Chữ ký", "labelEn": "Signature", "displayOrder": 3 }
            ] }
            """);
        ArrangeData(submissionId, ("note", "x"), ("attachment", "f1"), ("sign", "data:image/png;base64,AAA"));

        var result = await _handler.Handle(
            new GetBpRunSubmissionsQuery(InstanceId, TenantId), CancellationToken.None);

        var sub = result.Value.Should().ContainSingle().Subject;
        sub.Fields.Single(f => f.Key == "note").Type.Should().Be("Text");
        sub.Fields.Single(f => f.Key == "attachment").Type.Should().Be("File");
        sub.Fields.Single(f => f.Key == "sign").Type.Should().Be("Signature");
    }

    [Fact]
    public async Task Handle_SnapshotWithoutTypeKey_CoercesTypeToEmpty()
    {
        var formId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        ArrangeRun((formId, submissionId, Version: 1, StateId: null, SubmittedAt: DateTimeOffset.UtcNow));
        // Snapshot predating the type key — must not NRE; Type coerces to "".
        ArrangeSchema(formId, 1, """
            { "fields": [ { "key": "legacy", "labelVi": "Cũ", "labelEn": "Legacy", "displayOrder": 1 } ] }
            """);
        ArrangeData(submissionId, ("legacy", "v"));

        var result = await _handler.Handle(
            new GetBpRunSubmissionsQuery(InstanceId, TenantId), CancellationToken.None);

        var sub = result.Value.Should().ContainSingle().Subject;
        sub.Fields.Single(f => f.Key == "legacy").Type.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SignedSubmission_SurfacesEvidence_CnOnly()
    {
        var formId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var verifiedAt = DateTimeOffset.UtcNow;
        _submissionRunClient.GetLatestForRunAsync(InstanceId, TenantId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FormSubmissionSummaryDto>)new[]
            {
                new FormSubmissionSummaryDto(
                    submissionId, TenantId, formId, FormTemplateVersion: 1,
                    WorkflowInstanceId: InstanceId, WorkflowStateId: null,
                    SubmittedAt: DateTimeOffset.UtcNow, SubmittedBy: Guid.NewGuid(), Status: "Pending",
                    SignatureVerdict: true,
                    SignerSubject: "OID.0.9.2342.19200300.100.1.1=CCCD:017091004878, CN=ĐINH QUỐC VIỆT, L=PHÚ THỌ, C=VN",
                    SignatureVerifiedAt: verifiedAt),
            });
        ArrangeSchema(formId, 1, """
            { "fields": [ { "key": "ds", "type": "DigitalSignature", "labelVi": "Chữ ký số", "labelEn": "Digital signature", "displayOrder": 1 } ] }
            """);
        ArrangeData(submissionId); // DigitalSignature stores no field value

        var result = await _handler.Handle(
            new GetBpRunSubmissionsQuery(InstanceId, TenantId), CancellationToken.None);

        var sub = result.Value.Should().ContainSingle().Subject;
        sub.IsSigned.Should().BeTrue();
        sub.SignerName.Should().Be("ĐINH QUỐC VIỆT"); // CN only — CCCD/locality stripped
        sub.SignatureVerifiedAt.Should().Be(verifiedAt);
    }

    [Fact]
    public async Task Handle_UnsignedSubmission_IsSignedFalse_NoSignerName()
    {
        var formId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        ArrangeRun((formId, submissionId, Version: 1, StateId: null, SubmittedAt: DateTimeOffset.UtcNow));
        ArrangeSchema(formId, 1, """{ "fields": [ { "key": "a", "type": "Text", "labelVi": "A", "labelEn": "A", "displayOrder": 1 } ] }""");
        ArrangeData(submissionId, ("a", "x"));

        var result = await _handler.Handle(
            new GetBpRunSubmissionsQuery(InstanceId, TenantId), CancellationToken.None);

        var sub = result.Value.Should().ContainSingle().Subject;
        sub.IsSigned.Should().BeFalse();
        sub.SignerName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_IntakeBeforeStateForm_OrdersIntakeFirst()
    {
        var intakeForm = Guid.NewGuid();
        var stateForm = Guid.NewGuid();
        var intakeSub = Guid.NewGuid();
        var stateSub = Guid.NewGuid();
        // State form submitted EARLIER in wall-clock, intake later — ordering must still put intake first.
        ArrangeRun(
            (stateForm, stateSub, Version: 1, StateId: 21L, SubmittedAt: DateTimeOffset.UtcNow.AddMinutes(-5)),
            (intakeForm, intakeSub, Version: 1, StateId: null, SubmittedAt: DateTimeOffset.UtcNow));
        ArrangeSchema(intakeForm, 1, """{ "fields": [ { "key": "a", "labelVi": "A", "labelEn": "A", "displayOrder": 1 } ] }""");
        ArrangeSchema(stateForm, 1, """{ "fields": [ { "key": "b", "labelVi": "B", "labelEn": "B", "displayOrder": 1 } ] }""");
        ArrangeData(intakeSub, ("a", 1));
        ArrangeData(stateSub, ("b", 2));

        var result = await _handler.Handle(
            new GetBpRunSubmissionsQuery(InstanceId, TenantId), CancellationToken.None);

        result.Value.Select(s => s.WorkflowStateId).Should().ContainInOrder(null, 21L);
    }

    [Fact]
    public async Task Handle_MissingSnapshot_SkipsThatForm_KeepsOthers()
    {
        var goodForm = Guid.NewGuid();
        var orphanForm = Guid.NewGuid();
        var goodSub = Guid.NewGuid();
        var orphanSub = Guid.NewGuid();
        ArrangeRun(
            (goodForm, goodSub, Version: 1, StateId: null, SubmittedAt: DateTimeOffset.UtcNow.AddMinutes(-1)),
            (orphanForm, orphanSub, Version: 9, StateId: 5L, SubmittedAt: DateTimeOffset.UtcNow));
        ArrangeSchema(goodForm, 1, """{ "fields": [ { "key": "a", "labelVi": "A", "labelEn": "A", "displayOrder": 1 } ] }""");
        // orphanForm snapshot returns null (data-integrity) → skipped.
        _formTemplateClient.GetVersionSchemaByPublicIdAsync(orphanForm, 9, TenantId, Arg.Any<CancellationToken>())
            .Returns((FormTemplateVersionSchemaDto?)null);
        ArrangeData(goodSub, ("a", "ok"));

        var result = await _handler.Handle(
            new GetBpRunSubmissionsQuery(InstanceId, TenantId), CancellationToken.None);

        result.Value.Should().ContainSingle()
            .Which.FormTemplatePublicId.Should().Be(goodForm);
    }

    // ----- arrange helpers ---------------------------------------------------

    private void ArrangeRun(params (Guid FormId, Guid SubmissionId, int Version, long? StateId, DateTimeOffset SubmittedAt)[] rows)
    {
        var dtos = rows
            .Select(r => new FormSubmissionSummaryDto(
                r.SubmissionId, TenantId, r.FormId, r.Version,
                WorkflowInstanceId: InstanceId, WorkflowStateId: r.StateId,
                SubmittedAt: r.SubmittedAt, SubmittedBy: Guid.NewGuid(), Status: "Pending"))
            .ToList();

        _submissionRunClient.GetLatestForRunAsync(InstanceId, TenantId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FormSubmissionSummaryDto>)dtos);
    }

    private void ArrangeSchema(Guid formId, int version, string schemaJson) =>
        _formTemplateClient.GetVersionSchemaByPublicIdAsync(formId, version, TenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(formId, version, schemaJson));

    private void ArrangeData(Guid submissionId, params (string Key, object? Value)[] kvps)
    {
        IReadOnlyDictionary<string, object?> data = kvps.ToDictionary(kv => kv.Key, kv => kv.Value);
        _submissionRunClient.GetFullSubmissionAsync(submissionId, TenantId, Arg.Any<CancellationToken>())
            .Returns(data);
    }
}
