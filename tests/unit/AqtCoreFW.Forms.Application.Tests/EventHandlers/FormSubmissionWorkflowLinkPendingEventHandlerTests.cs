using AqtCoreFW.Forms.Application.EventHandlers;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Application.Tests.EventHandlers;

/// <summary>
/// P6-DEFER-1 Phase 03 unit tests — UPSERT idempotency + fail-soft contract for the
/// WorkflowLinkPendingEvent → pending-attempt persistence handler.
/// </summary>
public sealed class FormSubmissionWorkflowLinkPendingEventHandlerTests
{
    private readonly IFormSubmissionWorkflowLinkPendingAttemptRepository _repo
        = Substitute.For<IFormSubmissionWorkflowLinkPendingAttemptRepository>();

    private readonly ICrossModuleUnitOfWork _uow = Substitute.For<ICrossModuleUnitOfWork>();

    private readonly FormSubmissionWorkflowLinkPendingEventHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SubmissionId = Guid.NewGuid();
    private static readonly Guid DefinitionId = Guid.NewGuid();
    private static readonly Guid SubmittedBy = Guid.NewGuid();

    public FormSubmissionWorkflowLinkPendingEventHandlerTests()
    {
        _handler = new FormSubmissionWorkflowLinkPendingEventHandler(
            _repo,
            _uow,
            Substitute.For<ILogger<FormSubmissionWorkflowLinkPendingEventHandler>>());
    }

    private static FormSubmissionWorkflowLinkPendingEvent BuildEvent(
        string failureCode = "GOV_WFL_014",
        string? failureMessage = "No handler resolved.") =>
        new(
            SubmissionId: SubmissionId,
            FormTemplateId: 42L,
            ApprovalWorkflowDefinitionId: DefinitionId,
            TenantId: TenantId,
            SubmittedBy: SubmittedBy,
            FailureCode: failureCode,
            FailureMessage: failureMessage,
            MessageId: Guid.NewGuid());

    [Fact]
    public async Task Handle_FirstEmit_AddsAttemptRowWithCorrectFields()
    {
        _repo.GetTrackedBySubmissionIdAsync(SubmissionId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormSubmissionWorkflowLinkPendingAttempt?>(null));

        await _handler.Handle(BuildEvent(), CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Is<FormSubmissionWorkflowLinkPendingAttempt>(a =>
                a.TenantId == TenantId &&
                a.SubmissionId == SubmissionId &&
                a.FormTemplateId == 42L &&
                a.ApprovalWorkflowDefinitionId == DefinitionId &&
                a.SubmittedBy == SubmittedBy &&
                a.AttemptCount == 0 &&
                a.LastFailureCode == "GOV_WFL_014" &&
                a.LastError == "No handler resolved." &&
                a.Status == WorkflowLinkPendingStatus.Pending),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateEmit_DoesNotAddRow()
    {
        // Build a fake existing row using the factory (handler should short-circuit).
        var existing = FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
            TenantId, SubmissionId, 42L, DefinitionId, SubmittedBy,
            "GOV_WFL_014", "earlier-message");

        _repo.GetTrackedBySubmissionIdAsync(SubmissionId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormSubmissionWorkflowLinkPendingAttempt?>(existing));

        await _handler.Handle(BuildEvent(failureMessage: "later-message"), CancellationToken.None);

        await _repo.DidNotReceive().AddAsync(
            Arg.Any<FormSubmissionWorkflowLinkPendingAttempt>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LongFailureMessage_TruncatesTo500Chars()
    {
        _repo.GetTrackedBySubmissionIdAsync(SubmissionId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormSubmissionWorkflowLinkPendingAttempt?>(null));

        var longMsg = new string('x', 1200);

        await _handler.Handle(BuildEvent(failureMessage: longMsg), CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Is<FormSubmissionWorkflowLinkPendingAttempt>(a =>
                a.LastError != null && a.LastError.Length == 500),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenRepositoryThrows_SwallowsExceptionFailSoft()
    {
        // Fail-soft contract: handler must NEVER throw — submit command response is preserved.
        _repo.GetTrackedBySubmissionIdAsync(SubmissionId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormSubmissionWorkflowLinkPendingAttempt?>(null));
        _repo.AddAsync(Arg.Any<FormSubmissionWorkflowLinkPendingAttempt>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("simulated DB outage"));

        // Should NOT throw.
        await _handler.Handle(BuildEvent(), CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Any<FormSubmissionWorkflowLinkPendingAttempt>(),
            Arg.Any<CancellationToken>());

        // Even on failure the pinned UoW must be rolled back (no dangling open transaction).
        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    // ── RLS tenant-pin guard (regression for the anonymous public-form BLOCK-predicate hole) ──

    [Fact]
    public async Task Handle_FirstEmit_PinsUnitOfWorkToEventTenant_ThenCommits()
    {
        // The INSERT target carries a BLOCK-AFTER-INSERT RLS policy. The handler MUST pin
        // SESSION_CONTEXT to the event's tenant BEFORE the write — the ambient ITenantContext is
        // null on the anonymous public-form path, so relying on it would leave the policy unsatisfied
        // and the INSERT would be rejected (error 33504) then fail-soft-swallowed.
        _repo.GetTrackedBySubmissionIdAsync(SubmissionId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormSubmissionWorkflowLinkPendingAttempt?>(null));

        await _handler.Handle(BuildEvent(), CancellationToken.None);

        await _uow.Received(1).BeginAsync(TenantId, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }
}
