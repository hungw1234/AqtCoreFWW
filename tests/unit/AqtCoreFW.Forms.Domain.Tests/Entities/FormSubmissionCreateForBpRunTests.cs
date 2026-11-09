using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for FormSubmission.CreateForBpRun (P2).
/// Covers BP-Run factory invariants: WorkflowInstanceId required, WorkflowStateId optional,
/// immutability preserved (no mutation method for BP fields), tenant + state defaults.
/// FormSubmission is T2: PublicId=Guid (v4 random). FormTemplateId=long (T1 FK).
/// WorkflowInstanceId=Guid (T2 AK ref); WorkflowStateId=long? (T1 FK).
/// </summary>
public sealed class FormSubmissionCreateForBpRunTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WorkflowInstanceId = Guid.NewGuid();
    private const long WorkflowStateId = 7L;
    private const long FormTemplateId = 42L;
    private static readonly Guid SubmitterId = Guid.NewGuid();

    private static IGuidGenerator CreateGuidGenerator()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdRandom().Returns(Guid.NewGuid());
        return gen;
    }

    [Fact]
    public void CreateForBpRun_IntakeForm_SetsWorkflowInstanceIdAndLeavesStateNull()
    {
        var gen = CreateGuidGenerator();

        var submission = FormSubmission.CreateForBpRun(
            gen, TenantId, WorkflowInstanceId,
            workflowStateId: null,
            FormTemplateId, 1, """{"k":"v"}""", SubmitterId);

        submission.WorkflowInstanceId.Should().Be(WorkflowInstanceId);
        submission.WorkflowStateId.Should().BeNull("intake form has no associated state");
        submission.FormTemplateId.Should().Be(FormTemplateId);
        submission.TenantId.Should().Be(TenantId);
        submission.Status.Should().Be(SubmissionStatus.Pending);
    }

    [Fact]
    public void CreateForBpRun_StateForm_SetsBothInstanceAndStateIds()
    {
        var gen = CreateGuidGenerator();

        var submission = FormSubmission.CreateForBpRun(
            gen, TenantId, WorkflowInstanceId, WorkflowStateId,
            FormTemplateId, 1, """{"k":"v"}""", SubmitterId);

        submission.WorkflowInstanceId.Should().Be(WorkflowInstanceId);
        submission.WorkflowStateId.Should().Be(WorkflowStateId);
    }

    [Fact]
    public void CreateForBpRun_GeneratesUniquePublicIdsAcrossRapidSequentialCalls()
    {
        // R1 Option c (query-latest) requires each Republish = new row with distinct PublicId.
        var gen = Substitute.For<IGuidGenerator>();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        gen.NewIdRandom().Returns(id1, id2);

        var first = FormSubmission.CreateForBpRun(
            gen, TenantId, WorkflowInstanceId, WorkflowStateId,
            FormTemplateId, 1, """{"v":1}""", SubmitterId);
        var second = FormSubmission.CreateForBpRun(
            gen, TenantId, WorkflowInstanceId, WorkflowStateId,
            FormTemplateId, 1, """{"v":2}""", SubmitterId);

        first.PublicId.Should().Be(id1);
        second.PublicId.Should().Be(id2);
        first.PublicId.Should().NotBe(second.PublicId,
            "history retention requires distinct PublicId per submission row");
    }

    [Fact]
    public void CreateForBpRun_WithConsent_SetsConsentGivenFlag()
    {
        var gen = CreateGuidGenerator();

        var submission = FormSubmission.CreateForBpRun(
            gen, TenantId, WorkflowInstanceId, WorkflowStateId,
            FormTemplateId, 1, """{"k":"v"}""", SubmitterId,
            consentGiven: true);

        submission.ConsentGiven.Should().BeTrue();
    }

    [Fact]
    public void CreateForBpRun_TableMode_NullDataIsValid()
    {
        // Table-mode submissions store data in dynamic table, not SubmissionData JSON
        var gen = CreateGuidGenerator();

        var submission = FormSubmission.CreateForBpRun(
            gen, TenantId, WorkflowInstanceId, WorkflowStateId,
            FormTemplateId, 1, submissionData: null, SubmitterId);

        submission.SubmissionData.Should().BeNull();
    }

    [Fact]
    public void CreateForBpRun_WorkflowStateIdHasNoPublicSetter()
    {
        // Immutability guard: WorkflowStateId set ONLY at construction via factory.
        // Property setter must remain private.
        var setter = typeof(FormSubmission)
            .GetProperty(nameof(FormSubmission.WorkflowStateId))!
            .GetSetMethod(nonPublic: false);
        setter.Should().BeNull(
            "FormSubmission immutability invariant — BP fields set via factory only");
    }

    [Fact]
    public void CreateForBpRun_SubmittedAtIsCurrentUtc()
    {
        var gen = CreateGuidGenerator();
        var before = DateTimeOffset.UtcNow;

        var submission = FormSubmission.CreateForBpRun(
            gen, TenantId, WorkflowInstanceId, WorkflowStateId,
            FormTemplateId, 1, "{}", SubmitterId);

        submission.SubmittedAt.Should().BeOnOrAfter(before);
        submission.SubmittedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(1));
    }
}
