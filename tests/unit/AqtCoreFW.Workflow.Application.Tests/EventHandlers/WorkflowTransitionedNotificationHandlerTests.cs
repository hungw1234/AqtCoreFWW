using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.EventHandlers;
using AqtCoreFW.Workflow.Domain.Events;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.EventHandlers;

using TransitionContext = WorkflowTransitionedNotificationHandler.TransitionContext;
using NotificationConfigRow = WorkflowTransitionedNotificationHandler.NotificationConfigRow;

/// <summary>
/// Unit tests for WorkflowTransitionedNotificationHandler.
/// Verifies notification dispatch per config; uses NSubstitute for IReadDbConnection + INotificationModuleClient.
/// Phase-4b B4c: WorkflowTransitionedEvent uses InstancePublicId (Guid T2 AK) and
/// NewStateInternalId (long T1 PK). TransitionContext is a sealed class (not positional record).
/// DefinitionId in TransitionContext is long (internal FK).
/// </summary>
public sealed class WorkflowTransitionedNotificationHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly INotificationModuleClient _notificationClient;
    private readonly WorkflowTransitionedNotificationHandler _handler;

    private static readonly Guid InstancePublicId = Guid.NewGuid();
    private const long DefinitionId = 1L;
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TransitionedBy = Guid.NewGuid();
    private const long NewStateInternalId = 20L;

    // WorkflowTransitionedEvent(Guid InstancePublicId, long NewStateInternalId, Guid TransitionedBy)
    private readonly WorkflowTransitionedEvent _event = new(InstancePublicId, NewStateInternalId, TransitionedBy, Guid.NewGuid());

    public WorkflowTransitionedNotificationHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _notificationClient = Substitute.For<INotificationModuleClient>();
        _handler = new WorkflowTransitionedNotificationHandler(_db, _notificationClient);
    }

    // --- Context helpers ---

    // TransitionContext is a sealed class — use object initializer, not positional ctor.
    private void SetupContext(string actionName = "Approve") =>
        _db.QueryFirstOrDefaultAsync<TransitionContext>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new TransitionContext
            {
                DefinitionId = DefinitionId,
                TenantId = TenantId,
                ActionName = actionName
            });

    private void SetupNoContext() =>
        _db.QueryFirstOrDefaultAsync<TransitionContext>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((TransitionContext?)null);

    private void SetupConfigs(IEnumerable<NotificationConfigRow> rows) =>
        _db.QueryAsync<NotificationConfigRow>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<NotificationConfigRow>>(rows.ToList()));

    // --- Tests ---

    [Fact]
    public async Task Handle_MatchingConfig_SendsNotification()
    {
        SetupContext("Approve");
        var userId = Guid.NewGuid();
        SetupConfigs([
            new(1L, "email", "user", userId.ToString(), "Approved", "Your item was approved.")
        ]);

        await _handler.Handle(_event, CancellationToken.None);

        await _notificationClient.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.RecipientUserId == userId &&
                r.TenantId == TenantId &&
                r.Channel == "email"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoConfig_NoNotificationSent()
    {
        SetupContext("Approve");
        SetupConfigs([]);

        await _handler.Handle(_event, CancellationToken.None);

        await _notificationClient.DidNotReceive().SendAsync(
            Arg.Any<SendNotificationRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InstanceContextNotFound_NoNotificationSent()
    {
        // When context query returns null, handler exits early — no config query, no send
        SetupNoContext();

        await _handler.Handle(_event, CancellationToken.None);

        await _notificationClient.DidNotReceive().SendAsync(
            Arg.Any<SendNotificationRequest>(),
            Arg.Any<CancellationToken>());

        // Config query should never have been called
        await _db.DidNotReceive().QueryAsync<NotificationConfigRow>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MultipleConfigs_SendsAll()
    {
        SetupContext("Submit");
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        SetupConfigs([
            new(1L, "email", "user", userId1.ToString(), "Submitted", "Email body"),
            new(2L, "inapp", "user", userId2.ToString(), "Submitted", "In-app body")
        ]);

        await _handler.Handle(_event, CancellationToken.None);

        await _notificationClient.Received(2).SendAsync(
            Arg.Any<SendNotificationRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidUserRecipient_Skipped()
    {
        SetupContext("Approve");
        SetupConfigs([
            new(1L, "email", "user", "not-a-valid-guid", "Subject", "Body")
        ]);

        await _handler.Handle(_event, CancellationToken.None);

        await _notificationClient.DidNotReceive().SendAsync(
            Arg.Any<SendNotificationRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CreatorRecipientType_UsesTransitionedBy()
    {
        SetupContext("Approve");
        SetupConfigs([
            new(1L, "inapp", "creator", "creator", "Done", "Your action succeeded.")
        ]);

        await _handler.Handle(_event, CancellationToken.None);

        await _notificationClient.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r => r.RecipientUserId == TransitionedBy),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RoleRecipientType_Skipped()
    {
        // "role" resolution requires IUserService — deferred, should skip gracefully
        SetupContext("Approve");
        SetupConfigs([
            new(1L, "email", "role", "admin-role", "Subject", "Body")
        ]);

        await _handler.Handle(_event, CancellationToken.None);

        await _notificationClient.DidNotReceive().SendAsync(
            Arg.Any<SendNotificationRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CorrelationId_IncludesInstanceAndAction()
    {
        SetupContext("Approve");
        var userId = Guid.NewGuid();
        SetupConfigs([
            new(1L, "email", "user", userId.ToString(), "Subject", "Body")
        ]);

        await _handler.Handle(_event, CancellationToken.None);

        await _notificationClient.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.CorrelationId != null &&
                r.CorrelationId.Contains(InstancePublicId.ToString()) &&
                r.CorrelationId.Contains("Approve")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MixedConfigs_OnlySendsToValidRecipients()
    {
        SetupContext("Reject");
        var validUserId = Guid.NewGuid();
        SetupConfigs([
            new(1L, "email", "user", validUserId.ToString(), "Rejected", "Valid body"),
            new(2L, "sms", "user", "bad-guid", "Rejected", "Invalid body"),
            new(3L, "inapp", "role", "some-role", "Rejected", "Role body")
        ]);

        await _handler.Handle(_event, CancellationToken.None);

        // Only the valid user config should trigger a send
        await _notificationClient.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r => r.RecipientUserId == validUserId),
            Arg.Any<CancellationToken>());
    }
}
