using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Integration.Domain.Tests.Entities;

/// <summary>
/// Unit tests for MessageLog entity.
/// Verifies factory and state transitions (Sent → Delivered | Failed; Sent | Delivered → Acknowledged).
/// Pure in-process tests — no DB, no HTTP.
/// </summary>
public sealed class MessageLogEntityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const long PartnerId = 100L;    // T1 Partner internal PK — long, not Guid
    private const long ContractId = 200L;   // T2 Contract internal PK — long, not Guid
    private static readonly Guid CreatedBy = Guid.NewGuid();

    private static IGuidGenerator MakeGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(Guid.NewGuid());
        return gen;
    }

    private static MessageLog CreateLog()
    {
        var gen = MakeGen();
        return MessageLog.Create(gen, TenantId, PartnerId, ContractId,
            MessageDirection.Outbound, "GOV_NOTIFY", """{"msg":"hello"}""",
            "corr-001", CreatedBy);
    }

    // --- Factory ---

    [Fact]
    public void Create_ValidParams_ReturnsSentStatus()
    {
        var log = CreateLog();
        log.Status.Should().Be(MessageLogStatus.Sent);
    }

    [Fact]
    public void Create_ValidParams_SetsSentAtToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var log = CreateLog();
        log.SentAt.Should().BeAfter(before).And.BeOnOrBefore(DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Create_ValidParams_SetsMessageId()
    {
        var log = CreateLog();
        log.MessageId.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_ValidParams_SetsAllProperties()
    {
        var log = CreateLog();
        log.TenantId.Should().Be(TenantId);
        log.PartnerId.Should().Be(PartnerId);
        log.ContractId.Should().Be(ContractId);
        log.Direction.Should().Be(MessageDirection.Outbound);
        log.MessageType.Should().Be("GOV_NOTIFY");
        log.Payload.Should().Be("""{"msg":"hello"}""");
        log.CorrelationId.Should().Be("corr-001");
    }

    // --- MarkDelivered ---

    [Fact]
    public void MarkDelivered_WhenSent_SetsStatusDelivered()
    {
        var log = CreateLog();
        log.MarkDelivered();
        log.Status.Should().Be(MessageLogStatus.Delivered);
    }

    [Fact]
    public void MarkDelivered_WhenDelivered_ThrowsInvalidOperation()
    {
        var log = CreateLog();
        log.MarkDelivered();
        var act = () => log.MarkDelivered();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkDelivered_WhenAcknowledged_ThrowsInvalidOperation()
    {
        var log = CreateLog();
        log.Acknowledge();
        var act = () => log.MarkDelivered();
        act.Should().Throw<InvalidOperationException>();
    }

    // --- MarkFailed ---

    [Fact]
    public void MarkFailed_WhenSent_SetsStatusFailed()
    {
        var log = CreateLog();
        log.MarkFailed();
        log.Status.Should().Be(MessageLogStatus.Failed);
    }

    [Fact]
    public void MarkFailed_WhenDelivered_SetsStatusFailed()
    {
        var log = CreateLog();
        log.MarkDelivered();
        log.MarkFailed();
        log.Status.Should().Be(MessageLogStatus.Failed);
    }

    [Fact]
    public void MarkFailed_WhenAcknowledged_ThrowsInvalidOperation()
    {
        var log = CreateLog();
        log.Acknowledge();
        var act = () => log.MarkFailed();
        act.Should().Throw<InvalidOperationException>();
    }

    // --- Acknowledge ---

    [Fact]
    public void Acknowledge_WhenSent_SetsStatusAcknowledged()
    {
        var log = CreateLog();
        log.Acknowledge();
        log.Status.Should().Be(MessageLogStatus.Acknowledged);
    }

    [Fact]
    public void Acknowledge_WhenDelivered_SetsStatusAcknowledged()
    {
        var log = CreateLog();
        log.MarkDelivered();
        log.Acknowledge();
        log.Status.Should().Be(MessageLogStatus.Acknowledged);
    }

    [Fact]
    public void Acknowledge_WhenFailed_ThrowsInvalidOperation()
    {
        var log = CreateLog();
        log.MarkFailed();
        var act = () => log.Acknowledge();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Acknowledge_SetsAcknowledgedAtToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var log = CreateLog();
        log.Acknowledge();
        log.AcknowledgedAt.Should().NotBeNull();
        log.AcknowledgedAt!.Value.Should().BeAfter(before)
            .And.BeOnOrBefore(DateTime.UtcNow.AddSeconds(1));
    }
}
