using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Collaboration.Domain.Tests.Entities;

/// <summary>
/// Unit tests for Conversation aggregate root.
/// Verifies factory, archive/unarchive, last-message-at, and domain event emission.
/// Pure in-process — no DB, no HTTP.
/// </summary>
public sealed class ConversationEntityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    /// <summary>Creates a stub IGuidGenerator that returns a unique Guid on each NewIdMonotonic call.</summary>
    private static IGuidGenerator MakeGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(Guid.NewGuid());
        return gen;
    }

    private static Conversation CreateConversation(ConversationType type = ConversationType.Group) =>
        Conversation.Create(MakeGen(), type, "Test Chat", CreatedBy, TenantId);

    // --- Factory ---

    [Fact]
    public void Create_ValidParams_SetsAllProperties()
    {
        var conv = CreateConversation();
        conv.TenantId.Should().Be(TenantId);
        conv.CreatedBy.Should().Be(CreatedBy);
        conv.Type.Should().Be(ConversationType.Group);
        conv.Name.Should().Be("Test Chat");
        conv.IsArchived.Should().BeFalse();
        conv.LastMessageAt.Should().BeNull();
    }

    [Fact]
    public void Create_ValidParams_RaisesConversationCreatedEvent()
    {
        var conv = CreateConversation();
        conv.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ConversationCreatedEvent>();
    }

    [Fact]
    public void Create_ValidParams_EventHasCorrectIds()
    {
        var conv = CreateConversation();
        var evt = conv.DomainEvents.OfType<ConversationCreatedEvent>().Single();
        // Event carries PublicId (T2 AK), not internal long Id
        evt.ConversationPublicId.Should().Be(conv.PublicId);
        evt.CreatedBy.Should().Be(CreatedBy);
        evt.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Create_DirectType_TypeIsSet()
    {
        var conv = Conversation.Create(MakeGen(), ConversationType.Direct, null, CreatedBy, TenantId);
        conv.Type.Should().Be(ConversationType.Direct);
        conv.Name.Should().BeNull();
    }

    // --- Archive / Unarchive ---

    [Fact]
    public void Archive_WhenNotArchived_SetsIsArchivedTrue()
    {
        var conv = CreateConversation();
        conv.Archive();
        conv.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Archive_WhenAlreadyArchived_IsIdempotent()
    {
        var conv = CreateConversation();
        conv.Archive();
        conv.Archive(); // second call should not throw
        conv.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Unarchive_WhenArchived_SetsIsArchivedFalse()
    {
        var conv = CreateConversation();
        conv.Archive();
        conv.Unarchive();
        conv.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Unarchive_WhenNotArchived_IsIdempotent()
    {
        var conv = CreateConversation();
        conv.Unarchive(); // should not throw
        conv.IsArchived.Should().BeFalse();
    }

    // --- UpdateLastMessageAt ---

    [Fact]
    public void UpdateLastMessageAt_SetsTimestamp()
    {
        var conv = CreateConversation();
        var now = DateTime.UtcNow;
        conv.UpdateLastMessageAt(now);
        conv.LastMessageAt.Should().Be(now);
    }

    [Fact]
    public void UpdateLastMessageAt_CalledTwice_UsesLatestValue()
    {
        var conv = CreateConversation();
        var first = DateTime.UtcNow;
        var second = first.AddMinutes(5);
        conv.UpdateLastMessageAt(first);
        conv.UpdateLastMessageAt(second);
        conv.LastMessageAt.Should().Be(second);
    }

    // --- ClearDomainEvents ---

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var conv = CreateConversation();
        conv.ClearDomainEvents();
        conv.DomainEvents.Should().BeEmpty();
    }
}
