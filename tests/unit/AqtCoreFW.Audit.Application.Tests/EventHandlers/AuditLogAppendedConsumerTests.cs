using AqtCoreFW.Audit.Application.EventHandlers;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Audit.Application.Tests.EventHandlers;

/// <summary>
/// Unit tests for AuditLogAppendedConsumer (260427-1943 Phase-03 — extraction prep).
/// Today the consumer is a pure noop; these assertions guard against regressions where
/// someone wires a non-trivial side effect without recognising the dual-write semantic
/// (sync write path remains authoritative until Audit module is extracted).
/// </summary>
public sealed class AuditLogAppendedConsumerTests
{
    private readonly ILogger<AuditLogAppendedConsumer> _logger =
        Substitute.For<ILogger<AuditLogAppendedConsumer>>();
    private readonly IProcessedMessageStore _dedup = Substitute.For<IProcessedMessageStore>();
    private readonly AuditLogAppendedConsumer _sut;

    public AuditLogAppendedConsumerTests()
    {
        // Default: first-time claim succeeds (NSubstitute would otherwise return false → skip).
        _dedup.TryClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _sut = new AuditLogAppendedConsumer(_dedup, _logger);
    }

    [Fact]
    public async Task Consume_ValidEvent_CompletesWithoutThrowing()
    {
        var ctx = Substitute.For<ConsumeContext<AuditLogAppendedIntegrationEvent>>();
        ctx.Message.Returns(new AuditLogAppendedIntegrationEvent(
            AuditEntryId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            ActorUserId: Guid.NewGuid(),
            Action: "CREATE",
            ModuleName: "Cases",
            ResourceType: "Case",
            ResourceId: Guid.NewGuid().ToString(),
            MessageId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow));
        ctx.CancellationToken.Returns(CancellationToken.None);

        var act = async () => await _sut.Consume(ctx);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_NullableFieldsOmitted_StillCompletes()
    {
        // Login attempts and system-level audit entries can have null TenantId / ActorUserId.
        var ctx = Substitute.For<ConsumeContext<AuditLogAppendedIntegrationEvent>>();
        ctx.Message.Returns(new AuditLogAppendedIntegrationEvent(
            AuditEntryId: Guid.NewGuid(),
            TenantId: null,
            ActorUserId: null,
            Action: "LOGIN_FAILED",
            ModuleName: "Identity",
            ResourceType: "Session",
            ResourceId: null,
            MessageId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow));
        ctx.CancellationToken.Returns(CancellationToken.None);

        var act = async () => await _sut.Consume(ctx);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_ClaimsIdempotency_ByAuditEntryId()
    {
        // Slice 1C: the consumer must de-dup on the contract's documented key (AuditEntryId).
        var auditEntryId = Guid.NewGuid();
        var ctx = Substitute.For<ConsumeContext<AuditLogAppendedIntegrationEvent>>();
        ctx.Message.Returns(new AuditLogAppendedIntegrationEvent(
            AuditEntryId: auditEntryId, TenantId: Guid.NewGuid(), ActorUserId: Guid.NewGuid(),
            Action: "CREATE", ModuleName: "Forms", ResourceType: "FormSubmission",
            ResourceId: null, MessageId: Guid.NewGuid(), OccurredAt: DateTimeOffset.UtcNow));
        ctx.CancellationToken.Returns(CancellationToken.None);

        await _sut.Consume(ctx);

        await _dedup.Received(1).TryClaimAsync(
            nameof(AuditLogAppendedConsumer), auditEntryId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_DuplicateDelivery_SkipsWithoutThrowing()
    {
        _dedup.TryClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false); // already processed → consumer returns early
        var ctx = Substitute.For<ConsumeContext<AuditLogAppendedIntegrationEvent>>();
        ctx.Message.Returns(new AuditLogAppendedIntegrationEvent(
            AuditEntryId: Guid.NewGuid(), TenantId: null, ActorUserId: null,
            Action: "LOGIN_FAILED", ModuleName: "Identity", ResourceType: "Session",
            ResourceId: null, MessageId: Guid.NewGuid(), OccurredAt: DateTimeOffset.UtcNow));
        ctx.CancellationToken.Returns(CancellationToken.None);

        var act = async () => await _sut.Consume(ctx);

        await act.Should().NotThrowAsync();
    }
}
