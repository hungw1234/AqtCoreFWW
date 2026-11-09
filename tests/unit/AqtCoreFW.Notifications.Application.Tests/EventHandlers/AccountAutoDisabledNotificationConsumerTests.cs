using AqtCoreFW.Notifications.Application.EventHandlers;
using AqtCoreFW.Notifications.Application.Providers;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Notifications.Application.Tests.EventHandlers;

/// <summary>
/// Unit tests for <see cref="AccountAutoDisabledNotificationConsumer"/> — Slice 1C added consumer
/// idempotency. Mocks ConsumeContext directly; no MassTransit harness, no DB.
/// </summary>
public sealed class AccountAutoDisabledNotificationConsumerTests
{
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly IProcessedMessageStore _dedup = Substitute.For<IProcessedMessageStore>();
    private readonly ILogger<AccountAutoDisabledNotificationConsumer> _logger =
        Substitute.For<ILogger<AccountAutoDisabledNotificationConsumer>>();
    private readonly AccountAutoDisabledNotificationConsumer _sut;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime DisabledAt = new(2026, 07, 07, 3, 0, 0, DateTimeKind.Utc);

    public AccountAutoDisabledNotificationConsumerTests()
    {
        // Default: first-time claim succeeds (NSubstitute would otherwise return false → skip).
        _dedup.TryClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _sut = new AccountAutoDisabledNotificationConsumer(_email, _dedup, _logger);
    }

    private static ConsumeContext<AccountAutoDisabledIntegrationEvent> Context(string? email)
    {
        var ctx = Substitute.For<ConsumeContext<AccountAutoDisabledIntegrationEvent>>();
        ctx.Message.Returns(new AccountAutoDisabledIntegrationEvent(
            UserId: UserId, TenantId: Guid.NewGuid(), UserName: "nguyenvana",
            Email: email, AutoDisabledAtUtc: DisabledAt, ConsecutiveLockoutCount: 5));
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task Consume_FirstDelivery_SendsEmail_ClaimsCompositeKey()
    {
        await _sut.Consume(Context("user@aequitas.vn"));

        await _email.Received(1).SendAsync(
            "user@aequitas.vn", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // No MessageId on this direct-publish event → dedup key is the per-event identity.
        await _dedup.Received(1).TryClaimAsync(
            nameof(AccountAutoDisabledNotificationConsumer),
            $"{UserId:N}:{DisabledAt.Ticks}",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_DuplicateDelivery_SkipsEmail()
    {
        _dedup.TryClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await _sut.Consume(Context("user@aequitas.vn"));

        await _email.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_NoEmailAddress_SkipsWithoutClaiming()
    {
        await _sut.Consume(Context(email: null));

        await _email.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // No-effect path claims nothing.
        await _dedup.DidNotReceive().TryClaimAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
