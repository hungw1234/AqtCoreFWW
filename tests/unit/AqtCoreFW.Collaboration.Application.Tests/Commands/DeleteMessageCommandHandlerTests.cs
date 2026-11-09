using AqtCoreFW.Collaboration.Application.Commands.DeleteMessage;
using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Collaboration.Application.Tests.Commands;

/// <summary>
/// Unit tests for DeleteMessageCommandHandler — covers the ownership gate: only the message author
/// may delete it (closes the same-tenant horizontal IDOR where any user could delete any message).
/// </summary>
public sealed class DeleteMessageCommandHandlerTests
{
    private readonly IMessageRepository _messageRepo = Substitute.For<IMessageRepository>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly DeleteMessageCommandHandler _handler;

    private static readonly Guid MessageId = Guid.NewGuid();
    private static readonly Guid AuthorId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    public DeleteMessageCommandHandlerTests()
    {
        _guidGen.NewIdMonotonic().Returns(Guid.NewGuid());
        _handler = new DeleteMessageCommandHandler(_messageRepo);
    }

    private Message MakeMessage(Guid senderId)
    {
        var msg = Message.Create(_guidGen, conversationId: 1L, senderId, "hi", ContentType.Text, TenantId, replyToId: null);
        msg.ClearDomainEvents();
        return msg;
    }

    [Fact]
    public async Task Handle_AuthorDeletes_SoftDeletesAndSucceeds()
    {
        var msg = MakeMessage(AuthorId);
        _messageRepo.GetByPublicIdAsync(MessageId, Arg.Any<CancellationToken>()).Returns(msg);

        var result = await _handler.Handle(new DeleteMessageCommand(MessageId, AuthorId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        msg.IsMessageDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NonAuthor_ReturnsForbiddenAndDoesNotDelete()
    {
        var msg = MakeMessage(AuthorId);
        _messageRepo.GetByPublicIdAsync(MessageId, Arg.Any<CancellationToken>()).Returns(msg);

        var result = await _handler.Handle(
            new DeleteMessageCommand(MessageId, Guid.NewGuid()), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
        msg.IsMessageDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MessageNotFound_ReturnsFail()
    {
        _messageRepo.GetByPublicIdAsync(MessageId, Arg.Any<CancellationToken>()).Returns((Message?)null);

        var result = await _handler.Handle(
            new DeleteMessageCommand(MessageId, AuthorId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }
}
