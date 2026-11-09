using AqtCoreFW.Collaboration.Application.Commands.SendMessage;
using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Collaboration.Application.Tests.Commands;

/// <summary>
/// Unit tests for SendMessageCommandHandler.
/// Verifies message creation, conversation last-message update, and event publication.
/// </summary>
public sealed class SendMessageCommandHandlerTests
{
    private readonly IMessageRepository _messageRepo;
    private readonly IConversationRepository _conversationRepo;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IIdentityModuleClient _identityClient;
    private readonly IGuidGenerator _guidGen;
    private readonly SendMessageCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ConversationId = Guid.NewGuid();
    private static readonly Guid SenderId = Guid.NewGuid();

    public SendMessageCommandHandlerTests()
    {
        _messageRepo = Substitute.For<IMessageRepository>();
        _conversationRepo = Substitute.For<IConversationRepository>();
        _eventPublisher = Substitute.For<IDomainEventPublisher>();
        _identityClient = Substitute.For<IIdentityModuleClient>();
        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(Guid.NewGuid());
        _identityClient.GetUserInfoByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserInfoDto>
            {
                [SenderId] = new() { UserId = SenderId, FullName = "Test User" }
            });
        _handler = new SendMessageCommandHandler(_messageRepo, _conversationRepo, _eventPublisher, _identityClient, _guidGen);
    }

    private Conversation MakeConversation()
    {
        var conv = Conversation.Create(_guidGen, ConversationType.Group, "Test", SenderId, TenantId);
        // The sender must be a participant — the handler now rejects non-members (ForbiddenError).
        conv.AddMember(ConversationMember.Create(conv.Id, SenderId, MemberRole.Owner));
        conv.ClearDomainEvents();
        return conv;
    }

    private SendMessageCommand BuildCommand(string content = "Hello!") =>
        new(ConversationId, SenderId, content, ContentType.Text, TenantId);

    // --- Happy path ---

    [Fact]
    public async Task Handle_ConversationExists_ReturnsSuccess()
    {
        var conv = MakeConversation();
        _conversationRepo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>())
            .Returns(conv);

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ConversationExists_ReturnsDtoWithContent()
    {
        var conv = MakeConversation();
        _conversationRepo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>())
            .Returns(conv);

        var result = await _handler.Handle(BuildCommand("Hello!"), CancellationToken.None);
        result.Value.Content.Should().Be("Hello!");
    }

    [Fact]
    public async Task Handle_ConversationExists_PersistsMessage()
    {
        var conv = MakeConversation();
        _conversationRepo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>())
            .Returns(conv);

        await _handler.Handle(BuildCommand(), CancellationToken.None);
        await _messageRepo.Received(1).AddAsync(
            Arg.Is<Message>(m => m.Content == "Hello!" && m.SenderId == SenderId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConversationExists_PublishesDomainEvents()
    {
        var conv = MakeConversation();
        _conversationRepo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>())
            .Returns(conv);

        await _handler.Handle(BuildCommand(), CancellationToken.None);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Any<IEnumerable<IDomainEvent>>(),
            Arg.Any<CancellationToken>());
    }

    // --- Not found ---

    [Fact]
    public async Task Handle_ConversationNotFound_ReturnsFail()
    {
        _conversationRepo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(ConversationId.ToString()));
    }

    // --- Authorization: non-member sender ---

    [Fact]
    public async Task Handle_SenderNotAMember_ReturnsForbidden()
    {
        // Conversation exists but the sender was never added as a participant.
        var conv = Conversation.Create(_guidGen, ConversationType.Group, "Test", Guid.NewGuid(), TenantId);
        conv.ClearDomainEvents();
        _conversationRepo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>())
            .Returns(conv);

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
        await _messageRepo.DidNotReceive().AddAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
    }
}
