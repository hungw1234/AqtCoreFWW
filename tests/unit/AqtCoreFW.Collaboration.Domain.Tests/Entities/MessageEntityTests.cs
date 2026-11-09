using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Collaboration.Domain.Tests.Entities;

/// <summary>
/// Unit tests for Message aggregate root.
/// Verifies factory, edit, soft-delete, and domain event emission.
/// </summary>
public sealed class MessageEntityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    // ConversationId is long (FK to T2 Conversation.Id internal PK)
    private const long ConversationInternalId = 42L;
    private static readonly Guid SenderId = Guid.NewGuid();

    /// <summary>Creates a stub IGuidGenerator returning a unique Guid on NewIdMonotonic.</summary>
    private static IGuidGenerator MakeGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(Guid.NewGuid());
        return gen;
    }

    private static Message CreateMessage(string content = "Hello world") =>
        Message.Create(MakeGen(), ConversationInternalId, SenderId, content, ContentType.Text, TenantId);

    // --- Factory ---

    [Fact]
    public void Create_ValidParams_SetsAllProperties()
    {
        var msg = CreateMessage();
        msg.ConversationId.Should().Be(ConversationInternalId);
        msg.SenderId.Should().Be(SenderId);
        msg.Content.Should().Be("Hello world");
        msg.ContentType.Should().Be(ContentType.Text);
        msg.TenantId.Should().Be(TenantId);
        msg.IsMessageDeleted.Should().BeFalse();
        msg.IsSystemMessage.Should().BeFalse();
        msg.EditedAt.Should().BeNull();
        msg.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_ValidParams_RaisesMessageSentEvent()
    {
        var msg = CreateMessage();
        msg.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<MessageSentEvent>();
    }

    [Fact]
    public void Create_ValidParams_EventHasCorrectIds()
    {
        var msg = CreateMessage();
        var evt = msg.DomainEvents.OfType<MessageSentEvent>().Single();
        // Event carries PublicId (T2 AK) + long internal ConversationId
        evt.MessagePublicId.Should().Be(msg.PublicId);
        evt.ConversationInternalId.Should().Be(ConversationInternalId);
        evt.SenderId.Should().Be(SenderId);
        evt.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Create_SystemContentType_SetsIsSystemMessageTrue()
    {
        var msg = Message.Create(MakeGen(), ConversationInternalId, SenderId, "system msg", ContentType.System, TenantId);
        msg.IsSystemMessage.Should().BeTrue();
    }

    [Fact]
    public void Create_WithReplyToId_SetsReplyToId()
    {
        // ReplyToId is long? (FK to Message.Id internal PK)
        const long replyToInternalId = 99L;
        var msg = Message.Create(MakeGen(), ConversationInternalId, SenderId, "reply", ContentType.Text, TenantId, replyToInternalId);
        msg.ReplyToId.Should().Be(replyToInternalId);
    }

    // --- Edit ---

    [Fact]
    public void Edit_ValidContent_UpdatesContentAndEditedAt()
    {
        var msg = CreateMessage();
        msg.Edit("Updated content");
        msg.Content.Should().Be("Updated content");
        msg.EditedAt.Should().NotBeNull();
    }

    [Fact]
    public void Edit_AfterSoftDelete_ThrowsInvalidOperation()
    {
        var msg = CreateMessage();
        msg.SoftDelete();
        var act = () => msg.Edit("new content");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*deleted*");
    }

    // --- SoftDelete ---

    [Fact]
    public void SoftDelete_SetsDeletedAtAndIsDeleted()
    {
        var msg = CreateMessage();
        msg.SoftDelete();
        msg.IsMessageDeleted.Should().BeTrue();
        msg.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void SoftDelete_CalledTwice_IsIdempotent()
    {
        var msg = CreateMessage();
        msg.SoftDelete();
        var deletedAt = msg.DeletedAt;
        msg.SoftDelete();
        msg.DeletedAt.Should().Be(deletedAt); // unchanged
    }

    // --- ClearDomainEvents ---

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var msg = CreateMessage();
        msg.ClearDomainEvents();
        msg.DomainEvents.Should().BeEmpty();
    }
}
