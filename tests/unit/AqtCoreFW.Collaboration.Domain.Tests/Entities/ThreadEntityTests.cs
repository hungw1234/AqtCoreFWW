using AqtCoreFW.Collaboration.Domain.Entities;
using FluentAssertions;
using DomainThread = AqtCoreFW.Collaboration.Domain.Entities.Thread;

namespace AqtCoreFW.Collaboration.Domain.Tests.Entities;

/// <summary>
/// Unit tests for Thread entity.
/// Verifies factory and IncrementReplyCount behaviour.
/// ParentMessageId/ConversationId: long (FK to T2 internal PKs).
/// </summary>
public sealed class ThreadEntityTests
{
    // ParentMessageId: long (FK to T2 Message.Id internal PK)
    private const long ParentMessageInternalId = 5L;
    // ConversationId: long (FK to T2 Conversation.Id internal PK)
    private const long ConversationInternalId = 1L;

    private static DomainThread CreateThread() =>
        DomainThread.Create(ParentMessageInternalId, ConversationInternalId);

    // --- Factory ---

    [Fact]
    public void Create_SetsAllProperties()
    {
        var thread = CreateThread();
        thread.ParentMessageId.Should().Be(ParentMessageInternalId);
        thread.ConversationId.Should().Be(ConversationInternalId);
        thread.ReplyCount.Should().Be(0);
        thread.LastReplyAt.Should().BeNull();
    }

    // --- IncrementReplyCount ---

    [Fact]
    public void IncrementReplyCount_IncrementsCountByOne()
    {
        var thread = CreateThread();
        thread.IncrementReplyCount(DateTime.UtcNow);
        thread.ReplyCount.Should().Be(1);
    }

    [Fact]
    public void IncrementReplyCount_SetsLastReplyAt()
    {
        var thread = CreateThread();
        var replyAt = DateTime.UtcNow;
        thread.IncrementReplyCount(replyAt);
        thread.LastReplyAt.Should().Be(replyAt);
    }

    [Fact]
    public void IncrementReplyCount_CalledMultipleTimes_AccumulatesCount()
    {
        var thread = CreateThread();
        var t1 = DateTime.UtcNow;
        var t2 = t1.AddSeconds(10);
        thread.IncrementReplyCount(t1);
        thread.IncrementReplyCount(t2);
        thread.ReplyCount.Should().Be(2);
        thread.LastReplyAt.Should().Be(t2);
    }
}
