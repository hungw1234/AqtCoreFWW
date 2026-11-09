using AqtCoreFW.Collaboration.Application.Commands.ArchiveConversation;
using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Collaboration.Application.Tests.Commands;

/// <summary>
/// Unit tests for ArchiveConversationCommandHandler.
/// Covers the Owner-only authorization gate (IsArchived is an aggregate-wide flag), not-found, and
/// the happy path (archive flushed).
/// </summary>
public sealed class ArchiveConversationCommandHandlerTests
{
    private readonly IConversationRepository _repo;
    private readonly IGuidGenerator _guidGen;
    private readonly ArchiveConversationCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid ConversationId = Guid.NewGuid();

    public ArchiveConversationCommandHandlerTests()
    {
        _repo = Substitute.For<IConversationRepository>();
        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _handler = new ArchiveConversationCommandHandler(_repo);
    }

    private Conversation MakeConversation()
    {
        var conv = Conversation.Create(_guidGen, ConversationType.Group, "Test", OwnerId, TenantId);
        conv.AddMember(ConversationMember.Create(conv.Id, OwnerId, MemberRole.Owner));
        conv.ClearDomainEvents();
        return conv;
    }

    // --- Happy path: owner archives ---

    [Fact]
    public async Task Handle_OwnerArchives_PersistsAndSucceeds()
    {
        var conv = MakeConversation();
        _repo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>()).Returns(conv);

        var cmd = new ArchiveConversationCommand(ConversationId, OwnerId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        conv.IsArchived.Should().BeTrue();
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Authorization: non-owner member is rejected ---

    [Fact]
    public async Task Handle_CallerIsNonOwnerMember_ReturnsForbidden()
    {
        var conv = MakeConversation();
        var plainMember = Guid.NewGuid();
        conv.AddMember(ConversationMember.Create(conv.Id, plainMember, MemberRole.Member));
        _repo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>()).Returns(conv);

        var cmd = new ArchiveConversationCommand(ConversationId, plainMember);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
        conv.IsArchived.Should().BeFalse();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Authorization: non-member caller is rejected ---

    [Fact]
    public async Task Handle_CallerNotAMember_ReturnsForbidden()
    {
        var conv = MakeConversation();
        _repo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>()).Returns(conv);

        var cmd = new ArchiveConversationCommand(ConversationId, Guid.NewGuid());
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
    }

    // --- Not found ---

    [Fact]
    public async Task Handle_ConversationNotFound_ReturnsFail()
    {
        _repo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        var cmd = new ArchiveConversationCommand(ConversationId, OwnerId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }
}
