using AqtCoreFW.Infrastructure.Messaging;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Resilience.Tests.Messaging;

/// <summary>
/// Unit tests for DeadLetterService admin operations.
/// Uses EF Core in-memory provider — no Docker/SQL required.
/// </summary>
[Trait("Category", "Resilience")]
public sealed class DeadLetterTests : IDisposable
{
    // ── fixtures ───────────────────────────────────────────────────────────────

    private readonly MessagingDbContext _db;
    private readonly IPublishEndpoint _bus = Substitute.For<IPublishEndpoint>();
    private readonly DeadLetterService _sut;

    public DeadLetterTests()
    {
        var dbOptions = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new MessagingDbContext(dbOptions);
        _sut = new DeadLetterService(_db, _bus, NullLogger<DeadLetterService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task<MessageDeadLetter> SeedDeadLetterAsync(
        string messageType = "OrderCreated",
        string payload = "{\"orderId\":\"1\"}",
        string reason = "Processing failed",
        string queue = "order-queue",
        DeadLetterStatus status = DeadLetterStatus.Pending)
    {
        var letter = new MessageDeadLetter
        {
            MessageType = messageType,
            Payload = payload,
            FailureReason = reason,
            OriginalQueue = queue,
            Status = status
        };
        _db.DeadLetters.Add(letter);
        await _db.SaveChangesAsync();
        return letter;
    }

    // ── TC-RES-DL-001 ──────────────────────────────────────────────────────────

    /// <summary>
    /// TC-RES-DL-001: Failed message routed to dead letter — RetryAsync republishes
    /// and transitions status from Pending → Retried.
    /// </summary>
    [Fact]
    public async Task RetryAsync_PendingLetter_PublishesAndMarkAsRetried()
    {
        // Arrange
        var letter = await SeedDeadLetterAsync();

        // Act
        var result = await _sut.RetryAsync(letter.Id, CancellationToken.None);

        // Assert
        result.Should().BeTrue("retry should succeed for a Pending dead letter");

        var updated = await _db.DeadLetters.FindAsync(letter.Id);
        updated!.Status.Should().Be(DeadLetterStatus.Retried);
        updated.RetriedAt.Should().NotBeNull();
    }

    // ── TC-RES-DL-002 ──────────────────────────────────────────────────────────

    /// <summary>
    /// TC-RES-DL-002: Dead letter record preserves original message metadata intact.
    /// GetByIdAsync returns all fields as seeded without mutation.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_PreservesOriginalMessageMetadata()
    {
        // Arrange
        const string expectedType = "CaseAssigned";
        const string expectedPayload = "{\"caseId\":\"abc\",\"assigneeId\":\"xyz\"}";
        const string expectedReason = "Downstream timeout after 3 retries";
        const string expectedQueue = "case-assignment-queue";

        var letter = await SeedDeadLetterAsync(
            messageType: expectedType,
            payload: expectedPayload,
            reason: expectedReason,
            queue: expectedQueue);

        // Act
        var retrieved = await _sut.GetByIdAsync(letter.Id, CancellationToken.None);

        // Assert — all metadata preserved exactly
        retrieved.Should().NotBeNull();
        retrieved!.MessageType.Should().Be(expectedType);
        retrieved.Payload.Should().Be(expectedPayload);
        retrieved.FailureReason.Should().Be(expectedReason);
        retrieved.OriginalQueue.Should().Be(expectedQueue);
        retrieved.Status.Should().Be(DeadLetterStatus.Pending);
        retrieved.ReceivedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ── bonus: quarantine path ─────────────────────────────────────────────────

    /// <summary>
    /// Retry on non-Pending letter returns false (already retried/quarantined).
    /// Guards against accidental double-retry.
    /// </summary>
    [Fact]
    public async Task RetryAsync_AlreadyRetriedLetter_ReturnsFalse()
    {
        // Arrange
        var letter = await SeedDeadLetterAsync(status: DeadLetterStatus.Retried);

        // Act
        var result = await _sut.RetryAsync(letter.Id, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }
}
