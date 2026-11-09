using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using FluentAssertions;

namespace AqtCoreFW.Collaboration.Domain.Tests.Entities;

/// <summary>
/// Unit tests for ConversationMember entity.
/// Verifies factory, Mute/Unmute, and UpdateLastRead behaviours.
/// ConversationId is long (FK to T2 Conversation.Id internal PK).
/// LastReadMessageId is long? (FK to T2 Message.Id internal PK).
/// </summary>
public sealed class ConversationMemberEntityTests
{
    // ConversationId: long (FK to T2 Conversation.Id internal PK)
    private const long ConversationInternalId = 1L;
    private static readonly Guid UserId = Guid.NewGuid();

    private static ConversationMember CreateMember(MemberRole role = MemberRole.Member) =>
        ConversationMember.Create(ConversationInternalId, UserId, role);

    // --- Factory ---

    [Fact]
    public void Create_SetsAllProperties()
    {
        var member = CreateMember(MemberRole.Owner);
        member.ConversationId.Should().Be(ConversationInternalId);
        member.UserId.Should().Be(UserId);
        member.Role.Should().Be(MemberRole.Owner);
        member.MutedUntil.Should().BeNull();
        member.LastReadMessageId.Should().BeNull();
        member.JoinedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // --- Mute ---

    [Fact]
    public void Mute_SetsMutedUntil()
    {
        var member = CreateMember();
        var until = DateTime.UtcNow.AddHours(8);
        member.Mute(until);
        member.MutedUntil.Should().Be(until);
    }

    // --- Unmute ---

    [Fact]
    public void Unmute_ClearsMutedUntil()
    {
        var member = CreateMember();
        member.Mute(DateTime.UtcNow.AddHours(1));
        member.Unmute();
        member.MutedUntil.Should().BeNull();
    }

    [Fact]
    public void Unmute_WhenNotMuted_IsIdempotent()
    {
        var member = CreateMember();
        member.Unmute(); // should not throw
        member.MutedUntil.Should().BeNull();
    }

    // --- UpdateLastRead ---

    [Fact]
    public void UpdateLastRead_SetsLastReadMessageId()
    {
        var member = CreateMember();
        // LastReadMessageId is long? (FK to Message.Id internal PK)
        const long messageInternalId = 10L;
        member.UpdateLastRead(messageInternalId);
        member.LastReadMessageId.Should().Be(messageInternalId);
    }

    [Fact]
    public void UpdateLastRead_CalledTwice_UsesLatestMessageId()
    {
        var member = CreateMember();
        const long first = 10L;
        const long second = 20L;
        member.UpdateLastRead(first);
        member.UpdateLastRead(second);
        member.LastReadMessageId.Should().Be(second);
    }
}
