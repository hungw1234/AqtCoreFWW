using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using FluentAssertions;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for the Phase-04 retry-job lifecycle mutators on
/// <see cref="FormSubmissionWorkflowLinkPendingAttempt"/>: MarkRetrying, MarkSucceeded, MarkShelved,
/// RecordRetryFailure, ReactivateForManualRetry, ArmForImmediateRetry. Each legal transition mutates
/// state; each illegal transition throws <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class FormSubmissionWorkflowLinkPendingAttemptTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SubmissionId = Guid.NewGuid();
    private static readonly Guid DefinitionId = Guid.NewGuid();
    private static readonly Guid SubmittedBy = Guid.NewGuid();
    private static readonly Guid SystemActor = Guid.Empty;
    private static readonly Guid AdminActor = Guid.NewGuid();

    private static FormSubmissionWorkflowLinkPendingAttempt CreatePending() =>
        FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
            TenantId, SubmissionId, formTemplateId: 1L, DefinitionId, SubmittedBy,
            failureCode: "GOV_WFL_014", failureMessage: "empty handler pool");

    // --- MarkRetrying ---

    [Fact]
    public void MarkRetrying_FromPending_SetsRetrying()
    {
        var attempt = CreatePending();

        attempt.MarkRetrying(SystemActor);

        attempt.Status.Should().Be(WorkflowLinkPendingStatus.Retrying);
        attempt.ModifiedBy.Should().Be(SystemActor);
    }

    [Fact]
    public void MarkRetrying_FromRetrying_IsIdempotent_StaysRetrying()
    {
        var attempt = CreatePending();
        attempt.MarkRetrying(SystemActor);

        attempt.MarkRetrying(SystemActor); // crash-recovery re-claim — legal, not a no-op guard trip

        attempt.Status.Should().Be(WorkflowLinkPendingStatus.Retrying);
    }

    [Fact]
    public void MarkRetrying_FromSucceeded_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkSucceeded(Guid.NewGuid(), SystemActor);

        var act = () => attempt.MarkRetrying(SystemActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_040*");
    }

    [Fact]
    public void MarkRetrying_FromShelved_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkShelved(SystemActor);

        var act = () => attempt.MarkRetrying(SystemActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_040*");
    }

    // --- MarkSucceeded ---

    [Fact]
    public void MarkSucceeded_FromPending_SetsTerminalSucceeded()
    {
        var attempt = CreatePending();
        var instanceId = Guid.NewGuid();

        attempt.MarkSucceeded(instanceId, AdminActor);

        attempt.Status.Should().Be(WorkflowLinkPendingStatus.Succeeded);
        attempt.ResolvedAt.Should().NotBeNull();
        attempt.ResolvedBy.Should().Be(AdminActor);
        attempt.NextRetryAt.Should().BeNull();
    }

    [Fact]
    public void MarkSucceeded_FromRetrying_SetsTerminalSucceeded()
    {
        var attempt = CreatePending();
        attempt.MarkRetrying(SystemActor);

        attempt.MarkSucceeded(Guid.NewGuid(), SystemActor);

        attempt.Status.Should().Be(WorkflowLinkPendingStatus.Succeeded);
    }

    [Fact]
    public void MarkSucceeded_FromShelved_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkShelved(SystemActor);

        var act = () => attempt.MarkSucceeded(Guid.NewGuid(), SystemActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_041*");
    }

    [Fact]
    public void MarkSucceeded_FromSucceeded_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkSucceeded(Guid.NewGuid(), SystemActor);

        var act = () => attempt.MarkSucceeded(Guid.NewGuid(), SystemActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_041*");
    }

    // --- MarkShelved ---

    [Fact]
    public void MarkShelved_FromPending_SetsTerminalShelved_SubmissionUntouchedByEntity()
    {
        var attempt = CreatePending();

        attempt.MarkShelved(SystemActor);

        attempt.Status.Should().Be(WorkflowLinkPendingStatus.Shelved);
        attempt.ResolvedAt.Should().NotBeNull();
        attempt.ResolvedBy.Should().Be(SystemActor);
        attempt.NextRetryAt.Should().BeNull();
    }

    [Fact]
    public void MarkShelved_FromRetrying_SetsTerminalShelved()
    {
        var attempt = CreatePending();
        attempt.MarkRetrying(SystemActor);

        attempt.MarkShelved(SystemActor);

        attempt.Status.Should().Be(WorkflowLinkPendingStatus.Shelved);
    }

    [Fact]
    public void MarkShelved_FromSucceeded_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkSucceeded(Guid.NewGuid(), SystemActor);

        var act = () => attempt.MarkShelved(SystemActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_042*");
    }

    [Fact]
    public void MarkShelved_FromShelved_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkShelved(SystemActor);

        var act = () => attempt.MarkShelved(SystemActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_042*");
    }

    // --- RecordRetryFailure (RT3 unit slice) ---

    [Fact]
    public void RecordRetryFailure_FromPending_IncrementsAttemptCount_ReArmsNextRetryAt_StaysPending()
    {
        var attempt = CreatePending();
        attempt.AttemptCount.Should().Be(0);
        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(5);

        attempt.RecordRetryFailure("WF_NO_HANDLER", "still no handler", nextRetryAt, SystemActor);

        attempt.AttemptCount.Should().Be(1);
        attempt.LastFailureCode.Should().Be("WF_NO_HANDLER");
        attempt.LastError.Should().Be("still no handler");
        attempt.NextRetryAt.Should().Be(nextRetryAt);
        attempt.Status.Should().Be(WorkflowLinkPendingStatus.Pending);
    }

    [Fact]
    public void RecordRetryFailure_FromRetrying_IncrementsAttemptCount_ReArmsToPending()
    {
        var attempt = CreatePending();
        attempt.MarkRetrying(SystemActor);
        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(10);

        attempt.RecordRetryFailure("WF_NO_HANDLER", null, nextRetryAt, SystemActor);

        attempt.AttemptCount.Should().Be(1);
        attempt.Status.Should().Be(WorkflowLinkPendingStatus.Pending); // re-armed, not left Retrying
    }

    [Fact]
    public void RecordRetryFailure_CalledTwice_IncrementsAttemptCountEachTime()
    {
        var attempt = CreatePending();

        attempt.RecordRetryFailure("E1", null, DateTimeOffset.UtcNow.AddMinutes(5), SystemActor);
        attempt.RecordRetryFailure("E2", null, DateTimeOffset.UtcNow.AddMinutes(10), SystemActor);

        attempt.AttemptCount.Should().Be(2);
        attempt.LastFailureCode.Should().Be("E2");
    }

    [Fact]
    public void RecordRetryFailure_FromShelved_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkShelved(SystemActor);

        var act = () => attempt.RecordRetryFailure("E", null, DateTimeOffset.UtcNow, SystemActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_043*");
    }

    [Fact]
    public void RecordRetryFailure_FromSucceeded_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkSucceeded(Guid.NewGuid(), SystemActor);

        var act = () => attempt.RecordRetryFailure("E", null, DateTimeOffset.UtcNow, SystemActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_043*");
    }

    // --- ReactivateForManualRetry (LOCKED Q4 — manual admin path ONLY) ---

    [Fact]
    public void ReactivateForManualRetry_FromShelved_SetsPending_ArmsImmediateRetry_ClearsResolution()
    {
        var attempt = CreatePending();
        attempt.MarkShelved(SystemActor);

        attempt.ReactivateForManualRetry(AdminActor);

        attempt.Status.Should().Be(WorkflowLinkPendingStatus.Pending);
        attempt.NextRetryAt.Should().NotBeNull();
        attempt.NextRetryAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        attempt.ResolvedAt.Should().BeNull();
        attempt.ResolvedBy.Should().BeNull();
        attempt.ModifiedBy.Should().Be(AdminActor);
    }

    [Fact]
    public void ReactivateForManualRetry_FromPending_Throws()
    {
        var attempt = CreatePending();

        var act = () => attempt.ReactivateForManualRetry(AdminActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_044*");
    }

    [Fact]
    public void ReactivateForManualRetry_FromRetrying_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkRetrying(SystemActor);

        var act = () => attempt.ReactivateForManualRetry(AdminActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_044*");
    }

    [Fact]
    public void ReactivateForManualRetry_FromSucceeded_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkSucceeded(Guid.NewGuid(), SystemActor);

        var act = () => attempt.ReactivateForManualRetry(AdminActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_044*");
    }

    // --- ArmForImmediateRetry (manual admin nudge — does NOT increment AttemptCount) ---

    [Fact]
    public void ArmForImmediateRetry_FromPending_SetsNextRetryAtNow_DoesNotIncrementAttemptCount()
    {
        var attempt = CreatePending();
        attempt.RecordRetryFailure("E", null, DateTimeOffset.UtcNow.AddHours(6), SystemActor); // AttemptCount=1
        var attemptCountBefore = attempt.AttemptCount;

        attempt.ArmForImmediateRetry(AdminActor);

        attempt.AttemptCount.Should().Be(attemptCountBefore, "a manual nudge is not a failed attempt");
        attempt.NextRetryAt.Should().NotBeNull();
        attempt.NextRetryAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        attempt.ModifiedBy.Should().Be(AdminActor);
    }

    [Fact]
    public void ArmForImmediateRetry_FromRetrying_SetsNextRetryAtNow()
    {
        var attempt = CreatePending();
        attempt.MarkRetrying(SystemActor);

        attempt.ArmForImmediateRetry(AdminActor);

        attempt.Status.Should().Be(WorkflowLinkPendingStatus.Retrying, "arming does not change status");
        attempt.NextRetryAt.Should().NotBeNull();
    }

    [Fact]
    public void ArmForImmediateRetry_FromShelved_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkShelved(SystemActor);

        var act = () => attempt.ArmForImmediateRetry(AdminActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_045*");
    }

    [Fact]
    public void ArmForImmediateRetry_FromSucceeded_Throws()
    {
        var attempt = CreatePending();
        attempt.MarkSucceeded(Guid.NewGuid(), SystemActor);

        var act = () => attempt.ArmForImmediateRetry(AdminActor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_FRM_045*");
    }
}
