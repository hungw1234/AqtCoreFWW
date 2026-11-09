using AqtCoreFW.Collaboration.Application.Commands.MarkAsRead;
using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Events;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Collaboration.Application.Tests.Commands;

/// <summary>
/// Unit tests for MarkAsReadCommandHandler.
/// Covers membership gate, not-found, and the read-receipt persist + realtime-event behavior
/// (first read publishes once; re-mark is idempotent and publishes nothing).
/// </summary>
public sealed class MarkAsReadCommandHandlerTests
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IMessageRepository _messageRepo;
    private readonly IMessageReadStateRepository _readStateRepo;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IGuidGenerator _guidGen;
    private readonly MarkAsReadCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ConversationId = Guid.NewGuid();
    private static readonly Guid MessageId = Guid.NewGuid();

    public MarkAsReadCommandHandlerTests()
    {
        _conversationRepo = Substitute.For<IConversationRepository>();
        _messageRepo = Substitute.For<IMessageRepository>();
        _readStateRepo = Substitute.For<IMessageReadStateRepository>();
        _eventPublisher = Substitute.For<IDomainEventPublisher>();
        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _guidGen.NewIdRandom().Returns(_ => Guid.NewGuid());
        _handler = new MarkAsReadCommandHandler(
            _conversationRepo, _messageRepo, _readStateRepo, _eventPublisher, _guidGen);
    }

    private Conversation MakeConversation()
    {
        var conv = Conversation.Create(_guidGen, ConversationType.Group, "Test", UserId, TenantId);
        // The caller must be a participant — the handler rejects non-members (ForbiddenError).
        conv.AddMember(ConversationMember.Create(conv.Id, UserId, MemberRole.Owner));
        conv.ClearDomainEvents();
        return conv;
    }

    private Message MakeMessage(Conversation conv)
    {
        var msg = Message.Create(_guidGen, conv.Id, UserId, "hi", ContentType.Text, conv.TenantId);
        msg.ClearDomainEvents();
        return msg;
    }

    private void Arrange(Conversation conv, Message msg)
    {
        _conversationRepo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>()).Returns(conv);
        _messageRepo.GetByPublicIdAsync(MessageId, Arg.Any<CancellationToken>()).Returns(msg);
    }

    // --- Happy path: first read persists a receipt and publishes the realtime event ---

    [Fact]
    public async Task Handle_FirstRead_PersistsReceiptAndPublishesEvent()
    {
        var conv = MakeConversation();
        Arrange(conv, MakeMessage(conv));
        _readStateRepo.AddIfNotExistsAsync(Arg.Any<MessageReadState>(), Arg.Any<CancellationToken>())
            .Returns(true); // newly inserted

        var result = await _handler.Handle(new MarkAsReadCommand(ConversationId, MessageId, UserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _readStateRepo.Received(1).AddIfNotExistsAsync(Arg.Any<MessageReadState>(), Arg.Any<CancellationToken>());
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(e => e.OfType<MessageReadEvent>().Any()),
            Arg.Any<CancellationToken>());
    }

    // --- Idempotency: re-marking an already-read message publishes nothing ---

    [Fact]
    public async Task Handle_AlreadyRead_DoesNotPublishEvent()
    {
        var conv = MakeConversation();
        Arrange(conv, MakeMessage(conv));
        _readStateRepo.AddIfNotExistsAsync(Arg.Any<MessageReadState>(), Arg.Any<CancellationToken>())
            .Returns(false); // already existed

        var result = await _handler.Handle(new MarkAsReadCommand(ConversationId, MessageId, UserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _eventPublisher.DidNotReceive().PublishAsync(
            Arg.Any<IEnumerable<IDomainEvent>>(), Arg.Any<CancellationToken>());
    }

    // --- Not found ---

    [Fact]
    public async Task Handle_ConversationNotFound_ReturnsFail()
    {
        _conversationRepo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        var result = await _handler.Handle(new MarkAsReadCommand(ConversationId, MessageId, UserId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    // --- Authorization: non-member caller ---

    [Fact]
    public async Task Handle_CallerNotAMember_ReturnsForbidden()
    {
        var conv = MakeConversation(); // members: UserId only
        Arrange(conv, MakeMessage(conv));

        var cmd = new MarkAsReadCommand(ConversationId, MessageId, Guid.NewGuid()); // a different (non-member) user
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
        await _readStateRepo.DidNotReceive().AddIfNotExistsAsync(Arg.Any<MessageReadState>(), Arg.Any<CancellationToken>());
    }
}
