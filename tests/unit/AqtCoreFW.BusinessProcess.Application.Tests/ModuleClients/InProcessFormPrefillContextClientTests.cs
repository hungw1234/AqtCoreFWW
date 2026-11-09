using AqtCoreFW.BusinessProcess.Application.ModuleClients;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.ModuleClients;

/// <summary>
/// Unit tests for the BP MVP P6 prefill resolver. Each fact pins one branch of the architecture
/// diagram in <c>phase-06-form-prefill-context-client.md</c>: cross-tenant 404 (D8), no prefill,
/// happy path, source-submission missing (D4), source-key missing (D4), invalid envelope (D4),
/// and N+1 dedup (R3).
/// </summary>
public sealed class InProcessFormPrefillContextClientTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid TargetFormId = Guid.NewGuid();
    private const int TargetVersion = 3;

    private readonly IWorkflowModuleClient _workflowClient = Substitute.For<IWorkflowModuleClient>();
    private readonly IFormTemplateQueryClient _formTemplateClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly IFormSubmissionRunClient _submissionRunClient = Substitute.For<IFormSubmissionRunClient>();
    private readonly InProcessFormPrefillContextClient _resolver;

    public InProcessFormPrefillContextClientTests()
    {
        _resolver = new InProcessFormPrefillContextClient(
            _workflowClient,
            _formTemplateClient,
            _submissionRunClient,
            NullLogger<InProcessFormPrefillContextClient>.Instance);
    }

    [Fact]
    public async Task GetPrefillContext_InstanceNotFound_ReturnsNull_NoFurtherCalls()
    {
        _workflowClient.GetTenantForInstanceAsync(InstanceId, Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var result = await _resolver.GetPrefillContextAsync(
            InstanceId, TargetFormId, TargetVersion, TenantId);

        result.Should().BeNull();
        await _formTemplateClient.DidNotReceiveWithAnyArgs()
            .GetVersionSchemaByPublicIdAsync(default, default, default, default);
        await _submissionRunClient.DidNotReceiveWithAnyArgs()
            .GetLatestForRunAsync(default, default, default);
    }

    [Fact]
    public async Task GetPrefillContext_CrossTenantInstance_ReturnsNull()
    {
        _workflowClient.GetTenantForInstanceAsync(InstanceId, Arg.Any<CancellationToken>())
            .Returns(OtherTenantId);

        var result = await _resolver.GetPrefillContextAsync(
            InstanceId, TargetFormId, TargetVersion, TenantId);

        result.Should().BeNull();
        await _formTemplateClient.DidNotReceiveWithAnyArgs()
            .GetVersionSchemaByPublicIdAsync(default, default, default, default);
    }

    [Fact]
    public async Task GetPrefillContext_NoPrefillFieldsInTarget_ReturnsEmptyDict_NoSubmissionLookup()
    {
        ArrangeTenantOk();
        ArrangeTargetSchema(SchemaWithoutPrefill());

        var result = await _resolver.GetPrefillContextAsync(
            InstanceId, TargetFormId, TargetVersion, TenantId);

        result.Should().NotBeNull().And.BeEmpty();
        await _submissionRunClient.DidNotReceiveWithAnyArgs()
            .GetLatestForRunAsync(default, default, default);
    }

    [Fact]
    public async Task GetPrefillContext_HappyPath_ReturnsValueFromSourceSubmission()
    {
        ArrangeTenantOk();
        var sourceFormId = Guid.NewGuid();
        var sourceSubmissionId = Guid.NewGuid();

        ArrangeTargetSchema(SchemaWithSinglePrefill("targetKey", sourceFormId, "sourceKey"));
        ArrangeRunSubmissions((sourceFormId, sourceSubmissionId));
        ArrangeFullSubmission(sourceSubmissionId, ("sourceKey", "Hello"));

        var result = await _resolver.GetPrefillContextAsync(
            InstanceId, TargetFormId, TargetVersion, TenantId);

        result.Should().NotBeNull();
        result!.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new KeyValuePair<string, object?>("targetKey", "Hello"));
    }

    [Fact]
    public async Task GetPrefillContext_SourceSubmissionMissingFromRun_OmitsField()
    {
        ArrangeTenantOk();
        var unrelatedFormId = Guid.NewGuid();
        var orphanSourceFormId = Guid.NewGuid();

        ArrangeTargetSchema(SchemaWithSinglePrefill("targetKey", orphanSourceFormId, "x"));
        // Run only has a submission for an unrelated form — the source isn't there.
        ArrangeRunSubmissions((unrelatedFormId, Guid.NewGuid()));

        var result = await _resolver.GetPrefillContextAsync(
            InstanceId, TargetFormId, TargetVersion, TenantId);

        result.Should().NotBeNull().And.BeEmpty();
        await _submissionRunClient.DidNotReceiveWithAnyArgs()
            .GetFullSubmissionAsync(default, default, default);
    }

    [Fact]
    public async Task GetPrefillContext_SourceFieldKeyAbsent_OmitsField()
    {
        ArrangeTenantOk();
        var sourceFormId = Guid.NewGuid();
        var sourceSubmissionId = Guid.NewGuid();

        ArrangeTargetSchema(SchemaWithSinglePrefill("targetKey", sourceFormId, "missingKey"));
        ArrangeRunSubmissions((sourceFormId, sourceSubmissionId));
        ArrangeFullSubmission(sourceSubmissionId, ("someOtherKey", 42));

        var result = await _resolver.GetPrefillContextAsync(
            InstanceId, TargetFormId, TargetVersion, TenantId);

        result.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetPrefillContext_InvalidEnvelopeOnOneField_OtherFieldsStillResolve()
    {
        ArrangeTenantOk();
        var sourceFormId = Guid.NewGuid();
        var sourceSubmissionId = Guid.NewGuid();

        // Field "good" has a valid envelope; field "bad" has malformed JSON — only "good" should resolve.
        var schema = $$"""
            {
              "schemaEnvelopeVersion": 1,
              "fields": [
                { "key": "good", "prefillSource": "{\"sourceFormTemplateId\":\"{{sourceFormId}}\",\"sourceFieldKey\":\"src\"}" },
                { "key": "bad",  "prefillSource": "this is not json {" }
              ]
            }
            """;
        ArrangeTargetSchema(schema);
        ArrangeRunSubmissions((sourceFormId, sourceSubmissionId));
        ArrangeFullSubmission(sourceSubmissionId, ("src", "OK"));

        var result = await _resolver.GetPrefillContextAsync(
            InstanceId, TargetFormId, TargetVersion, TenantId);

        result.Should().NotBeNull();
        result!.Should().ContainSingle().Which.Key.Should().Be("good");
        result["good"].Should().Be("OK");
    }

    [Fact]
    public async Task GetPrefillContext_TwoFieldsSameSourceSubmission_FetchesOnce()
    {
        ArrangeTenantOk();
        var sourceFormId = Guid.NewGuid();
        var sourceSubmissionId = Guid.NewGuid();

        // Two prefill targets pointing at the same source submission — dedup must collapse to 1 fetch (R3).
        var schema = $$"""
            {
              "schemaEnvelopeVersion": 1,
              "fields": [
                { "key": "tA", "prefillSource": "{\"sourceFormTemplateId\":\"{{sourceFormId}}\",\"sourceFieldKey\":\"a\"}" },
                { "key": "tB", "prefillSource": "{\"sourceFormTemplateId\":\"{{sourceFormId}}\",\"sourceFieldKey\":\"b\"}" }
              ]
            }
            """;
        ArrangeTargetSchema(schema);
        ArrangeRunSubmissions((sourceFormId, sourceSubmissionId));
        ArrangeFullSubmission(sourceSubmissionId, ("a", 1), ("b", 2));

        var result = await _resolver.GetPrefillContextAsync(
            InstanceId, TargetFormId, TargetVersion, TenantId);

        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
        result["tA"].Should().Be(1);
        result["tB"].Should().Be(2);
        await _submissionRunClient.Received(1).GetFullSubmissionAsync(
            sourceSubmissionId, TenantId, Arg.Any<CancellationToken>());
    }

    // ----- arrange helpers ---------------------------------------------------

    private void ArrangeTenantOk() =>
        _workflowClient.GetTenantForInstanceAsync(InstanceId, Arg.Any<CancellationToken>())
            .Returns(TenantId);

    private void ArrangeTargetSchema(string schemaJson) =>
        _formTemplateClient.GetVersionSchemaByPublicIdAsync(
                TargetFormId, TargetVersion, TenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(TargetFormId, TargetVersion, schemaJson));

    private void ArrangeRunSubmissions(params (Guid FormId, Guid SubmissionId)[] submissions)
    {
        var dtos = submissions
            .Select((s, idx) => new FormSubmissionSummaryDto(
                s.SubmissionId, TenantId, s.FormId, FormTemplateVersion: 1,
                WorkflowInstanceId: InstanceId, WorkflowStateId: null,
                SubmittedAt: DateTimeOffset.UtcNow.AddMinutes(-idx),
                SubmittedBy: Guid.NewGuid(), Status: "Submitted"))
            .ToList();

        _submissionRunClient.GetLatestForRunAsync(InstanceId, TenantId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FormSubmissionSummaryDto>)dtos);
    }

    private void ArrangeFullSubmission(Guid submissionId, params (string Key, object? Value)[] kvps)
    {
        IReadOnlyDictionary<string, object?> data =
            kvps.ToDictionary(kv => kv.Key, kv => kv.Value);
        _submissionRunClient.GetFullSubmissionAsync(submissionId, TenantId, Arg.Any<CancellationToken>())
            .Returns(data);
    }

    private static string SchemaWithoutPrefill() => """
        { "schemaEnvelopeVersion": 1, "fields": [ { "key": "x", "prefillSource": null } ] }
        """;

    private static string SchemaWithSinglePrefill(string targetKey, Guid sourceFormId, string sourceKey) =>
        $$"""
        {
          "schemaEnvelopeVersion": 1,
          "fields": [
            { "key": "{{targetKey}}", "prefillSource": "{\"sourceFormTemplateId\":\"{{sourceFormId}}\",\"sourceFieldKey\":\"{{sourceKey}}\"}" }
          ]
        }
        """;
}
