using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Webhooks;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.EventHandlers;
using AqtCoreFW.Workflow.Domain.Events;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.EventHandlers;

using TransitionContext = WorkflowTransitionedWebhookHandler.WebhookTransitionContext;

/// <summary>
/// TDD tests for WorkflowTransitionedWebhookHandler.
/// Verifies webhook dispatch on workflow transitions via IWebhookService.
/// Phase-4b B4c: WorkflowTransitionedEvent uses InstancePublicId (Guid T2 AK)
/// and NewStateInternalId (long T1 PK). WebhookTransitionContext is a sealed class.
/// FromStateId/ToStateId in payload are long (T1 WorkflowState.Id — no Guid AK).
/// </summary>
public sealed class WorkflowTransitionedWebhookHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly IWebhookService _webhookService;
    private readonly WorkflowTransitionedWebhookHandler _handler;

    private static readonly Guid InstancePublicId = Guid.NewGuid();
    private static readonly Guid DefinitionPublicId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TransitionedBy = Guid.NewGuid();
    private const long NewStateInternalId = 20L;
    private const long FromStateInternalId = 10L;
    private static readonly Guid EntityId = Guid.NewGuid();
    private const string DefinitionKey = "case-review";
    private const string ActionName = "Approve";
    private const string EntityType = "Case";

    // WorkflowTransitionedEvent(Guid InstancePublicId, long NewStateInternalId, Guid TransitionedBy)
    private readonly WorkflowTransitionedEvent _event = new(InstancePublicId, NewStateInternalId, TransitionedBy, Guid.NewGuid());

    public WorkflowTransitionedWebhookHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _webhookService = Substitute.For<IWebhookService>();
        _handler = new WorkflowTransitionedWebhookHandler(_db, _webhookService);
    }

    // WebhookTransitionContext is a sealed class — use object initializer, not positional ctor.
    private void SetupContext() =>
        _db.QueryFirstOrDefaultAsync<TransitionContext>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new TransitionContext
            {
                DefinitionPublicId = DefinitionPublicId,
                TenantId = TenantId,
                ActionName = ActionName,
                FromStateId = FromStateInternalId,
                DefinitionKey = DefinitionKey,
                EntityType = EntityType,
                EntityId = EntityId
            });

    private void SetupNoContext() =>
        _db.QueryFirstOrDefaultAsync<TransitionContext>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((TransitionContext?)null);

    [Fact]
    public async Task Handle_ValidTransition_DispatchesWebhook()
    {
        SetupContext();

        await _handler.Handle(_event, CancellationToken.None);

        await _webhookService.Received(1).DispatchAsync(
            "workflow.transitioned",
            Arg.Any<object>(),
            TenantId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidTransition_PayloadContainsCorrectFields()
    {
        SetupContext();
        object? capturedPayload = null;

        await _webhookService.DispatchAsync(
            Arg.Any<string>(),
            Arg.Do<object>(p => capturedPayload = p),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        await _handler.Handle(_event, CancellationToken.None);

        Assert.NotNull(capturedPayload);
        var payload = (WorkflowTransitionWebhookPayload)capturedPayload;
        Assert.Equal(InstancePublicId, payload.InstanceId);
        Assert.Equal(DefinitionKey, payload.DefinitionKey);
        Assert.Equal(FromStateInternalId, payload.FromStateId);
        Assert.Equal(NewStateInternalId, payload.ToStateId);
        Assert.Equal(ActionName, payload.ActionName);
        Assert.Equal(TransitionedBy, payload.TransitionedBy);
        Assert.Equal(EntityType, payload.EntityType);
        Assert.Equal(EntityId, payload.EntityId);
    }

    [Fact]
    public async Task Handle_ValidTransition_PayloadExcludesTenantId()
    {
        // F06: TenantId must NOT be in webhook payload (security — internal UUID leak)
        SetupContext();
        object? capturedPayload = null;

        await _webhookService.DispatchAsync(
            Arg.Any<string>(),
            Arg.Do<object>(p => capturedPayload = p),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        await _handler.Handle(_event, CancellationToken.None);

        Assert.NotNull(capturedPayload);
        var payload = (WorkflowTransitionWebhookPayload)capturedPayload;
        // Payload record should NOT have a TenantId property
        var props = payload.GetType().GetProperties();
        Assert.DoesNotContain(props, p => p.Name == "TenantId");
    }

    [Fact]
    public async Task Handle_ContextNotFound_NoWebhookDispatched()
    {
        SetupNoContext();

        await _handler.Handle(_event, CancellationToken.None);

        await _webhookService.DidNotReceive().DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WebhookServiceThrows_DoesNotPropagateException()
    {
        // Handler should be fire-and-forget — exceptions must not break the transition
        SetupContext();
        _webhookService.DispatchAsync(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("Webhook delivery failed")));

        // Should not throw
        await _handler.Handle(_event, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_EventType_IsWorkflowTransitioned()
    {
        SetupContext();

        await _handler.Handle(_event, CancellationToken.None);

        await _webhookService.Received(1).DispatchAsync(
            "workflow.transitioned",
            Arg.Any<object>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PayloadTimestamp_IsRecent()
    {
        SetupContext();
        object? capturedPayload = null;

        await _webhookService.DispatchAsync(
            Arg.Any<string>(),
            Arg.Do<object>(p => capturedPayload = p),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        var before = DateTimeOffset.UtcNow;
        await _handler.Handle(_event, CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(capturedPayload);
        var payload = (WorkflowTransitionWebhookPayload)capturedPayload;
        Assert.InRange(payload.Timestamp, before, after);
    }
}
